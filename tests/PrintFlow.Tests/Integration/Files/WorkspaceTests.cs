using System.IO;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Files;

/// <summary>
/// Jira 11106b: the real controlled workspace — session layout, source preservation, working
/// copies, collision-safe reservation, and protected-area enforcement (task §45).
/// </summary>
public sealed class WorkspaceTests
{
    [Fact]
    public void Session_directory_follows_the_S_utc_shortid_convention_with_no_customer_text()
    {
        using TempWorkspace workspace = new();
        FileWorkspace fileWorkspace = new(workspace.Root);
        SessionId id = SessionId.From(Guid.CreateVersion7());
        DateTimeOffset createdUtc = new(2026, 8, 19, 10, 30, 0, TimeSpan.Zero);

        OperationResult<WorkspaceDirRef> created = fileWorkspace.CreateSession(id, createdUtc);

        created.IsSuccess.ShouldBeTrue();
        string expectedShortId = id.Value.ToString("N")[..8];
        created.Value.RelativePath.ShouldBe($"Sessions/S_20260819T103000Z_{expectedShortId}");
        Directory.Exists(Path.Combine(workspace.Root, created.Value.RelativePath)).ShouldBeTrue();
        foreach (string area in new[] { "Source", "Working", "Approved", "Rejected", "Logs" })
        {
            Directory.Exists(Path.Combine(workspace.Root, created.Value.RelativePath, area)).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Import_never_modifies_the_operators_source_file()
    {
        using TempWorkspace workspace = new();
        FileWorkspace fileWorkspace = new(workspace.Root);
        SessionId id = SessionId.From(Guid.CreateVersion7());
        WorkspaceDirRef session = fileWorkspace.CreateSession(id, DateTimeOffset.UtcNow).Value;

        byte[] content = SyntheticImages.Png();
        string sourcePath = workspace.CreateSourceFile("original.png", content);
        byte[] beforeBytes = File.ReadAllBytes(sourcePath);
        DateTime beforeWrite = File.GetLastWriteTimeUtc(sourcePath);

        OperationResult<WorkspaceFileRef> imported =
            await fileWorkspace.ImportSourceAsync(session, sourcePath, CancellationToken.None);

        imported.IsSuccess.ShouldBeTrue();
        File.Exists(sourcePath).ShouldBeTrue();
        File.ReadAllBytes(sourcePath).ShouldBe(beforeBytes);
        File.GetLastWriteTimeUtc(sourcePath).ShouldBe(beforeWrite);
    }

    [Fact]
    public async Task Snapshot_is_byte_identical_to_the_source_and_read_only()
    {
        using TempWorkspace workspace = new();
        FileWorkspace fileWorkspace = new(workspace.Root);
        SessionId id = SessionId.From(Guid.CreateVersion7());
        WorkspaceDirRef session = fileWorkspace.CreateSession(id, DateTimeOffset.UtcNow).Value;

        byte[] content = SyntheticImages.Jpeg();
        string sourcePath = workspace.CreateSourceFile("original.jpg", content);

        OperationResult<WorkspaceFileRef> imported =
            await fileWorkspace.ImportSourceAsync(session, sourcePath, CancellationToken.None);

        string snapshotAbsolute = fileWorkspace.ResolveAbsolute(imported.Value);
        File.ReadAllBytes(snapshotAbsolute).ShouldBe(content);
        File.GetAttributes(snapshotAbsolute).HasFlag(FileAttributes.ReadOnly).ShouldBeTrue();
    }

    [Fact]
    public async Task Failed_import_leaves_the_source_untouched()
    {
        using TempWorkspace workspace = new();
        FileWorkspace fileWorkspace = new(workspace.Root);
        SessionId id = SessionId.From(Guid.CreateVersion7());
        WorkspaceDirRef session = fileWorkspace.CreateSession(id, DateTimeOffset.UtcNow).Value;

        string missingSource = Path.Combine(workspace.Root, "does-not-exist.png");

        OperationResult<WorkspaceFileRef> imported =
            await fileWorkspace.ImportSourceAsync(session, missingSource, CancellationToken.None);

        imported.IsFailure.ShouldBeTrue();
        File.Exists(missingSource).ShouldBeFalse();
    }

    [Fact]
    public async Task Every_attempt_gets_its_own_working_directory_and_retry_never_reuses_it()
    {
        using TempWorkspace workspace = new();
        FileWorkspace fileWorkspace = new(workspace.Root);
        SessionId id = SessionId.From(Guid.CreateVersion7());
        WorkspaceDirRef session = fileWorkspace.CreateSession(id, DateTimeOffset.UtcNow).Value;

        WorkspaceFileRef source = (await fileWorkspace.ImportSourceAsync(
            session, workspace.CreateSourceFile("in.png", SyntheticImages.Png()), CancellationToken.None)).Value;

        AttemptId firstAttempt = AttemptId.From(Guid.CreateVersion7());
        AttemptId secondAttempt = AttemptId.From(Guid.CreateVersion7());

        OperationResult<WorkspaceFileRef> first =
            await fileWorkspace.CreateWorkingCopyAsync(session, firstAttempt, source, CancellationToken.None);
        OperationResult<WorkspaceFileRef> second =
            await fileWorkspace.CreateWorkingCopyAsync(session, secondAttempt, source, CancellationToken.None);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        first.Value.RelativePath.ShouldNotBe(second.Value.RelativePath);
        first.Value.RelativePath.ShouldContain(firstAttempt.Value.ToString("D"));
        second.Value.RelativePath.ShouldContain(secondAttempt.Value.ToString("D"));
    }

    [Fact]
    public void Collision_reservation_yields_base_then_02_then_03_and_never_overwrites()
    {
        using TempWorkspace workspace = new();
        FileWorkspace fileWorkspace = new(workspace.Root);
        SessionId id = SessionId.From(Guid.CreateVersion7());
        WorkspaceDirRef session = fileWorkspace.CreateSession(id, DateTimeOffset.UtcNow).Value;
        NamingPatternSet patterns = NamingPatternSet.DesignDefault;

        OperationResult<WorkspaceFileRef> first =
            fileWorkspace.ReserveOutput(session, WorkspaceArea.Approved, "Name.png", patterns);
        OperationResult<WorkspaceFileRef> second =
            fileWorkspace.ReserveOutput(session, WorkspaceArea.Approved, "Name.png", patterns);
        OperationResult<WorkspaceFileRef> third =
            fileWorkspace.ReserveOutput(session, WorkspaceArea.Approved, "Name.png", patterns);

        first.Value.FileName.ShouldBe("Name.png");
        second.Value.FileName.ShouldBe("Name_02.png");
        third.Value.FileName.ShouldBe("Name_03.png");

        // Every reservation is a real, distinct file on disk — never an overwrite of the first.
        File.Exists(fileWorkspace.ResolveAbsolute(first.Value)).ShouldBeTrue();
        File.Exists(fileWorkspace.ResolveAbsolute(second.Value)).ShouldBeTrue();
        File.Exists(fileWorkspace.ResolveAbsolute(third.Value)).ShouldBeTrue();
    }

    [Fact]
    public void Chinese_output_names_survive_reservation()
    {
        using TempWorkspace workspace = new();
        FileWorkspace fileWorkspace = new(workspace.Root);
        SessionId id = SessionId.From(Guid.CreateVersion7());
        WorkspaceDirRef session = fileWorkspace.CreateSession(id, DateTimeOffset.UtcNow).Value;

        OperationResult<WorkspaceFileRef> reserved =
            fileWorkspace.ReserveOutput(session, WorkspaceArea.Approved, "客户设计_HD.png", NamingPatternSet.DesignDefault);

        reserved.IsSuccess.ShouldBeTrue();
        reserved.Value.FileName.ShouldBe("客户设计_HD.png");
        File.Exists(fileWorkspace.ResolveAbsolute(reserved.Value)).ShouldBeTrue();
    }

    [Fact]
    public void Traversal_in_a_relative_reference_is_rejected_at_construction()
    {
        Should.Throw<ArgumentException>(() => WorkspaceFileRef.Create("../../escape.png", WorkspaceArea.Approved));
    }

    [Fact]
    public void Write_into_Baseline_is_refused()
    {
        using TempWorkspace workspace = new();
        FileWorkspace fileWorkspace = new(workspace.Root);

        Should.Throw<InvalidOperationException>(() =>
            fileWorkspace.ResolveAbsolute(WorkspaceFileRef.Create("Baseline/evidence.json", WorkspaceArea.Working)));
    }

    [Fact]
    public void Write_into_TestData_is_refused()
    {
        using TempWorkspace workspace = new();
        FileWorkspace fileWorkspace = new(workspace.Root);

        Should.Throw<InvalidOperationException>(() =>
            fileWorkspace.ResolveAbsolute(WorkspaceFileRef.Create("TestData/fixture.png", WorkspaceArea.Working)));
    }

    [Fact]
    public async Task Cleanup_removes_only_Working_and_preserves_Source_and_Approved()
    {
        using TempWorkspace workspace = new();
        FileWorkspace fileWorkspace = new(workspace.Root);
        SessionId id = SessionId.From(Guid.CreateVersion7());
        WorkspaceDirRef session = fileWorkspace.CreateSession(id, DateTimeOffset.UtcNow).Value;

        WorkspaceFileRef source = (await fileWorkspace.ImportSourceAsync(
            session, workspace.CreateSourceFile("in.png", SyntheticImages.Png()), CancellationToken.None)).Value;
        WorkspaceFileRef working = (await fileWorkspace.CreateWorkingCopyAsync(
            session, AttemptId.From(Guid.CreateVersion7()), source, CancellationToken.None)).Value;
        WorkspaceFileRef approved = fileWorkspace.ReserveOutput(
            session, WorkspaceArea.Approved, "final.png", NamingPatternSet.DesignDefault).Value;

        OperationResult<PrintFlow.Domain.Results.Unit> cleaned = fileWorkspace.CleanupWorking(session);

        cleaned.IsSuccess.ShouldBeTrue();
        File.Exists(fileWorkspace.ResolveAbsolute(working)).ShouldBeFalse();
        File.Exists(fileWorkspace.ResolveAbsolute(source)).ShouldBeTrue();
        File.Exists(fileWorkspace.ResolveAbsolute(approved)).ShouldBeTrue();
    }

    [Fact]
    public void RecycleBin_has_no_hard_delete_fallback_on_failure()
    {
        RecycleBin recycleBin = new();

        OperationResult<PrintFlow.Domain.Results.Unit> result = recycleBin.SendToRecycleBin(
            Path.Combine(Path.GetTempPath(), "PrintFlowTests", Guid.NewGuid() + "-missing.png"));

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputMissing);
    }

    [Fact]
    public void RecycleBin_sends_a_temporary_test_file_to_the_recycle_bin()
    {
        using TempWorkspace workspace = new();
        string path = workspace.CreateSourceFile("throwaway.png", SyntheticImages.Png());
        RecycleBin recycleBin = new();

        OperationResult<PrintFlow.Domain.Results.Unit> result = recycleBin.SendToRecycleBin(path);

        result.IsSuccess.ShouldBeTrue();
        File.Exists(path).ShouldBeFalse();
    }
}
