using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// Epic 11300 final-gate blocker fix 2: the R3 failure family, at the boundary where it actually
/// killed the process.
/// </summary>
/// <remarks>
/// R3's crash was not really about two file dialogs. It was about an in-flight import being
/// cancelled and the resulting <c>TaskCanceledException</c> travelling all the way out through
/// <c>SessionService.ImportAsync</c> and <c>HomeViewModel</c> into an async void command
/// continuation, where WPF has nothing left to do but terminate. Reproducing two real Windows
/// dialogs would test the trigger; these tests test the defect — cancellation of a running import
/// comes back as an ordinary reported failure, and no exception crosses the application boundary.
/// </remarks>
public sealed class ImportCancellationShellBoundaryTests
{
    private const long LargeSourceBytes = 64L * 1024 * 1024;

    private static readonly TimeSpan RendezvousTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_cancelled_import_is_reported_by_the_service_and_writes_no_source_state()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        string source = CreateLargeSource(harness.Workspace, "shell-boundary.png");

        using CancellationTokenSource cancellation = new();
        Task<OperationResult<SessionView>> importing = Task.Run(() => service.ImportAsync(
            WorkflowType.PrepareAsset, source, "shell-boundary", "tester", cancellation.Token));

        CancelOnceBytesHaveLanded(cancellation, SessionsAreaOf(harness), importing)
            .ShouldBeTrue("the import must have been in flight before the cancellation");

        OperationResult<SessionView> imported = await importing;

        imported.IsFailure.ShouldBeTrue();
        imported.Failure.Code.ShouldBe(FailureCode.Cancelled);

        // The session row exists — it was committed before the copy began — but nothing about it
        // claims a source: the Import attempt is Failed, and no Revision and no InputSnapshot was
        // written from the half-copied bytes.
        SessionAggregate reloaded = (await harness.Repository.LoadAsync(
            await OnlySessionIdAsync(harness), CancellationToken.None)).Value!;

