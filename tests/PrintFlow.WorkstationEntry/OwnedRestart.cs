using System.Diagnostics;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.WorkstationEntry;

public sealed record OwnedChildBoundary(int Pid, long StartUtcTicks, string RunToken, string OwnerToken,
    string ControlToken, string CandidateHash, string ScenarioHash, string Root, string HostAssembly,
    string Database, string Workspace, string SessionId, string AttemptId, int RevisionCount = 0, int OutputCount = 0, string RootIdentity = "");
public sealed record OwnedChildExpectation(int Pid, long StartUtcTicks, string ControlToken,
    string CandidateHash, string ScenarioHash, string Root, string HostAssembly, string RootIdentity = "");

public static partial class OwnedRestart
{
    public static void ValidateBoundary(OwnedChildBoundary boundary, OwnedChildExpectation expected)
    {
        bool Token(string? token) => token is { Length: 32 } && token.All(Uri.IsHexDigit);
        if (boundary.Pid != expected.Pid || boundary.Pid <= 0 || boundary.StartUtcTicks != expected.StartUtcTicks ||
            boundary.ControlToken != expected.ControlToken || !Token(boundary.ControlToken) ||
            !Token(boundary.RunToken) || !Token(boundary.OwnerToken) ||
            boundary.CandidateHash != expected.CandidateHash || boundary.ScenarioHash != expected.ScenarioHash ||
            boundary.Root != expected.Root || boundary.HostAssembly != expected.HostAssembly || string.IsNullOrEmpty(boundary.RootIdentity) || boundary.RootIdentity != expected.RootIdentity ||
            boundary.Database != Path.Combine(expected.Root, "state", "app.db") || boundary.Workspace != Path.Combine(expected.Root, "workspace") ||
            !Guid.TryParse(boundary.SessionId, out _) || !Guid.TryParse(boundary.AttemptId, out _))
            throw new IOException("Owned child process, token, candidate or output authority mismatch; termination refused.");
    }

