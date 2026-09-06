using System.IO;
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
/// SCRUM-11101 §3: the deterministic reproduction of the behaviour that exists <b>before</b> any
/// Product change, preserved so the replacement decision path can be shown to replace something
/// real rather than something described.
/// </summary>
/// <remarks>
/// The original Jira row (CSV Work Item ID <c>11408</c>, "Handle Existing White-Ink Spot Channels
/// Explicitly") requires PrintFlow to <i>stop and ask the operator whether to retain the existing
/// white ink or regenerate white ink</i>. What SCRUM-11099 accepted instead was a safe refusal:
/// existing W1 is detected, the inspection is retained, and preparation fails. That is not wrong —
/// it is deliberately not a silent destructive choice — but it is not the acceptance criterion
/// either, because no operator decision exists anywhere in the Product.
/// <para>
/// These tests assert exactly that gap, and they are expected to <b>change</b> when the decision
/// path lands. They are written against the session-level seam rather than the native bridge so
/// the thing being reproduced is the operator-visible behaviour — a refused step and an absent
/// choice — rather than one adapter's return value.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class ExistingWhiteInkPreFixReproductionTests
{
    /// <summary>
    /// A PSD carrying exactly one existing W1 spot channel with non-empty content, which is the
    /// input the Jira row is about.
    /// </summary>
    /// <remarks>
    /// The bytes come from the same synthetic builder every other PSD test uses, with its
    /// <c>spot</c> arrangement: image resources 1006 (alpha channel names, "W1") and 1007
    /// (DisplayInfo, a spot ink at 100% solidity), a fourth stored plane, and a full-canvas
    /// non-white W1 plane. No customer artwork is involved anywhere in this file.
    /// </remarks>
    private static byte[] ExistingW1Psd() => PsdInputPreparationTests.RgbCompositePsd(spot: true);

    [Fact]
    public async Task Existing_W1_reaches_inspection_and_is_refused_with_no_prepared_revision()
    {
        using SessionServiceHarness h = new();
        ExistingW1PsdProcessor adapter = new(h.FileWorkspace);
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

        // 1. Preparation reached Photoshop inspection: the adapter was actually called, so the
        //    refusal is a decision taken on observed facts rather than on the file extension.
        adapter.Calls.ShouldBe(1);

        // 2. It refused, and specifically as an unsupported input.
        prepared.IsFailure.ShouldBeTrue();
        prepared.Failure.Code.ShouldBe(FailureCode.PsdUnsupported);

        var state = (await h.Repository.LoadAsync(imported.Value.Id, CancellationToken.None)).Value!;

        // 3. The observed W1 facts survive on the attempt, which is what makes the refusal
        //    auditable — and what a decision path would later have to build on.
        ProcessingAttempt attempt = state.Attempts.Single(a => a.Operation == OperationKind.PreparePsd);
        attempt.Status.ShouldBe(AttemptStatus.Failed);
        PsdInspection facts = attempt.PsdInspection.ShouldNotBeNull();
        facts.HasW1.ShouldBeTrue();
        facts.HasSpots.ShouldBeTrue();

        // 4. No prepared Revision exists: only the import root.
        state.Revisions.Count.ShouldBe(1);
        state.Revisions.Single().IsRoot.ShouldBeTrue();
        attempt.OutputRevisionId.ShouldBeNull();

        // 5. The customer source is byte-identical and the automation lock is free.
        File.ReadAllBytes(source).ShouldBe(bytes);
        (await h.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task No_operator_decision_exists_anywhere_after_existing_W1_is_detected()
    {
        using SessionServiceHarness h = new();
        ExistingW1PsdProcessor adapter = new(h.FileWorkspace);
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

        // The step is Failed, which is the generic "this did not produce anything" state. It is
        // emphatically not a decision-required state, and nothing distinguishes this refusal from
        // a corrupt PSD or an unsupported colour mode.
        snapshot.CurrentStep.ShouldNotBeNull().State.ShouldBe(StepState.Failed);

        // The entire command vocabulary the engine will accept here. Retry and the ordinary
        // session-scoped commands are offered; there is no retain/regenerate choice among them,
        // because no such command exists in the Product at all.
        IReadOnlyList<CommandKind> available = WorkflowEngine.Instance.AvailableCommands(snapshot);
        available.ShouldContain(CommandKind.Retry);

        string[] offered = [.. available.Select(kind => kind.ToString())];
        offered.ShouldNotContain(name => name.Contains("WhiteInk", StringComparison.OrdinalIgnoreCase));
        offered.ShouldNotContain(name => name.Contains("Retain", StringComparison.OrdinalIgnoreCase));
        offered.ShouldNotContain(name => name.Contains("Regenerate", StringComparison.OrdinalIgnoreCase));

        // And the whole closed command vocabulary agrees: the concept is absent, not merely
        // unavailable in this state. This is the assertion that must change when the decision
        // lands, and it is deliberately written so that it does.
        string[] everyCommandKind = [.. Enum.GetNames<CommandKind>()];
        everyCommandKind.ShouldNotContain(name => name.Contains("WhiteInkDecision", StringComparison.OrdinalIgnoreCase));

        // Nothing recorded a white-ink authority either, so there is no persisted decision a
        // later step could read.
        snapshot.WhiteUnderbaseBranch.ShouldBeNull();
    }

    /// <summary>
    /// A Photoshop double that behaves exactly as the accepted Production PSD path does for an
    /// existing-W1 document: it inspects, reports the observed channels, and refuses.
    /// </summary>
    /// <remarks>
    /// The refusal shape mirrors <c>ProductionPhotoshopOutputProcessor.PreparePsdAsync</c> — the
    /// failure is <see cref="FailureCode.PsdUnsupported"/> and the inspection travels on the
    /// failure so the attempt can persist it — rather than re-implementing the native bridge.
    /// Nothing is written to the output path, because the real path never gets that far.
    /// </remarks>
    private sealed class ExistingW1PsdProcessor(IWorkspace workspace) : IPhotoshopOutputProcessor
    {
        public string AdapterId => "test-existing-w1-psd-v1";

        public AdapterExecutionMode Mode => AdapterExecutionMode.Fake;

        public int Calls { get; private set; }

        public Task<OperationResult<AdapterOutput>> GenerateAsync(
            PhotoshopRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This reproduction never reaches production output.");

        public Task<OperationResult<AdapterOutput>> PreparePsdAsync(
            PsdPreparationRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            _ = workspace.ResolveAbsolute(request.ExpectedOutput);

            PsdInspection inspection = new(
                PixelWidth: 4,
                PixelHeight: 3,
                OriginalMode: "RGB",
                BitDepth: 8,
                HasRealMergedData: true,
                HasTransparency: null,
                Channels:
                [
                    new PsdChannel("Red", "COMPONENT"),
                    new PsdChannel("Green", "COMPONENT"),
                    new PsdChannel("Blue", "COMPONENT"),
                    new PsdChannel("W1", "SPOTCOLOR"),
                ],
                PhotoshopVersion: "test-double");

            return Task.FromResult(OperationResult.Fail<AdapterOutput>(
                OperationFailure.Create(
                    FailureCode.PsdUnsupported,
                    "PSD was not prepared: Existing W1/spot channels require an operator decision.")
                with
                { PsdInspection = inspection }));
        }
    }
}