        reloaded.Attempts.Single(a => a.Step == StepKind.Import).Status.ShouldBe(AttemptStatus.Failed);
        reloaded.Revisions.ShouldBeEmpty();
        reloaded.Snapshot.ShouldBeNull();
        reloaded.Steps.Single(s => s.Step == StepKind.Import).State.ShouldBe(StepState.Failed);
    }

    /// <summary>
    /// The same cancellation, driven through the screen the operator actually uses.
    /// </summary>
    /// <remarks>
    /// <c>ChooseFileCommand</c> is an <see cref="IAsyncRelayCommand"/> built from a cancellable
    /// method, so cancelling it is exactly what a second execution of the same command did to the
    /// first in R3. Awaiting the command's task is where the shell would have died; here it
    /// completes, Home reports the failure in its notice line, and the operator stays on Home
    /// rather than being navigated into a session with no source.
    /// </remarks>
    [Fact]
    public async Task A_cancelled_import_leaves_Home_reporting_a_failure_rather_than_terminating()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = CreateLargeSource(harness.Inner.Workspace, "home-cancelled.png");

        Task running = harness.Home.ChooseFileCommand.ExecuteAsync(null);

        CancelOnceBytesHaveLanded(harness.Home.ChooseFileCommand, SessionsAreaOf(harness.Inner), running)
            .ShouldBeTrue("the import must have been in flight before the cancellation");

        // Before the fix this await threw TaskCanceledException out of the view model.
        await Should.NotThrowAsync(async () => await running);

        harness.Home.Notice.ShouldNotBeNull().ShouldContain(FailureCode.Cancelled.ToString());
        harness.Navigation.WorkflowSelectionFor.ShouldBeNull(
            "a cancelled import must not open a session screen for a source that was never established");
        harness.Home.IsBusy.ShouldBeFalse("the screen is usable again afterwards");
    }

    /// <summary>
    /// The ordinary single import still works end to end through the same screen.
    /// </summary>
    [Fact]
    public async Task An_ordinary_import_through_Home_still_establishes_the_session_and_navigates()
    {
        using HomeScreenHarness harness = new();
        string source = harness.WriteSourceFile("ordinary.png");
        byte[] before = File.ReadAllBytes(source);
        harness.FilePicker.Path = source;

        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        harness.Home.Notice.ShouldBeNull();
        SessionView opened = harness.Navigation.WorkflowSelectionFor.ShouldNotBeNull();

        SessionAggregate reloaded = (await harness.Inner.Repository.LoadAsync(
            opened.Id, CancellationToken.None)).Value!;
        reloaded.Attempts.Single(a => a.Step == StepKind.Import).Status.ShouldBe(AttemptStatus.Succeeded);
        reloaded.Snapshot.ShouldNotBeNull();
        reloaded.Revisions.ShouldHaveSingleItem().Operation.ShouldBe(OperationKind.Import);

        File.ReadAllBytes(source).ShouldBe(before, "the customer's own file is never modified");
    }

    /// <summary>
    /// §11 of the fix brief: whether a human operator can reach the R3 trigger at all.
    /// </summary>
    /// <remarks>
    /// Two barriers stand between a mouse or keyboard and a second <c>ChooseFile</c> execution,
    /// and this asserts the one that lives in PrintFlow's own code. The generated
    /// <see cref="IAsyncRelayCommand"/> disallows concurrent executions, so <c>CanExecute</c> is
    /// false for as long as the first import runs and the bound button is disabled. The second
    /// barrier is Windows': <c>OpenFileDialog.ShowDialog()</c> is modal and disables its owner
    /// window, so the button cannot even be reached while the picker is open. R3's second
    /// execution came from UI Automation invoking the command directly, which is a harness
    /// capability rather than an operator one — so no command-concurrency infrastructure is added
    /// here to chase it. The cancellation it caused is fixed on its own merits above.
    /// </remarks>
    [Fact]
    public async Task A_second_ChooseFile_is_refused_while_the_first_import_is_still_running()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = CreateLargeSource(harness.Inner.Workspace, "double-invoke.png");

        harness.Home.ChooseFileCommand.CanExecute(null).ShouldBeTrue();

        Task running = harness.Home.ChooseFileCommand.ExecuteAsync(null);

        // The concurrency assertion itself lives in the rendezvous, which then cancels — a
        // 64 MiB copy is not worth completing once the question it was staged for is answered.
        CancelOnceBytesHaveLanded(harness.Home.ChooseFileCommand, SessionsAreaOf(harness.Inner), running)
            .ShouldBeTrue("the import must have been in flight while concurrency was checked");

        await running;

        harness.Home.ChooseFileCommand.CanExecute(null).ShouldBeTrue(
            "and it is offered again once that import has finished");
    }

    private static async Task<SessionId> OnlySessionIdAsync(SessionServiceHarness harness)
    {
        IReadOnlyList<SessionListItem> listed = (await harness.Repository.ListRecentAsync(
            10, DateTimeOffset.MinValue, CancellationToken.None)).Value;
        return listed.ShouldHaveSingleItem().Id;
    }

    private static string SessionsAreaOf(SessionServiceHarness harness) =>
        Path.Combine(harness.Workspace.Root, "Sessions");

    private static string CreateLargeSource(TempWorkspace workspace, string fileName)
    {
        string path = Path.Combine(workspace.Root, fileName);
        using FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.SetLength(LargeSourceBytes);
        return path;
    }

    /// <summary>
    /// Cancels the token only once the import has genuinely put bytes into a session's
    /// <c>Source\</c> area, and reports whether it did. See the workspace-level tests for why
    /// this is a rendezvous rather than a sleep.
    /// </summary>
    private static bool CancelOnceBytesHaveLanded(
        CancellationTokenSource cancellation, string sessionsArea, Task pending) =>
        WaitForBytes(sessionsArea, pending, cancellation.Cancel);

    private static bool CancelOnceBytesHaveLanded(
        IAsyncRelayCommand command, string sessionsArea, Task pending) =>
        WaitForBytes(sessionsArea, pending, () =>
        {
            // Asserted at the moment it matters: while this import is running, the command that
            // started it refuses to start another.
            command.CanExecute(null).ShouldBeFalse(
                "a second ChooseFile must be refused while the first import is still running");
            command.Cancel();
        });

    private static bool WaitForBytes(string sessionsArea, Task pending, Action onObserved)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        bool landed = false;

        while (elapsed.Elapsed < RendezvousTimeout)
        {
            if (BytesLandedIn(sessionsArea) > 0)
            {
                landed = true;
                break;
            }

            if (pending.IsCompleted)
            {
                break;
            }
        }

        onObserved();
        return landed;
    }

    private static long BytesLandedIn(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        long total = 0;
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (IOException)
            {
                // Being written; readable on the next pass.
            }
        }

        return total;
    }
}
