using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Preset;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// The two ordinary Fake-mode producing steps, run against the accepted naming contract
/// (naming-contract fix §10).
/// </summary>
/// <remarks>
/// The R2 Final Gate crashed on the Background Removal half of this: the preset supplied
/// <c>{Name}_CUTOUT.png</c>, the renderer handed it to <c>string.Format</c>, and the resulting
/// <see cref="FormatException"/> left the process before a CUTOUT review could exist. The suite
/// did not catch it because its fixture preset spoke a positional syntax no accepted manifest
/// has ever used.
/// <para>
/// With the fixture now carrying the accepted patterns, these tests exercise the real
/// <see cref="SessionService"/>, the real workspace and a real database, and assert the four
/// things the gate asks for at each step: a name, exactly one Revision, the attempt's own
/// directory, and an untouched upstream.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class NamingContractWorkflowRegressionTests
{
    /// <summary>
    /// Enhancement: upstream in, <c>&lt;Name&gt;_HD.png</c> out, ReviewRequired, upstream intact.
    /// </summary>
    [Fact]
    public async Task Fake_enhancement_produces_one_HD_revision_and_leaves_its_upstream_alone()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        string sourcePath = harness.WriteSourcePng("naming-hd.png");
        byte[] sourceBefore = await File.ReadAllBytesAsync(sourcePath);

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, sourcePath, "PF_NAME_HD", "tester", CancellationToken.None)).Value.Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

        SessionAggregate before = await Reload(harness, id);
        Revision upstream = before.Revisions.Single(r => r.Operation == OperationKind.Import);
        Sha256 upstreamHash = upstream.Sha256;

        // No FormatException: the command completes and reports success rather than escaping.
        SessionView view = await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None));

        view.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.ReviewRequired);

        SessionAggregate after = await Reload(harness, id);
        Revision[] produced = [.. after.Revisions.Where(r => r.Operation == OperationKind.Enhance)];
        produced.Length.ShouldBe(1);
        produced[0].File.FileName.ShouldBe("PF_NAME_HD_HD.png");

        ProcessingAttempt attempt = after.Attempts.Single(
            a => a.Step == StepKind.Enhancement && a.Status == AttemptStatus.Succeeded);
        produced[0].File.Area.ShouldBe(WorkspaceArea.Working);
        produced[0].File.RelativePath.ShouldContain(attempt.Id.Value.ToString("D"));
        File.Exists(harness.FileWorkspace.ResolveAbsolute(produced[0].File)).ShouldBeTrue();

        // The upstream Revision and the operator's own source file are both untouched.
        after.Revisions.Single(r => r.Id == upstream.Id).Sha256.ShouldBe(upstreamHash);
        HashOf(harness.FileWorkspace.ResolveAbsolute(upstream.File)).ShouldBe(upstreamHash.ToString());
        (await File.ReadAllBytesAsync(sourcePath)).ShouldBe(sourceBefore);
    }

    /// <summary>
    /// Background Removal: the reviewed upstream and an explicit authority in,
    /// <c>&lt;Name&gt;_CUTOUT.png</c> out, ReviewRequired, upstream intact.
    /// </summary>
    /// <remarks>
    /// This is the R2 crash point, reached through the service rather than the shell.
    /// </remarks>
    [Fact]
    public async Task Fake_background_removal_produces_one_CUTOUT_revision_and_leaves_its_upstream_alone()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        string sourcePath = harness.WriteSourcePng("naming-cutout.png");
        byte[] sourceBefore = await File.ReadAllBytesAsync(sourcePath);

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, sourcePath, "PF_NAME_BR", "tester", CancellationToken.None)).Value.Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

        SessionView enhanced = await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.Approve(
                StepKind.Enhancement,
                enhanced.Steps.Single(s => s.Step == StepKind.Enhancement).CurrentRevisionSha256!.Value),
            "tester", CancellationToken.None));

        SessionAggregate reviewed = await Reload(harness, id);
        Revision upstream = reviewed.Revisions.Single(r => r.Operation == OperationKind.Enhance);
        Sha256 upstreamHash = upstream.Sha256;

        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);

        SessionView cutout = await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval), "tester", CancellationToken.None));

        cutout.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).State
            .ShouldBe(StepState.ReviewRequired);

        SessionAggregate after = await Reload(harness, id);
        Revision[] produced = [.. after.Revisions.Where(r => r.Operation == OperationKind.RemoveBackground)];
        produced.Length.ShouldBe(1);
        produced[0].File.FileName.ShouldBe("PF_NAME_BR_CUTOUT.png");

        ProcessingAttempt attempt = after.Attempts.Single(
            a => a.Step == StepKind.BackgroundRemoval && a.Status == AttemptStatus.Succeeded);
        produced[0].File.Area.ShouldBe(WorkspaceArea.Working);
        produced[0].File.RelativePath.ShouldContain(attempt.Id.Value.ToString("D"));
        File.Exists(harness.FileWorkspace.ResolveAbsolute(produced[0].File)).ShouldBeTrue();

        // The reviewed content the authority was granted over is byte-for-byte what it was, and
        // the cutout is a different file entirely.
        after.Revisions.Single(r => r.Id == upstream.Id).Sha256.ShouldBe(upstreamHash);
        HashOf(harness.FileWorkspace.ResolveAbsolute(upstream.File)).ShouldBe(upstreamHash.ToString());
        produced[0].File.RelativePath.ShouldNotBe(upstream.File.RelativePath);
        (await File.ReadAllBytesAsync(sourcePath)).ShouldBe(sourceBefore);
    }

    /// <summary>
    /// The accepted manifest itself, driven through the whole Fake workflow to CUTOUT review
    /// (naming-contract fix §9, §15).
    /// </summary>
    /// <remarks>
    /// The strongest form of the regression available without a window. Where this workstation
    /// holds the manifest <c>appsettings.json</c> points at, the session service is built on a
    /// real <see cref="WorkstationPresetProvider"/> over those exact bytes, hash-verified against
    /// the configured digest — so the naming values in play are the accepted ones, read the way
    /// production reads them, not values a test chose. Elsewhere the synthetic fixture stands in;
    /// it carries the same accepted patterns, and
    /// <c>AcceptedNamingContractTests.The_transcribed_contract_matches_the_configured_manifest</c>
    /// is what keeps that true.
    /// </remarks>
    [Fact]
    public async Task The_accepted_preset_drives_the_fake_workflow_to_a_cutout_review()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService(AcceptedNamingContract.ConfiguredProvider());

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteSourcePng("accepted-preset.png"), "PF_ACCEPTED",
            "tester", CancellationToken.None)).Value.Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

        SessionView enhanced = await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.Approve(
                StepKind.Enhancement,
                enhanced.Steps.Single(s => s.Step == StepKind.Enhancement).CurrentRevisionSha256!.Value),
            "tester", CancellationToken.None));

        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);

        SessionView cutout = await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval), "tester", CancellationToken.None));

        cutout.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).State
            .ShouldBe(StepState.ReviewRequired);

        SessionAggregate persisted = await Reload(harness, id);
        persisted.Revisions.Single(r => r.Operation == OperationKind.Enhance)
            .File.FileName.ShouldBe("PF_ACCEPTED_HD.png");
        persisted.Revisions.Single(r => r.Operation == OperationKind.RemoveBackground)
            .File.FileName.ShouldBe("PF_ACCEPTED_CUTOUT.png");
    }

    private static async Task<SessionAggregate> Reload(SessionServiceHarness harness, SessionId id) =>
        (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;

    private static async Task<SessionView> Must(Task<OperationResult<SessionView>> pending)
    {
        OperationResult<SessionView> result = await pending;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        return result.Value;
    }

    private static string HashOf(string absolutePath) =>
        Sha256.FromBytes(SHA256.HashData(File.ReadAllBytes(absolutePath))).ToString();
}
