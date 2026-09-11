using System.Diagnostics;
using System.IO;
using Microsoft.Data.Sqlite;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Ui;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Verification;

[Collection(EnvironmentVariableCollection.Name)]
public sealed class WorkstationAutomationLeaseTests
{
    private const string ChildMode = "PF_R2_LEASE_CHILD_MODE";
    private const string ChildStore = "PF_R2_LEASE_CHILD_STORE";
    private const string ChildResource = "PF_R2_LEASE_CHILD_RESOURCE";
    private const string ChildBusinessDatabase = "PF_R2_LEASE_CHILD_BUSINESS_DB";
    private const string ChildReady = "PF_R2_LEASE_CHILD_READY";
    private const string ChildRelease = "PF_R2_LEASE_CHILD_RELEASE";
    private const string ChildExternal = "PF_R2_LEASE_CHILD_EXTERNAL";

    [Fact]
    public async Task Constructing_and_observing_a_missing_authority_are_physically_read_only()
    {
        using TempWorkspace files = new();
        string parent = Path.Combine(files.Root, "authority", "nested");
        string store = Path.Combine(parent, "lease.db");

        SqliteWorkstationAutomationLeaseManager manager = new(store, "test.passive");

        Directory.Exists(parent).ShouldBeFalse();
        (await manager.ObserveAsync(null, CancellationToken.None)).Status
            .ShouldBe(WorkstationAutomationLeaseStatus.Unknown);
        Directory.Exists(parent).ShouldBeFalse();
        File.Exists(store).ShouldBeFalse();
    }