    public static async Task<int> SuperviseAsync(ValidatedInput input, EntryOptions options, TextWriter output)
    {
        if (System.Reflection.Assembly.GetEntryAssembly() != typeof(Program).Assembly)
            throw new InvalidOperationException("Owned restart supervisor is valid only in the dedicated entry executable.");
        if (options.Resume || options.Mode != "PrepareAndSmoke" || options.RestartChildToken is not null)
            throw new ArgumentException("Owned restart requires a fresh noninteractive run.");
        string token = Guid.NewGuid().ToString("N");
        using Process child = Start(options, token, resume: false);
        Task<string> stdout = child.StandardOutput.ReadToEndAsync(), stderr = child.StandardError.ReadToEndAsync();
        Process? recovery = null;
        Task<string>? recoveryOut = null, recoveryError = null;
        try
        {
        OwnedChildExpectation expected = new(child.Id, child.StartTime.ToUniversalTime().Ticks, token, input.CandidateHash, input.ScenarioHash, input.Plan.Root, typeof(Program).Assembly.Location);
        string boundaryPath = Path.Combine(input.Plan.Root, "evidence", "owned-child-boundary.json");
        OwnedChildBoundary? boundary = null;
        string? firstBoundaryIdentity = null;
        DateTime deadline = DateTime.UtcNow.AddSeconds(60);
        while (!child.HasExited && DateTime.UtcNow < deadline)
        {
            if (File.Exists(boundaryPath))
            {
                // ReadFile verifies ancestors/reparse/link-count before any JSON is consumed.
                try
                {
                    using NativePathLease held = NativePathLease.ReadFile(boundaryPath);
                    boundary = JsonSerializer.Deserialize<OwnedChildBoundary>(File.ReadAllText(boundaryPath), ManifestReader.Json);
                    firstBoundaryIdentity = held.Identity;
                }
                catch (JsonException) { /* Child publication can still be flushing; never authorize a partial record. */ }
                catch (IOException ex) when (ex.InnerException is System.ComponentModel.Win32Exception { NativeErrorCode: 32 }) { /* Still being flushed by this child. */ }
                if (boundary is not null) break;
            }
            await Task.Delay(100);
        }
        if (boundary is null)
        {
            // No authority record: never kill. The child has its own bounded cancellation.
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(6));
            output.WriteLine(await stdout); output.WriteLine(await stderr);
            throw new IOException("Owned child did not publish a controlled boundary; no process was terminated.");
        }
        using NativePathLease root = NativePathLease.Ancestors(input.Plan.Root);
        expected = expected with { RootIdentity = root.Identity };
        ValidateBoundary(boundary, expected);
        using NativePathLease boundaryIdentity = NativePathLease.ReadFile(boundaryPath);
        OwnedChildBoundary stable = JsonSerializer.Deserialize<OwnedChildBoundary>(File.ReadAllText(boundaryPath), ManifestReader.Json)!;
        if (boundaryIdentity.Identity != firstBoundaryIdentity || stable != boundary || child.HasExited || child.Id != expected.Pid || child.StartTime.ToUniversalTime().Ticks != expected.StartUtcTicks)
            throw new IOException("Owned child boundary/process changed; termination refused.");
        // This Process object is the exact child started above. Never enumerate or kill by name,
        // never terminate a process tree, and never target a real application.
        child.Kill(entireProcessTree: false);
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        output.WriteLine($"OWNED_CHILD_INTERRUPTED pid={expected.Pid} start={expected.StartUtcTicks}; not power-loss evidence.");
        output.WriteLine(await stdout); output.WriteLine(await stderr);
        recovery = Start(options, token, resume: true);
        recoveryOut = recovery.StandardOutput.ReadToEndAsync(); recoveryError = recovery.StandardError.ReadToEndAsync();
        await recovery.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(6));
        output.WriteLine(await recoveryOut); output.WriteLine(await recoveryError);
        if (recovery.ExitCode != 0) throw new IOException("Owned recovery child refused; retained run is incomplete.");
        string resultPath = Path.Combine(input.Plan.Root, "evidence", "owned-child-recovery.json");
        using NativePathLease resultIdentity = NativePathLease.ReadFile(resultPath);
        using JsonDocument result = JsonDocument.Parse(File.ReadAllText(resultPath));
        if (!result.RootElement.GetProperty("Verified").GetBoolean() || result.RootElement.GetProperty("RunToken").GetString() != boundary.RunToken ||
            result.RootElement.GetProperty("ControlToken").GetString() != token)
            throw new IOException("Recovery evidence does not belong to this controlled interruption.");
        output.WriteLine("OWNED_RESTART_VERIFIED: interrupted attempt/recovery navigation only; visible readiness remains separate.");
        return 0;
        }
        finally
        {
            // A malformed/unknown record never grants termination. Wait for this known
            // child to cancel itself before dropping its owned Process/pipe handles.
            try
            {
                if (!child.HasExited) await child.WaitForExitAsync();
                await Task.WhenAll(stdout, stderr);
                if (recovery is not null)
                {
                    if (!recovery.HasExited) await recovery.WaitForExitAsync();
                    await Task.WhenAll(recoveryOut!, recoveryError!);
                }
            }
            finally { recovery?.Dispose(); }
        }
    }

    private static Process Start(EntryOptions options, string token, bool resume)
    {
        string executable = Environment.ProcessPath ?? throw new IOException("Dedicated executable path unavailable.");
        if (!string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Owned restart must be launched through the explicit dotnet entry launcher.");
        ProcessStartInfo start = new(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { typeof(Program).Assembly.Location, "--Mode", "PrepareAndSmoke", "--Root", options.Root,
            "--ScenarioManifest", options.ScenarioManifest, "--CandidateManifest", options.CandidateManifest, "--RestartChildToken", token }) start.ArgumentList.Add(arg);
        if (resume) start.ArgumentList.Add("--Resume");
        return Process.Start(start) ?? throw new IOException("Owned noninteractive child did not start.");
    }

    public static async Task RunChildAsync(ServiceProvider services, OwnedRun run, ValidatedInput input, string token, OwnedWork ownedWork, CancellationToken ct)
    {
        using CancellationTokenSource bounded = CancellationTokenSource.CreateLinkedTokenSource(ct); bounded.CancelAfter(TimeSpan.FromSeconds(90));
        OwnedPaths paths = run.Paths;
        string source = paths.Require(paths.At("fixtures", "inputs", "owned-restart.png"), "fixtures");
        byte[] pixels = Enumerable.Repeat((byte)255, 20 * 16 * 4).ToArray();
        BitmapSource bitmap = BitmapSource.Create(20, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 20 * 4);
        PngBitmapEncoder encoder = new(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream stream = new(source, FileMode.CreateNew, FileAccess.Write, FileShare.None)) encoder.Save(stream);
        paths.Admit(source);
        ISessionService sessions = services.GetRequiredService<ISessionService>();
        SessionView session = Must(await sessions.ImportAsync(WorkflowType.PrepareAsset, source, "owned-restart", "synthetic-restart", bounded.Token));
        session = Must(await sessions.ExecuteAsync(session.Id, new WorkflowCommand.ConfirmOriginal(), "synthetic-restart", bounded.Token));
        FakeMeituProcessor fake = services.GetRequiredService<FakeMeituProcessor>();
        fake.SetScenario(FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));
        Task<OperationResult<SessionView>> running = sessions.ExecuteAsync(session.Id, new WorkflowCommand.StartStep(StepKind.Enhancement), "synthetic-restart", bounded.Token);
        ownedWork.Track(running);
        await fake.HangStarted.WaitAsync(TimeSpan.FromSeconds(15), bounded.Token);
        SessionAggregate aggregate = Must(await services.GetRequiredService<ISessionRepository>().LoadAsync(session.Id, bounded.Token)) ?? throw new IOException("Owned child aggregate missing.");
        var attempt = aggregate.Attempts.Single(item => item.Status == AttemptStatus.Running);
        using Process self = Process.GetCurrentProcess();
        OwnedChildBoundary boundary = new(Environment.ProcessId, self.StartTime.ToUniversalTime().Ticks, run.Ownership.RunToken, run.Ownership.OwnerToken,
            token, input.CandidateHash, input.ScenarioHash, paths.Root, typeof(Program).Assembly.Location, paths.At("state", "app.db"), paths.At("workspace"),
            session.Id.ToString(), attempt.Id.ToString(), aggregate.Revisions.Count, aggregate.Outputs.Count, run.Ownership.RootIdentity);
        Write(paths, "owned-child-boundary.json", boundary);
        await running.WaitAsync(bounded.Token); // Self-cancellation enters the host's owned-work teardown.
        throw new IOException("Owned child ended before controlled interruption; restart proof incomplete.");
    }

    public static async Task VerifyRecoveryAsync(ServiceProvider services, OwnedRun run, ValidatedInput input, string token, StartupRecoveryReport report, OwnedWork ownedWork, CancellationToken ct)
    {
        string path = run.Paths.Require(run.Paths.At("evidence", "owned-child-boundary.json"), "evidence");
        using NativePathLease held = NativePathLease.ReadFile(path);
        OwnedChildBoundary boundary = JsonSerializer.Deserialize<OwnedChildBoundary>(File.ReadAllText(path), ManifestReader.Json)!;
        ValidateBoundary(boundary, new(boundary.Pid, boundary.StartUtcTicks, token, input.CandidateHash, input.ScenarioHash, run.Paths.Root, typeof(Program).Assembly.Location, run.Ownership.RootIdentity));
        if (run.Ownership.RunToken != boundary.RunToken || run.Ownership.OwnerToken == boundary.OwnerToken) throw new IOException("Recovery did not renew this run's owner.");
        SessionId sessionId = new(Guid.Parse(boundary.SessionId)); AttemptId attemptId = new(Guid.Parse(boundary.AttemptId));
        if (!report.Entries.Any(entry => entry.Action == StartupRecoveryAction.AttemptInterrupted && entry.SessionId == sessionId && entry.AttemptId == attemptId) || !report.ReleasedAutomationLock)
            throw new IOException("Real recovery did not interrupt the exact dead child's attempt and release its stale lock.");
        SessionAggregate aggregate = Must(await services.GetRequiredService<ISessionRepository>().LoadAsync(sessionId, ct)) ?? throw new IOException("Recovered session missing.");
        if (aggregate.Attempts.Single(attempt => attempt.Id == attemptId).Status != AttemptStatus.Interrupted || aggregate.Revisions.Count != boundary.RevisionCount || aggregate.Outputs.Count != boundary.OutputCount)
            throw new IOException("Recovery manufactured a result or failed to preserve the interrupted attempt.");
        INavigationService navigation = services.GetRequiredService<INavigationService>();
        async Task Observe(Task task) { ownedWork.Track(task); await task.WaitAsync(ct); }
        await Observe(navigation.GoHomeAsync(ct));
        HomeViewModel home = (HomeViewModel)navigation.Current!; await Observe(home.ThumbnailsLoaded);
        RecoverySessionRow row = home.RecoverySessions.Single(item => item.Id == sessionId);
        await Observe(home.OpenRecoveryCommand.ExecuteAsync(row));
        SessionViewModel session = (SessionViewModel)navigation.Current!;
        await Observe(Task.WhenAll(session.PreviewsLoaded, session.PreflightLoaded, session.FinalSaveFactsLoaded));
        if (session.Id != sessionId) throw new IOException("Recovery command opened another session.");
        await Observe(session.OpenErrorDetailsCommand.ExecuteAsync(null));
        if (navigation.Current is not ErrorDetailsViewModel details) throw new IOException("Interrupted session did not reach existing Error Details.");
        await Observe(details.BackCommand.ExecuteAsync(null));
        if (navigation.Current is not SessionViewModel recoveredSession) throw new IOException("Details did not return to recovered Session.");
        await Observe(recoveredSession.BackToHomeCommand.ExecuteAsync(null));
        if (navigation.Current is not HomeViewModel returnedHome) throw new IOException("Recovered Session did not return through its existing Home command.");
        await Observe(returnedHome.ThumbnailsLoaded);
        Write(run.Paths, "owned-child-recovery.json", new { Verified = true, Evidence = "SYNTHETIC_PROCESS_INTERRUPTION_NOT_POWER_LOSS", RunToken = run.Ownership.RunToken,
            ControlToken = token, boundary.SessionId, boundary.AttemptId, NewOwnerToken = run.Ownership.OwnerToken, Recovered = report, NewResults = false,
            Screens = new[] { "Home", "Recovery.Open", "Session", "ErrorDetails", "Session", "Home" } });
    }
    private static T Must<T>(OperationResult<T> result) => result.IsSuccess ? result.Value : throw new IOException(result.Failure.Code + ": " + result.Failure.TechnicalDetail);
    private static void Write(OwnedPaths paths, string leaf, object value)
    {
        using FileStream stream = new(paths.Require(paths.At("evidence", leaf), "evidence"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, value, ManifestReader.Json); stream.Flush(true);
    }
}
