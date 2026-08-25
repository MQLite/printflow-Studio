using System.IO;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// What the workflow now asks a Meitu adapter to produce, and where
/// (Epic 11300 Part B2B §4, §7, §19, §29).
/// </summary>
/// <remarks>
/// The workflow used to name the working copy as its own expected output, because the fake
/// adapter processes in place and nothing downstream minded. A real Meitu does mind: exporting
/// over the input would destroy the bytes the attempt validates its result against.
///
/// These tests pin the replacement — a new file, beside the working copy, named by the same
/// preset-driven authority that names the approved deliverable — through the real
/// <see cref="SessionService"/>, the real workspace and a real database, so what is asserted is
/// where the file actually is rather than what the request said.
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class MeituOutputNamingTests
{
    /// <summary>
    /// The Enhancement Revision points at <c>{Name}_HD.png</c> inside the attempt's own directory.
    /// </summary>
    [Fact]
    public async Task An_Enhancement_Revision_points_at_the_preset_named_output_beside_its_working_copy()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteSourcePng(), "PF_JOB_123", "tester",
            CancellationToken.None)).Value.Id;
        await service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None);

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Failure.ToString() : string.Empty);

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision enhancement = aggregate.Revisions.Single(r => r.Operation == OperationKind.Enhance);
        ProcessingAttempt attempt = aggregate.Attempts.Single(
            a => a.Step == StepKind.Enhancement && a.Status == AttemptStatus.Succeeded);

        // The preset's enhanced pattern, applied to the operator's output name.
        enhancement.File.FileName.ShouldBe("PF_JOB_123_HD.png");

        // Inside this attempt's own working directory, which is what makes it unique per attempt
        // without the name having to carry a sequence.
        enhancement.File.Area.ShouldBe(WorkspaceArea.Working);
        enhancement.File.RelativePath.ShouldContain(attempt.Id.Value.ToString("D"));

        File.Exists(harness.FileWorkspace.ResolveAbsolute(enhancement.File)).ShouldBeTrue();
    }

    /// <summary>
    /// The working copy survives the attempt that produced a result from it.
    /// </summary>
    /// <remarks>
    /// §19 at workflow level. The result is a genuinely separate file, so the input the attempt
    /// was validated against is still on disk and still readable — which is what makes the
    /// adapter's byte-for-byte source check possible at all.
    /// </remarks>
    [Fact]
    public async Task The_working_copy_still_exists_beside_the_result_it_produced()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteSourcePng(), "PF_JOB_456", "tester",
            CancellationToken.None)).Value.Id;
        await service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None);
        await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision enhancement = aggregate.Revisions.Single(r => r.Operation == OperationKind.Enhance);

        string outputAbsolute = harness.FileWorkspace.ResolveAbsolute(enhancement.File);
        string attemptDirectory = Path.GetDirectoryName(outputAbsolute)!;
        string[] files = [.. Directory.EnumerateFiles(attemptDirectory).Select(Path.GetFileName)!];

        files.ShouldContain("PF_JOB_456_HD.png");
        files.Length.ShouldBe(2);
        files.ShouldContain(name => name != "PF_JOB_456_HD.png");
    }

    /// <summary>
    /// A retry produces a second output file without touching the first
    /// (Epic 11300 Part B2B §29, §30).
    /// </summary>
    /// <remarks>
    /// Both results carry the same name — the preset's — and neither overwrites the other,
    /// because uniqueness comes from the attempt directory. That is the property that lets a
    /// rejected result and the retry that replaces it both survive for comparison, and it is
    /// worth asserting on the file system rather than inferring from the Revision rows.
    /// </remarks>
    [Fact]
    public async Task A_retry_writes_a_second_output_without_overwriting_the_first()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteSourcePng(), "PF_JOB_789", "tester",
            CancellationToken.None)).Value.Id;
        await service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None);

        OperationResult<SessionView> first = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
        SessionStep stepA = first.Value.Steps.Single(s => s.Step == StepKind.Enhancement);

        await service.ExecuteAsync(
            id,
            new WorkflowCommand.Reject(
                StepKind.Enhancement, stepA.CurrentRevisionSha256!.Value,
                RejectionReason.InsufficientResult, "not sharp enough"),
            "tester", CancellationToken.None);
        await service.ExecuteAsync(
            id, new WorkflowCommand.Retry(StepKind.Enhancement), "tester", CancellationToken.None);
        await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision[] revisions = [.. aggregate.Revisions.Where(r => r.Operation == OperationKind.Enhance)];

        revisions.Length.ShouldBe(2);
        revisions.ShouldAllBe(r => r.File.FileName == "PF_JOB_789_HD.png");

        // Same name, different files, both still on disk.
        string a = harness.FileWorkspace.ResolveAbsolute(revisions[0].File);
        string b = harness.FileWorkspace.ResolveAbsolute(revisions[1].File);
        a.ShouldNotBe(b);
        File.Exists(a).ShouldBeTrue();
        File.Exists(b).ShouldBeTrue();
    }

    /// <summary>Background removal names its own artefact, not the Enhancement one.</summary>
    [Fact]
    public async Task Background_removal_asks_for_the_cutout_name()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteSourcePng(), "PF_JOB_321", "tester",
            CancellationToken.None)).Value.Id;
        await service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None);

        OperationResult<SessionView> enhanced = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
        await service.ExecuteAsync(
            id,
            new WorkflowCommand.Approve(
                StepKind.Enhancement,
                enhanced.Value.Steps.Single(s => s.Step == StepKind.Enhancement).CurrentRevisionSha256!.Value),
            "tester", CancellationToken.None);

        OperationResult<SessionView> cutout = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval), "tester", CancellationToken.None);
        cutout.IsSuccess.ShouldBeTrue(cutout.IsFailure ? cutout.Failure.ToString() : string.Empty);

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        aggregate.Revisions
            .Single(r => r.Operation == OperationKind.RemoveBackground)
            .File.FileName.ShouldBe("PF_JOB_321_CUTOUT.png");
    }
}
