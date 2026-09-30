using System.IO;
using PrintFlow.WorkstationEntry;

namespace PrintFlow.Tests.Unit.WorkstationEntry;

public sealed class OwnedRestartAuthorityTests
{
    private static readonly string Root = Path.Combine(EntryTestPaths.Repository, "artifacts", "pf-opux-scrum11154-workstation-entry", "runs", "child-authority-static");
    private static readonly OwnedChildExpectation Expected = new(1234, 987654, new string('a', 32), new string('b', 64), new string('c', 64), Root, Path.Combine(EntryTestPaths.Repository, "host.dll"), "test-ntfs-identity");
    private static OwnedChildBoundary Valid => new(Expected.Pid, Expected.StartUtcTicks, new string('d', 32), new string('e', 32),
        Expected.ControlToken, Expected.CandidateHash, Expected.ScenarioHash, Root, Expected.HostAssembly, Path.Combine(Root, "state", "app.db"), Path.Combine(Root, "workspace"), Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), RootIdentity: Expected.RootIdentity);
    [Fact] public void Exact_owned_child_boundary_is_accepted_without_process_control() => OwnedRestart.ValidateBoundary(Valid, Expected);
    [Fact] public void Reused_pid_changed_start_or_token_is_refused()
    {
        Should.Throw<IOException>(() => OwnedRestart.ValidateBoundary(Valid with { StartUtcTicks = 1 }, Expected));
        Should.Throw<IOException>(() => OwnedRestart.ValidateBoundary(Valid with { ControlToken = new string('f', 32) }, Expected));
        Should.Throw<IOException>(() => OwnedRestart.ValidateBoundary(Valid with { Pid = 999 }, Expected));
    }
    [Fact] public void Outside_output_and_changed_candidate_are_refused()
    {
        Should.Throw<IOException>(() => OwnedRestart.ValidateBoundary(Valid with { Database = @"D:\PrintFlowStudio\app.db" }, Expected));
        Should.Throw<IOException>(() => OwnedRestart.ValidateBoundary(Valid with { Root = Path.GetDirectoryName(Root)! }, Expected));
        Should.Throw<IOException>(() => OwnedRestart.ValidateBoundary(Valid with { CandidateHash = new string('f', 64) }, Expected));
        Should.Throw<IOException>(() => OwnedRestart.ValidateBoundary(Valid with { RootIdentity = "replacement" }, Expected));
    }
    [Fact] public void Missing_session_and_ownership_are_refused()
    {
        Should.Throw<IOException>(() => OwnedRestart.ValidateBoundary(Valid with { SessionId = "unknown" }, Expected));
        Should.Throw<IOException>(() => OwnedRestart.ValidateBoundary(Valid with { RunToken = "" }, Expected));
    }
}