    [Fact]
    public async Task Different_business_databases_share_one_SessionService_external_authority()
    {
        using TempWorkspace leaseFiles = new();
        using SessionServiceHarness firstHarness = new();
        using SessionServiceHarness secondHarness = new();
        string store = Path.Combine(leaseFiles.Root, "lease.db");
        string resource = "test.two-databases." + Guid.NewGuid().ToString("N");
        SqliteWorkstationAutomationLeaseManager firstManager = new(store, resource);
        SqliteWorkstationAutomationLeaseManager secondManager = new(store, resource);
        BlockingPsdProcessor firstAdapter = new(firstHarness.FileWorkspace, block: true);
        BlockingPsdProcessor secondAdapter = new(secondHarness.FileWorkspace, block: false);
        ISessionService first = firstHarness.CreateServiceWithPhotoshop(
            firstAdapter, environmentGate: PermissiveEnvironmentGate.Instance,
            automationLeases: firstManager);
        ISessionService second = secondHarness.CreateServiceWithPhotoshop(
            secondAdapter, environmentGate: PermissiveEnvironmentGate.Instance,
            automationLeases: secondManager);

        SessionId firstId = await ImportPsdAsync(firstHarness, first);
        SessionId secondId = await ImportPsdAsync(secondHarness, second);
        Task<OperationResult<SessionView>> firstRun = first.ExecuteAsync(
            firstId, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", CancellationToken.None);
        await firstAdapter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        OperationResult<SessionView> refused = await second.ExecuteAsync(
            secondId, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", CancellationToken.None);
        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.AdapterUnavailable);
        secondAdapter.Calls.ShouldBe(0);
        (await secondHarness.Repository.LoadAsync(secondId, CancellationToken.None)).Value!.Attempts
            .ShouldNotContain(attempt => attempt.Operation == OperationKind.PreparePsd);

        firstAdapter.Release.TrySetResult();
        (await firstRun).IsSuccess.ShouldBeTrue();
        OperationResult<SessionView> afterRelease = await second.ExecuteAsync(
            secondId, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", CancellationToken.None);
        afterRelease.IsSuccess.ShouldBeTrue(afterRelease.IsFailure ? afterRelease.Failure.ToString() : "");
        secondAdapter.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Independent_processes_exclude_across_business_databases_and_reacquire_after_release()
    {
        if (Environment.GetEnvironmentVariable(ChildMode) is not null) return;
        using TempWorkspace files = new();
        string store = Path.Combine(files.Root, "lease.db");
        string resource = "test.processes." + Guid.NewGuid().ToString("N");
        string ready = Path.Combine(files.Root, "holder.ready");
        string release = Path.Combine(files.Root, "holder.release");
        string external = Path.Combine(files.Root, "external.log");
        Process? holder = null;
        Process? refused = null;
        Process? successor = null;
        try
        {
            holder = StartChild(
                "hold", store, resource, Path.Combine(files.Root, "holder-business.db"), ready, release, external);
            await WaitForFileAsync(ready, holder, TimeSpan.FromSeconds(20));
            refused = StartChild(
                "expect-refused", store, resource, Path.Combine(files.Root, "refused-business.db"),
                Path.Combine(files.Root, "refused.ready"), release, external);
            await WaitForExitAsync(refused, TimeSpan.FromSeconds(30));
            refused.ExitCode.ShouldBe(0, await ReadFailureAsync(refused));
            File.ReadAllLines(external).ShouldBe(["hold"]);

            File.WriteAllText(release, "release");
            await WaitForExitAsync(holder, TimeSpan.FromSeconds(30));
            holder.ExitCode.ShouldBe(0, await ReadFailureAsync(holder));
            successor = StartChild(
                "expect-acquired", store, resource, Path.Combine(files.Root, "successor-business.db"),
                Path.Combine(files.Root, "successor.ready"), release, external);
            await WaitForExitAsync(successor, TimeSpan.FromSeconds(30));
            successor.ExitCode.ShouldBe(0, await ReadFailureAsync(successor));
            File.ReadAllLines(external).ShouldBe(["hold", "expect-acquired"]);
        }
        finally
        {
            KillOwnedProcess(successor);
            KillOwnedProcess(refused);
            KillOwnedProcess(holder);
            successor?.Dispose();
            refused?.Dispose();
            holder?.Dispose();
        }
    }

    [Fact]
    public async Task Crashed_controller_is_recovered_only_after_its_owned_process_exits()
    {
        if (Environment.GetEnvironmentVariable(ChildMode) is not null) return;
        using TempWorkspace files = new();
        string store = Path.Combine(files.Root, "lease.db");
        string resource = "test.crash." + Guid.NewGuid().ToString("N");
        string ready = Path.Combine(files.Root, "crash.ready");
        string release = Path.Combine(files.Root, "unused.release");
        string external = Path.Combine(files.Root, "external.log");
        Process? crashed = null;
        try
        {
            crashed = StartChild(
                "crash", store, resource, Path.Combine(files.Root, "crash-business.db"),
                ready, release, external);
            await WaitForFileAsync(ready, crashed, TimeSpan.FromSeconds(20));
            await WaitForExitAsync(crashed, TimeSpan.FromSeconds(30));
            crashed.ExitCode.ShouldNotBe(0);

            SqliteWorkstationAutomationLeaseManager successor = new(store, resource);
            OperationResult<IWorkstationAutomationLease> acquired = await successor
                .TryAcquireAsync(null, CancellationToken.None);
            acquired.IsSuccess.ShouldBeTrue(acquired.IsFailure ? acquired.Failure.ToString() : "");
            (await acquired.Value.ReleaseAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
            File.ReadAllLines(external).ShouldBe(["crash"]);
        }
        finally
        {
            KillOwnedProcess(crashed);
            crashed?.Dispose();
        }
    }

    [Fact]
    public async Task Explicit_nested_scope_cannot_release_outer_and_unrelated_same_process_operation_is_refused()
    {
        using LeaseStore leaseStore = new();
        SqliteWorkstationAutomationLeaseManager manager = leaseStore.Manager();
        IWorkstationAutomationLease outer = (await manager.TryAcquireAsync(null, CancellationToken.None)).Value;
        IWorkstationAutomationLease nested = (await manager.TryAcquireAsync(outer, CancellationToken.None)).Value;
        (await nested.ReleaseAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await manager.ObserveAsync(outer, CancellationToken.None)).Status
            .ShouldBe(WorkstationAutomationLeaseStatus.Owned);
        (await manager.TryAcquireAsync(null, CancellationToken.None)).IsFailure.ShouldBeTrue();
        (await outer.ReleaseAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await manager.ObserveAsync(null, CancellationToken.None)).Status
            .ShouldBe(WorkstationAutomationLeaseStatus.Free);
    }

    [Fact]
    public async Task Releasing_one_handle_concurrently_is_idempotent_and_does_not_release_a_nested_owner()
    {
        using LeaseStore leaseStore = new();
        SqliteWorkstationAutomationLeaseManager manager = leaseStore.Manager();
        IWorkstationAutomationLease outer = (await manager.TryAcquireAsync(null, CancellationToken.None)).Value;
        IWorkstationAutomationLease nested = (await manager.TryAcquireAsync(outer, CancellationToken.None)).Value;
        OperationResult<PrintFlow.Domain.Results.Unit>[] duplicate = await Task.WhenAll(
            outer.ReleaseAsync(CancellationToken.None), outer.ReleaseAsync(CancellationToken.None));
        duplicate.ShouldAllBe(result => result.IsSuccess);
        (await manager.ObserveAsync(nested, CancellationToken.None)).Status
            .ShouldBe(WorkstationAutomationLeaseStatus.Owned);
        (await manager.TryAcquireAsync(null, CancellationToken.None)).IsFailure.ShouldBeTrue();

        (await nested.ReleaseAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        IWorkstationAutomationLease successor = (await manager.TryAcquireAsync(null, CancellationToken.None)).Value;
        (await outer.ReleaseAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await manager.ObserveAsync(successor, CancellationToken.None)).Status
            .ShouldBe(WorkstationAutomationLeaseStatus.Owned);
        await successor.ReleaseAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Missing_and_unreadable_authority_are_Unknown_not_Free()
    {
        using TempWorkspace files = new();
        SqliteWorkstationAutomationLeaseManager missing = new(
            Path.Combine(files.Root, "missing.db"), "test.missing");
        (await missing.ObserveAsync(null, CancellationToken.None)).Status
            .ShouldBe(WorkstationAutomationLeaseStatus.Unknown);
        string unreadable = Path.Combine(files.Root, "unreadable.db");
        File.WriteAllText(unreadable, "not sqlite");
        SqliteWorkstationAutomationLeaseManager malformed = new(unreadable, "test.unreadable");
        (await malformed.ObserveAsync(null, CancellationToken.None)).Status
            .ShouldBe(WorkstationAutomationLeaseStatus.Unknown);
        (await malformed.TryAcquireAsync(null, CancellationToken.None)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Unverifiable_live_owner_is_never_evicted_and_release_failure_never_reports_Free()
    {
        using TempWorkspace files = new();
        string store = Path.Combine(files.Root, "lease.db");
        string resource = "test.unknown." + Guid.NewGuid().ToString("N");
        SqliteWorkstationAutomationLeaseManager.ControllerIdentity firstIdentity = new(
            123, "synthetic-machine", "synthetic-controller", DateTimeOffset.UnixEpoch);
        SqliteWorkstationAutomationLeaseManager owner = new(
            store, resource, firstIdentity, new ConstantLiveness(ProcessLiveness.Unknown));
        IWorkstationAutomationLease held = (await owner.TryAcquireAsync(null, CancellationToken.None)).Value;
        SqliteWorkstationAutomationLeaseManager observer = new(
            store, resource,
            new SqliteWorkstationAutomationLeaseManager.ControllerIdentity(
                456, "other-machine", "other-controller", DateTimeOffset.UnixEpoch.AddSeconds(1)),
            new ConstantLiveness(ProcessLiveness.Unknown));

        (await observer.ObserveAsync(null, CancellationToken.None)).Status
            .ShouldBe(WorkstationAutomationLeaseStatus.Unknown);
        (await observer.TryAcquireAsync(null, CancellationToken.None)).IsFailure.ShouldBeTrue();

        using (SqliteConnection connection = new SqliteConnectionFactory(store).Open())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DROP TABLE WorkstationAutomationLease;";
            command.ExecuteNonQuery();
        }

        OperationResult<PrintFlow.Domain.Results.Unit> release =
            await held.ReleaseAsync(CancellationToken.None);
        release.IsFailure.ShouldBeTrue();
        (await observer.ObserveAsync(null, CancellationToken.None)).Status
            .ShouldBe(WorkstationAutomationLeaseStatus.Unknown);
    }

    [Fact]
    public async Task Fake_work_remains_available_while_the_same_physical_domain_is_held()
    {
        using LeaseStore leaseStore = new();
        using SessionServiceHarness harness = new();
        SqliteWorkstationAutomationLeaseManager holderManager = leaseStore.Manager();
        SqliteWorkstationAutomationLeaseManager serviceManager = leaseStore.Manager();
        IWorkstationAutomationLease held = (await holderManager.TryAcquireAsync(null, CancellationToken.None)).Value;
        ISessionService service = harness.CreateService(automationLeases: serviceManager);
        OperationResult<SessionView> imported = await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteSourcePng(), "fake-work", "qa", CancellationToken.None);
        imported.IsSuccess.ShouldBeTrue(imported.IsFailure ? imported.Failure.ToString() : "");
        OperationResult<SessionView> approved = await service.ExecuteAsync(
            imported.Value.Id,
            new WorkflowCommand.ConfirmOriginal(),
            "qa", CancellationToken.None);
        approved.IsSuccess.ShouldBeTrue(approved.IsFailure ? approved.Failure.ToString() : "");
        OperationResult<SessionView> started = await service.ExecuteAsync(
            imported.Value.Id, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "qa", CancellationToken.None);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Failure.ToString() : "");
        await held.ReleaseAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Opening_commit_failure_releases_physical_ownership_before_any_adapter_effect()
    {
        using LeaseStore leaseStore = new();
        using SessionServiceHarness harness = new();
        SqliteWorkstationAutomationLeaseManager manager = leaseStore.Manager();
        FaultingRepository repository = new(harness.Repository);
        BlockingPsdProcessor adapter = new(harness.FileWorkspace, block: false);
        OwnScopeReinspectionGate gate = new(repository);
        ISessionService service = harness.CreateServiceWithPhotoshop(
            adapter, gate, repository: repository, automationLeases: manager);
        SessionId id = await ImportPsdAsync(harness, service);
        repository.FailFromCommit = repository.AttemptedCommits + 1;

        OperationResult<SessionView> result = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation),
            "qa", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PersistenceError);
        gate.ScopedCalls.ShouldBe(1);
        gate.LeaseWasActive.ShouldBeTrue();
        gate.DatabaseWasUnclaimed.ShouldBeTrue();
        adapter.Calls.ShouldBe(0);
        (await manager.ObserveAsync(null, CancellationToken.None)).Status
            .ShouldBe(WorkstationAutomationLeaseStatus.Free);
        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
        (await harness.Repository.FindRunningAttemptsAsync(CancellationToken.None)).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Closing_commit_failure_retains_running_history_but_releases_physical_ownership()
    {
        using LeaseStore leaseStore = new();
        using SessionServiceHarness harness = new();
        SqliteWorkstationAutomationLeaseManager manager = leaseStore.Manager();
        FaultingRepository repository = new(harness.Repository);
        BlockingPsdProcessor adapter = new(harness.FileWorkspace, block: false);
        ISessionService service = harness.CreateServiceWithPhotoshop(
            adapter, PermissiveEnvironmentGate.Instance,
            repository: repository, automationLeases: manager);
        SessionId id = await ImportPsdAsync(harness, service);
        repository.FailFromCommit = repository.AttemptedCommits + 2;

        OperationResult<SessionView> result = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation),
            "qa", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PersistenceError);
        adapter.Calls.ShouldBe(1);
        (await manager.ObserveAsync(null, CancellationToken.None)).Status
            .ShouldBe(WorkstationAutomationLeaseStatus.Free);
        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeTrue();
        (await harness.Repository.FindRunningAttemptsAsync(CancellationToken.None)).Value
            .ShouldHaveSingleItem();

        SqliteWorkstationAutomationLeaseManager successorManager = leaseStore.Manager();
        OperationResult<IWorkstationAutomationLease> successor = await successorManager
            .TryAcquireAsync(null, CancellationToken.None);
        successor.IsSuccess.ShouldBeTrue(successor.IsFailure ? successor.Failure.ToString() : "");
        (await successor.Value.ReleaseAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Cancellation_waits_for_adapter_unwind_and_closing_commit_before_releasing_ownership()
    {
        using LeaseStore leaseStore = new();
        using SessionServiceHarness harness = new();
        SqliteWorkstationAutomationLeaseManager manager = leaseStore.Manager();
        CancellingPsdProcessor adapter = new();
        ISessionService service = harness.CreateServiceWithPhotoshop(
            adapter, PermissiveEnvironmentGate.Instance, automationLeases: manager);
        SessionId id = await ImportPsdAsync(harness, service);
        using CancellationTokenSource cancellation = new();

        Task<OperationResult<SessionView>> running = service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation),
            "qa", cancellation.Token);
        await adapter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        (await manager.ObserveAsync(null, CancellationToken.None)).Status
            .ShouldBe(WorkstationAutomationLeaseStatus.Busy);
        cancellation.Cancel();
        await adapter.UnwindEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        SqliteWorkstationAutomationLeaseManager competitor = leaseStore.Manager();
        (await competitor.TryAcquireAsync(null, CancellationToken.None)).IsFailure.ShouldBeTrue(
            "cancellation is not surrender while the adapter's bounded unwind is still active");
        adapter.AllowUnwind.TrySetResult();
        OperationResult<SessionView> result = await running;

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.Cancelled);
        adapter.Unwound.Task.IsCompleted.ShouldBeTrue();
        (await manager.ObserveAsync(null, CancellationToken.None)).Status
            .ShouldBe(WorkstationAutomationLeaseStatus.Free);
        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
        (await harness.Repository.FindRunningAttemptsAsync(CancellationToken.None)).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Child_process_helper_executes_only_when_explicitly_owned_by_the_parent_test()
    {
        string? mode = Environment.GetEnvironmentVariable(ChildMode);
        if (mode is null) return;
        string store = Required(ChildStore);
        string resource = Required(ChildResource);
        string businessDatabase = Required(ChildBusinessDatabase);
        string ready = Required(ChildReady);
        string release = Required(ChildRelease);
        string external = Required(ChildExternal);
        SqliteConnectionFactory business = new(businessDatabase);
        using (SqliteConnection connection = business.Open())
            MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();

        SqliteWorkstationAutomationLeaseManager manager = new(store, resource);
        OperationResult<IWorkstationAutomationLease> acquired = await manager
            .TryAcquireAsync(null, CancellationToken.None);
        if (mode == "expect-refused")
        {
            acquired.IsFailure.ShouldBeTrue();
            return;
        }

        acquired.IsSuccess.ShouldBeTrue(acquired.IsFailure ? acquired.Failure.ToString() : "");
        IWorkstationAutomationLease lease = acquired.Value;
        File.AppendAllLines(external, [mode]);
        if (mode == "crash")
        {
            File.WriteAllText(ready, "ready");
            // Terminate the explicitly owned testhost without running cleanup/finally code,
            // leaving its committed owner row exactly as a controller crash would.
            Environment.Exit(17);
        }
        if (mode == "hold")
        {
            File.WriteAllText(ready, "ready");
            await WaitForFileAsync(release, process: null, TimeSpan.FromSeconds(25));
        }
        (await lease.ReleaseAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    private static async Task<SessionId> ImportPsdAsync(SessionServiceHarness harness, ISessionService service)
    {
        string source = harness.Workspace.CreateSourceFile(
            Guid.NewGuid().ToString("N") + ".psd", PsdInputPreparationTests.RgbCompositePsd());
        OperationResult<SessionView> imported = await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, source, null, "qa", CancellationToken.None);
        imported.IsSuccess.ShouldBeTrue(imported.IsFailure ? imported.Failure.ToString() : "");
        return imported.Value.Id;
    }

    private static Process StartChild(string mode, string store, string resource,
        string businessDatabase, string ready, string release, string external)
    {
        string root = FindRepositoryRoot();
        string project = Path.Combine(root, "tests", "PrintFlow.Tests", "PrintFlow.Tests.csproj");
        string dotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "dotnet", "dotnet.exe");
        ProcessStartInfo start = new(dotnet)
        {
            Arguments = $"test \"{project}\" -c Release --no-build --no-restore " +
                "--filter \"FullyQualifiedName=PrintFlow.Tests.Integration.Verification." +
                "WorkstationAutomationLeaseTests.Child_process_helper_executes_only_when_explicitly_owned_by_the_parent_test\"",
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string key in start.Environment.Keys
                     .Where(key => key.StartsWith("PRINTFLOW_", StringComparison.OrdinalIgnoreCase) ||
                                   key.StartsWith("PF_R2_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        start.Environment[ChildMode] = mode;
        start.Environment[ChildStore] = store;
        start.Environment[ChildResource] = resource;
        start.Environment[ChildBusinessDatabase] = businessDatabase;
        start.Environment[ChildReady] = ready;
        start.Environment[ChildRelease] = release;
        start.Environment[ChildExternal] = external;
        return Process.Start(start) ?? throw new InvalidOperationException("The owned lease child did not start.");
    }

    private static async Task WaitForFileAsync(string path, Process? process, TimeSpan timeout)
    {
        using CancellationTokenSource watchdog = new(timeout);
        while (!File.Exists(path))
        {
            if (process is { HasExited: true }) throw new InvalidOperationException(await ReadFailureAsync(process));
            await Task.Delay(20, watchdog.Token);
        }
    }

    private static async Task WaitForExitAsync(Process process, TimeSpan timeout)
    {
        using CancellationTokenSource watchdog = new(timeout);
        await process.WaitForExitAsync(watchdog.Token);
    }

    private static async Task<string> ReadFailureAsync(Process process) =>
        (await process.StandardOutput.ReadToEndAsync()) + Environment.NewLine + await process.StandardError.ReadToEndAsync();

    private static void KillOwnedProcess(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // The explicitly owned process exited between the check and the kill.
        }
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Missing owned child variable {name}.");

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrintFlowStudio.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the PrintFlow repository root.");
    }

    private sealed class LeaseStore : IDisposable
    {
        private readonly TempWorkspace _files = new();
        private readonly string _resource = "test.store." + Guid.NewGuid().ToString("N");
        internal SqliteWorkstationAutomationLeaseManager Manager() => new(
            Path.Combine(_files.Root, "lease.db"), _resource);
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            _files.Dispose();
        }
    }

    private sealed class BlockingPsdProcessor(IWorkspace workspace, bool block) : IPhotoshopOutputProcessor
    {
        public string AdapterId => "recording-production-psd";
        public AdapterExecutionMode Mode => AdapterExecutionMode.Production;
        public int Calls { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<OperationResult<AdapterOutput>> GenerateAsync(
            PhotoshopRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async Task<OperationResult<AdapterOutput>> PreparePsdAsync(
            PsdPreparationRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            Entered.TrySetResult();
            if (block) await Release.Task.WaitAsync(cancellationToken);
            string path = workspace.ResolveAbsolute(request.ExpectedOutput);
            File.WriteAllBytes(path, SyntheticImages.Png(4, 3));
            return OperationResult.Ok(new AdapterOutput(request.ExpectedOutput, TimeSpan.Zero, "recorded")
            {
                PsdInspection = new PsdInspection(4, 3, "RGB", 8, true, true,
                    [new("Red", "COMPONENT"), new("Green", "COMPONENT"), new("Blue", "COMPONENT")],
                    "recording-boundary"),
            });
        }
    }

    private sealed class CancellingPsdProcessor : IPhotoshopOutputProcessor
    {
        public string AdapterId => "cancelling-production-psd";
        public AdapterExecutionMode Mode => AdapterExecutionMode.Production;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource UnwindEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowUnwind { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Unwound { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<OperationResult<AdapterOutput>> GenerateAsync(
            PhotoshopRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<OperationResult<AdapterOutput>> PreparePsdAsync(
            PsdPreparationRequest request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The cancellation test unexpectedly resumed work.");
            }
            catch (OperationCanceledException)
            {
                UnwindEntered.TrySetResult();
                await AllowUnwind.Task.ConfigureAwait(false);
                throw;
            }
            finally
            {
                Unwound.TrySetResult();
            }
        }
    }

    private sealed class OwnScopeReinspectionGate(ISessionRepository repository)
        : IWorkstationScopedEnvironmentGate
    {
        public int ScopedCalls { get; private set; }
        public bool LeaseWasActive { get; private set; }
        public bool DatabaseWasUnclaimed { get; private set; }

        public OperationResult<PrintFlow.Domain.Results.Unit> Verify(AdapterExecutionMode mode) =>
            throw new InvalidOperationException("Production must use the explicit own-scope reinspection overload.");

        public OperationResult<PrintFlow.Domain.Results.Unit> Verify(
            AdapterExecutionMode mode,
            IWorkstationAutomationLease workstationLease)
        {
            ScopedCalls++;
            LeaseWasActive = workstationLease.IsActive;
            AutomationLockState lockState = repository.GetAutomationLockAsync(CancellationToken.None)
                .GetAwaiter().GetResult().Value;
            IReadOnlyList<ProcessingAttempt> running = repository
                .FindRunningAttemptsAsync(CancellationToken.None).GetAwaiter().GetResult().Value;
            DatabaseWasUnclaimed = !lockState.IsHeld && running.Count == 0;
            return OperationResult.Ok();
        }
    }

    private sealed class PermissiveEnvironmentGate : IEnvironmentGate
    {
        internal static readonly PermissiveEnvironmentGate Instance = new();
        public OperationResult<PrintFlow.Domain.Results.Unit> Verify(AdapterExecutionMode mode) =>
            OperationResult.Ok();
    }

    private sealed class ConstantLiveness(ProcessLiveness result)
        : SqliteWorkstationAutomationLeaseManager.ILeaseControllerLiveness
    {
        public ProcessLiveness Check(
            SqliteWorkstationAutomationLeaseManager.ControllerIdentity owner) => result;
    }
}
