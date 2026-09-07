using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Ui;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// The visual-only input contract that superseded SCRUM-11101: a customer PSD is design artwork,
/// so an existing spot-colour or white-ink channel in the source is observed, rasterised out with
/// every other non-component channel, and never becomes production authority.
/// </summary>
/// <remarks>
/// The original SCRUM-11101 acceptance criterion required PrintFlow to stop and ask the operator
/// whether to retain the existing white ink or regenerate it. The business clarification recorded
/// in <c>docs/printflow/scrum-11101-existing-white-ink-decision.md</c> supersedes that: PrintFlow
/// does not retain source W1 and does not ask. Production W1 is always generated from the approved
/// visual artwork immediately before the TIFF is written for Maintop.
/// <para>
/// What SCRUM-11099 accepted, and what these tests replace, was a hard refusal on
/// <c>HasSpots || HasW1</c>. That refusal is gone; every other PSD refusal is untouched, and
/// <see cref="PsdPreparationWorkflowTests"/> still covers them.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class VisualOnlyPsdInputContractTests
{
    /// <summary>
    /// The source PSD the original Jira row was about: RGB/8 with exactly one existing <c>W1</c>
    /// spot channel carrying non-empty content.
    /// </summary>
    private static byte[] ExistingW1Psd() => PsdInputPreparationTests.RgbCompositePsd(spot: true);

    /// <summary>
    /// A supported PSD is not refused because it carries white ink. It prepares into a managed
    /// visual raster and the operator is offered review, exactly as for a spot-free PSD.
    /// </summary>
    [Fact]
    public async Task Psd_with_an_existing_W1_prepares_into_a_reviewable_managed_raster()
    {
        using SessionServiceHarness h = new();
        VisualPsdProcessor adapter = new(h.FileWorkspace) { Spot = new PsdChannel("W1", "SPOTCOLOR") };
        ISessionService service = h.CreateServiceWithPhotoshop(adapter);

        byte[] bytes = ExistingW1Psd();
        string source = h.Workspace.CreateSourceFile("existing-w1.psd", bytes);
        var imported = await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, source, null, "qa", CancellationToken.None);
        imported.IsSuccess.ShouldBeTrue();

        var prepared = await service.ExecuteAsync(
            imported.Value.Id,
            new WorkflowCommand.StartStep(StepKind.OriginalConfirmation),
            "qa",
            CancellationToken.None);

        // Photoshop was actually consulted, so this rests on observed facts rather than on the
        // file extension — and it did not refuse.
        adapter.Calls.ShouldBe(1);
        prepared.IsSuccess.ShouldBeTrue(prepared.IsFailure ? prepared.Failure.ToString() : "");

        var state = (await h.Repository.LoadAsync(imported.Value.Id, CancellationToken.None)).Value!;

        // A managed PNG Revision exists, descended from the untouched import root.
        Revision root = state.Revisions.Single(r => r.IsRoot);
        Revision raster = state.Revisions.Single(r => r.Operation == OperationKind.PreparePsd);
        raster.SourceRevisionId.ShouldBe(root.Id);
        raster.Facts.Format.ShouldBe(ImageFormat.Png);
        raster.Facts.PixelWidth.ShouldBe(4);
        raster.Facts.PixelHeight.ShouldBe(3);

        // The operator reviews it.
        state.ToSnapshot().CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        (await h.Previews.GetPreviewAsync(imported.Value.Id, raster.Id, CancellationToken.None))
            .IsSuccess.ShouldBeTrue();

        ProcessingAttempt attempt = state.Attempts.Single(a => a.Operation == OperationKind.PreparePsd);
        attempt.Status.ShouldBe(AttemptStatus.Succeeded);
        attempt.OutputRevisionId.ShouldBe(raster.Id);

        // The observation survives as a diagnostic, and only as a diagnostic.
        PsdInspection facts = attempt.PsdInspection.ShouldNotBeNull();
        facts.HasW1.ShouldBeTrue("the fixture must really carry a W1, or this proves nothing");
        facts.HasSpots.ShouldBeTrue();

        // The customer source is byte-identical and the automation lock is free.
        File.ReadAllBytes(source).ShouldBe(bytes);
        (await h.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
    }

    /// <summary>
    /// A spot channel that is not white ink is treated the same way, because the rule is about
    /// what a PSD <i>is</i> — visual artwork — and not about which ink the channel names.
    /// </summary>
    /// <remarks>
    /// The chosen behaviour, stated explicitly rather than left implicit: source spot semantics
    /// are ignored and the visual composite is accepted. A <c>SPOTCOLOR</c> channel is never on
    /// its own a reason to refuse. Preparation still refuses whenever Photoshop cannot produce a
    /// truthful composite safely, which is what the untouched SCRUM-11099 refusals cover.
    /// </remarks>
    [Theory]
    [InlineData("W1", "SPOTCOLOR")]
    [InlineData("PANTONE 485 C", "SPOTCOLOR")]
    [InlineData("Varnish", "SPOTCOLOR")]
    [InlineData("W1", "MASKEDAREA")]
    public async Task No_source_channel_name_or_kind_is_on_its_own_a_refusal(string name, string kind)
    {
        using SessionServiceHarness h = new();
        VisualPsdProcessor adapter = new(h.FileWorkspace) { Spot = new PsdChannel(name, kind) };
        ISessionService service = h.CreateServiceWithPhotoshop(adapter);

        string source = h.Workspace.CreateSourceFile("spot.psd", ExistingW1Psd());
        var imported = await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, source, null, "qa", CancellationToken.None);
        var prepared = await service.ExecuteAsync(
            imported.Value.Id,
            new WorkflowCommand.StartStep(StepKind.OriginalConfirmation),
            "qa",
            CancellationToken.None);

        prepared.IsSuccess.ShouldBeTrue(prepared.IsFailure ? prepared.Failure.ToString() : "");
        (await h.Repository.LoadAsync(imported.Value.Id, CancellationToken.None)).Value!
            .Revisions.Count(r => r.Operation == OperationKind.PreparePsd).ShouldBe(1);
    }

    /// <summary>
    /// The Product offers no Retain-versus-Regenerate choice, in this state or in any other,
    /// because PrintFlow does not retain source white ink at all.
    /// </summary>
    /// <remarks>
    /// This replaces the SCRUM-11101 pre-fix reproduction, which asserted the same absence as a
    /// <i>gap</i> to be closed by a future decision path. Under the superseding business contract
    /// the absence is the contract, so the assertion stays and its meaning inverts: it is now
    /// expected to hold permanently rather than to change.
    /// </remarks>
    [Fact]
    public async Task No_retain_or_regenerate_white_ink_decision_exists_anywhere()
    {
        using SessionServiceHarness h = new();
        VisualPsdProcessor adapter = new(h.FileWorkspace) { Spot = new PsdChannel("W1", "SPOTCOLOR") };
        ISessionService service = h.CreateServiceWithPhotoshop(adapter);

        string source = h.Workspace.CreateSourceFile("existing-w1.psd", ExistingW1Psd());
        var imported = await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, source, null, "qa", CancellationToken.None);
        await service.ExecuteAsync(
            imported.Value.Id,
            new WorkflowCommand.StartStep(StepKind.OriginalConfirmation),
            "qa",
            CancellationToken.None);

        var state = (await h.Repository.LoadAsync(imported.Value.Id, CancellationToken.None)).Value!;
        WorkflowSnapshot snapshot = state.ToSnapshot();

        // The step is in ordinary review, not in any decision-required state.
        snapshot.CurrentStep.ShouldNotBeNull().State.ShouldBe(StepState.ReviewRequired);

        string[] offered = [.. WorkflowEngine.Instance.AvailableCommands(snapshot).Select(k => k.ToString())];
        offered.ShouldNotContain(name => name.Contains("WhiteInk", StringComparison.OrdinalIgnoreCase));
        offered.ShouldNotContain(name => name.Contains("Retain", StringComparison.OrdinalIgnoreCase));
        offered.ShouldNotContain(name => name.Contains("Regenerate", StringComparison.OrdinalIgnoreCase));

        // The whole closed vocabulary agrees: the concept is absent from the Product, not merely
        // unavailable here.
        string[] everyCommandKind = [.. Enum.GetNames<CommandKind>()];
        everyCommandKind.ShouldNotContain(name =>
            name.Contains("WhiteInkDecision", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RetainExisting", StringComparison.OrdinalIgnoreCase));

        // Nothing recorded a white-ink authority from the source, so no later step can read one.
        snapshot.WhiteUnderbaseBranch.ShouldBeNull();
    }

    /// <summary>
    /// A source white-ink channel does not survive into the artefact the rest of the Product uses:
    /// the prepared Revision is an ordinary RGB PNG raster with no production channel of any kind.
    /// </summary>
    /// <remarks>
    /// PNG colour type 2 or 6 at eight bits is RGB, or RGB with alpha, and nothing else — the
    /// format has no way to carry a spot channel. That is the structural reason source ink cannot
    /// reach the production TIFF: everything downstream of review resolves this artefact through
    /// the step lineage, and the signed Action generates W1 from it.
    /// </remarks>
    [Fact]
    public async Task Prepared_raster_carries_no_production_channel_from_the_source()
    {
        using SessionServiceHarness h = new();
        VisualPsdProcessor adapter = new(h.FileWorkspace) { Spot = new PsdChannel("W1", "SPOTCOLOR") };
        ISessionService service = h.CreateServiceWithPhotoshop(adapter);

        string source = h.Workspace.CreateSourceFile("existing-w1.psd", ExistingW1Psd());
        var imported = await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, source, null, "qa", CancellationToken.None);
        (await service.ExecuteAsync(
            imported.Value.Id,
            new WorkflowCommand.StartStep(StepKind.OriginalConfirmation),
            "qa",
            CancellationToken.None)).IsSuccess.ShouldBeTrue();

        var state = (await h.Repository.LoadAsync(imported.Value.Id, CancellationToken.None)).Value!;
        Revision raster = state.Revisions.Single(r => r.Operation == OperationKind.PreparePsd);

        raster.Facts.Format.ShouldBe(ImageFormat.Png);
        raster.Facts.ColourMode.ShouldBe(ColourMode.Rgb);

        byte[] header = new byte[26];
        using (FileStream stream = File.OpenRead(h.FileWorkspace.ResolveAbsolute(raster.File)))
        {
            stream.ReadExactly(header);
        }

        header[24].ShouldBe((byte)8);
        header[25].ShouldBeOneOf((byte)2, (byte)6);
    }

    /// <summary>
    /// Removing the white-ink refusal does not let preparation touch the customer's file, and does
    /// not regress restart: a resumed session finds the same managed raster and re-prepares nothing.
    /// </summary>
    [Fact]
    public async Task Source_is_unchanged_and_restart_reuses_the_same_managed_raster()
    {
        using SessionServiceHarness h = new();
        VisualPsdProcessor adapter = new(h.FileWorkspace) { Spot = new PsdChannel("W1", "SPOTCOLOR") };
        ISessionService service = h.CreateServiceWithPhotoshop(adapter);

        byte[] bytes = ExistingW1Psd();
        string sourceShaBefore = Convert.ToHexString(SHA256.HashData(bytes));
        string source = h.Workspace.CreateSourceFile("existing-w1.psd", bytes);
        var imported = await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, source, null, "qa", CancellationToken.None);
        var id = imported.Value.Id;
        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation),
            "qa", CancellationToken.None)).IsSuccess.ShouldBeTrue();

        var state = (await h.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision raster = state.Revisions.Single(r => r.Operation == OperationKind.PreparePsd);
        Revision root = state.Revisions.Single(r => r.IsRoot);

        // The visual-only contract does not permit modifying customer input, on either copy.
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ShouldBe(sourceShaBefore);
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(root.File))))
            .ShouldBe(sourceShaBefore);

        // A fresh service over the same database and workspace has no in-memory result.
        ISessionService restarted = h.CreateServiceWithPhotoshop(adapter);
        var resumed = await restarted.LoadAsync(id, CancellationToken.None);
        resumed.Value.CurrentArtefact!.RevisionId.ShouldBe(raster.Id);
        resumed.Value.CurrentArtefact.Sha256.ShouldBe(raster.Sha256);

        // Loading a prepared session never drives Photoshop again.
        adapter.Calls.ShouldBe(1);

        (await restarted.ExecuteAsync(id,
            new WorkflowCommand.Approve(StepKind.OriginalConfirmation, raster.Sha256),
            "qa", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        adapter.Calls.ShouldBe(1);
    }

    /// <summary>
    /// A Photoshop double that behaves as the accepted Production PSD path now does for a document
    /// carrying a source ink channel: it inspects, reports every channel it saw, writes the visual
    /// raster, and succeeds.
    /// </summary>
    /// <remarks>
    /// The reported channel list carries the spot alongside the three components, because that is
    /// what Photoshop returns; the written PNG carries only the composite, because the fixed JSX
    /// removes every non-component channel from its disposable duplicate before saving. The gap
    /// between those two facts is the whole contract, so the double reproduces both rather than
    /// tidying either away.
    /// </remarks>
    private sealed class VisualPsdProcessor(IWorkspace workspace) : IPhotoshopOutputProcessor
    {
        public string AdapterId => "test-visual-only-psd-v1";

        public AdapterExecutionMode Mode => AdapterExecutionMode.Fake;

        public int Calls { get; private set; }

        /// <summary>The one non-component channel the source document declares.</summary>
        public required PsdChannel Spot { get; init; }

        public Task<OperationResult<AdapterOutput>> GenerateAsync(
            PhotoshopRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("These tests stop at visual preparation.");

        public Task<OperationResult<AdapterOutput>> PreparePsdAsync(
            PsdPreparationRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            File.WriteAllBytes(workspace.ResolveAbsolute(request.ExpectedOutput), SyntheticImages.Png(4, 3));

            return Task.FromResult(OperationResult.Ok(
                new AdapterOutput(request.ExpectedOutput, TimeSpan.Zero, "visual-only psd preparation")
                {
                    PsdInspection = new PsdInspection(
                        PixelWidth: 4,
                        PixelHeight: 3,
                        OriginalMode: "RGB",
                        BitDepth: 8,
                        HasRealMergedData: true,
                        HasTransparency: true,
                        Channels:
                        [
                            new PsdChannel("Red", "COMPONENT"),
                            new PsdChannel("Green", "COMPONENT"),
                            new PsdChannel("Blue", "COMPONENT"),
                            Spot,
                        ],
                        PhotoshopVersion: "test-double"),
                }));
        }
    }
}
