using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.WorkstationEntry.Scenarios;

/// <summary>All dependencies are the already constructed services of one reviewed, owned run.</summary>
public sealed record ScenarioServices(
    string Root,
    ISessionService Sessions,
    IWorkspace Workspace,
    ISessionRepository Repository,
    IApprovedArtifactDeliveryService Delivery,
    IProductionTiffReviewService TiffReview,
    FakeMeituProcessor FakeMeitu,
    FakePhotoshopOutputProcessor FakePhotoshop,
    Action<string> AdmitFixture,
    Action<string> ValidateOwnedWrite,
    Action<Task> TrackOwnedTask);

/// <summary>
/// Uses product commands and read models. Every mutation is logged from a fresh repository read.
/// This is synthetic, headless fixture preparation; it makes no keyboard or workstation claim.
/// </summary>
public static class ScenarioRunner
{
    private const string Operator = "synthetic-entry-fixture";

    public static async Task<ScenarioLedger> RunAsync(ScenarioServices services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!Path.IsPathFullyQualified(services.Root))
            throw new ArgumentException("An already validated, absolute run root is required.", nameof(services));

        ScenarioLedger ledger = new();
        try
        {
            await Run(ledger, "F1", "Asset trim R1, draft, R2 and exact PNG promotion", s => F1(services, s, ct), ct);
            await Run(ledger, "F1K", "KeepOriginalExtent approval gap", s => F1K(services, s, ct), ct);
            await Run(ledger, "F2D", "Direct coherent TIFF", s => F2(services, s, WorkflowType.GeneratePrintTiff, ct), ct);
            await Run(ledger, "F2C", "Customer-design coherent TIFF", s => F2(services, s, WorkflowType.PrepareCustomerDesign, ct), ct);
            await Run(ledger, "F3", "Enlargement authority and a second size", s => F3(services, s, ct), ct);
            await Run(ledger, "F4", "Bound correction return", s => F4(services, s, ct), ct);
            await Run(ledger, "F5", "Failure, retry and Recent identities", s => F5(services, s, ct), ct);
            await Run(ledger, "F6", "6x5 / 200x150 maximum-box mismatch", s => F6(services, s, ct), ct);
        }
        finally
        {
            ledger.FinishedUtc = DateTimeOffset.UtcNow;
            AddCoverage(ledger);
            ledger.Save(Path.Combine(services.Root, "evidence", "scenario-ledger.json"),
                services.ValidateOwnedWrite);
        }
        return ledger;
    }

    private static async Task Run(ScenarioLedger ledger, string id, string purpose,
        Func<ScenarioEntry, Task> action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ScenarioEntry entry = new(id, purpose);
        ledger.Scenarios.Add(entry);
        try
        {
            await action(entry);
            entry.Status = "VERIFIED_SYNTHETIC_SERVICE_FACTS";
        }
        catch (ScenarioGap gap)
        {
            entry.Status = "BLOCKED_REQUIRED_SCENARIO";
            entry.Limitation = gap.Message;
        }
        catch (ScenarioFatal fatal)
        {
            entry.Status = "INCOMPLETE_OWNED_TASK";
            entry.Limitation = fatal.Message;
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            entry.Status = "INCOMPLETE_CANCELLED";
            throw;
        }
        catch (Exception ex)
        {
            entry.Status = "FAILED";
            entry.Limitation = ex.GetType().Name + ": " + ex.Message;
        }
    }

    private static async Task F1(ScenarioServices c, ScenarioEntry e, CancellationToken ct)
    {
        SessionView v = await Import(c, e, WorkflowType.PrepareAsset, 1200, 900, "asset", ct,
            (x, y) => x is >= 117 and < 1017 && y is >= 91 and < 691);
        v = await Command(c, e, v, new WorkflowCommand.ConfirmOriginal(), "confirm original", ct);
        v = await ProduceAndApprove(c, e, v, StepKind.Enhancement, ct);
        v = await AuthoriseBackground(c, e, v, ct);
        v = await ProduceAndApprove(c, e, v, StepKind.BackgroundRemoval, ct);
        v = await Command(c, e, v, new WorkflowCommand.StartStep(StepKind.Trim), "automatic trim R1", ct);
        if (v.CurrentStep?.Step != StepKind.Trim || v.CurrentStep.State != StepState.ReviewRequired ||
            v.TrimAdjustment is not { } target)
            throw new ScenarioGap("The current product did not offer an exact trim adjustment at R1.");
        if (v.CurrentArtefact?.Facts is not { PixelWidth: 900, PixelHeight: 600 })
            throw new ScenarioGap("R1 did not retain the 900x600 alpha-content extent.");
        e.Facts.Add(Fact("crop-editor cancel NOT_RUN; R1 reload before adjustment", ("revision", target.ResultRevisionId.ToString()),
            ("sha256", target.ResultSha256.ToString()), ("source", target.PreTrimRevisionId.ToString())));
        SessionView afterCancel = Must(await c.Sessions.LoadAsync(v.Id, ct), "reload after cancel");
        if (afterCancel.TrimAdjustment is null || !afterCancel.TrimAdjustment.SameTargetAs(target))
            throw new InvalidOperationException("Cancelling a draft did not preserve R1 as the current review target.");
        TrimBounds crop = TrimBounds.FromEdges(130, 100, 1000, 680);
        v = await Command(c, e, v, new WorkflowCommand.AdjustTrimFromReview(
            target.ResultRevisionId, target.ResultSha256, target.PreTrimRevisionId, target.PreTrimSha256, crop),
            "submit exact adjustment R2", ct);
        if (v.CurrentStep?.State != StepState.ReviewRequired ||
            v.CurrentStep.CurrentRevisionId == target.ResultRevisionId)
            throw new InvalidOperationException("R2 did not become its own review target.");
        if (v.CurrentArtefact?.Facts is not { PixelWidth: 870, PixelHeight: 580 })
            throw new ScenarioGap("R2 did not hold the submitted 870x580 crop.");
        v = await Approve(c, e, v, StepKind.Trim, ct);
        var reviewed = v.Steps.Single(x => x.Step == StepKind.Trim);
        v = Must(await c.Sessions.PromoteReviewedPngAsync(v.Id, reviewed.CurrentRevisionId!.Value,
            reviewed.CurrentRevisionSha256!.Value, Operator, ct), "promote exact reviewed PNG");
        await Capture(c, e, v, "promote exact reviewed PNG", ct);
        await Deliver(c, e, v, ArtifactKind.ApprovedAssetPng,
            v.Steps.Single(x => x.Step == StepKind.ApprovedPngExport).CurrentRevisionId!.Value.Value,
            "asset-reviewed.png", ct);
    }

    private static async Task F1K(ScenarioServices c, ScenarioEntry e, CancellationToken ct)
    {
        SessionView v = await Import(c, e, WorkflowType.PrepareAsset, 10, 8, "keep-extent", ct,
            (x, y) => true);
        v = await Command(c, e, v, new WorkflowCommand.ConfirmOriginal(), "confirm original", ct);
        v = await ProduceAndApprove(c, e, v, StepKind.Enhancement, ct);
        v = await AuthoriseBackground(c, e, v, ct);
        v = await ProduceAndApprove(c, e, v, StepKind.BackgroundRemoval, ct);
        if (!v.CanKeepOriginalExtent)
            throw new ScenarioGap("KeepOriginalExtent was not a lawful current command.");
        var approvedSource = v.Steps.Single(x => x.Step == StepKind.BackgroundRemoval);
        v = await Command(c, e, v, new WorkflowCommand.KeepOriginalExtent(), "retain original extent", ct);
        OperationResult<SessionView> promotion = await c.Sessions.PromoteReviewedPngAsync(
            v.Id, approvedSource.CurrentRevisionId!.Value,
            approvedSource.CurrentRevisionSha256!.Value, Operator, ct);
        e.Facts.Add(Fact("KeepOriginalExtent promotion attempt", ("result", promotion.IsSuccess ? "SUCCESS" :
            promotion.Failure.Code.ToString())));
        if (promotion.IsSuccess)
            throw new InvalidOperationException("Unexpected positive KeepOriginalExtent promotion; inspect approval lineage before use.");
        await Capture(c, e, v, "negative KeepOriginalExtent", ct);
    }

    private static async Task F2(ScenarioServices c, ScenarioEntry e, WorkflowType workflow, CancellationToken ct)
    {
        SessionView v = await Import(c, e, workflow, 2400, 1800, "coherent-tiff", ct,
            (x, y) => true);
        v = await Command(c, e, v, new WorkflowCommand.ConfirmOriginal(), "confirm original", ct);
        if (workflow == WorkflowType.PrepareCustomerDesign)
        {
            v = await ProduceAndApprove(c, e, v, StepKind.Enhancement, ct);
            v = await AuthoriseBackground(c, e, v, ct);
            v = await ProduceAndApprove(c, e, v, StepKind.BackgroundRemoval, ct);
            v = await Command(c, e, v, new WorkflowCommand.KeepOriginalExtent(), "retain whole approved canvas", ct);
        }
        v = await Command(c, e, v, new WorkflowCommand.SetCustomTargetEdgeSize(TargetEdge.LongEdge, 152.4m),
            "set target long edge", ct);
        if (v.Preflight is not { OutputPixelWidth: 1800, OutputPixelHeight: 1350, RequiresEnlargement: false })
            throw new ScenarioGap("Current preflight differs from 1800x1350 non-enlarged target-edge fixture.");
        v = await Command(c, e, v, new WorkflowCommand.SelectWhiteUnderbaseBranch(
            WhiteUnderbaseBranch.W1_1px, "synthetic ordinary-design fixture"), "select W1 branch", ct);
        c.FakePhotoshop.SetTiffOutput(FakePhotoshopTiffOutput.ValidTiff);
        v = await Command(c, e, v, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "produce valid fake TIFF", ct);
        await CheckTiff(c, e, v, 1800, 1350, ct);
        ArtifactKey pending = new(v.Id, ArtifactKind.ApprovedPrintTiff, v.Outputs.Last().Id.Value);
        (DeliveryOffer? premature, ArtifactRefusal beforeApproval) = await c.Delivery.GetOfferAsync(pending, ct);
        e.Facts.Add(Fact("approval-before-export", ("outputId", pending.ArtifactId.ToString()),
            ("offerBeforeApproval", premature is null ? "none" : "present"),
            ("refusal", beforeApproval.ToString())));
        if (premature is not null || beforeApproval == ArtifactRefusal.None)
            throw new InvalidOperationException("Unapproved TIFF unexpectedly obtained a delivery offer.");
        v = await Approve(c, e, v, StepKind.PhotoshopOutput, ct);
        await Deliver(c, e, v, ArtifactKind.ApprovedPrintTiff,
            v.Outputs.Single(x => x.ReviewState == PrintFlow.Domain.Revisions.ReviewState.Approved).Id.Value,
            workflow == WorkflowType.GeneratePrintTiff ? "direct-reviewed.tif" : "customer-reviewed.tif", ct);
    }

    private static async Task F3(ScenarioServices c, ScenarioEntry e, CancellationToken ct)
    {
        SessionView v = await Import(c, e, WorkflowType.GeneratePrintTiff, 600, 400, "enlarge", ct,
            (x, y) => true);
        v = await Command(c, e, v, new WorkflowCommand.ConfirmOriginal(), "confirm original", ct);
        v = await Command(c, e, v, new WorkflowCommand.SetCustomTargetEdgeSize(TargetEdge.LongEdge, 152.4m),
            "set enlargement target", ct);
        if (v.Preflight is not { OutputPixelWidth: 1800, OutputPixelHeight: 1200,
                RequiresEnlargement: true, EnlargementAuthorised: false } preflight ||
            preflight.EnlargementOfferId is null)
            throw new ScenarioGap("Current preflight lacks the bound 1800x1200 enlargement offer.");
        v = await Command(c, e, v, new WorkflowCommand.SelectWhiteUnderbaseBranch(
            WhiteUnderbaseBranch.W1_1px, "synthetic fixture"), "select W1 branch", ct);
        OperationResult<SessionView> refused = await c.Sessions.ExecuteAsync(v.Id,
            new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), Operator, ct);
        e.Facts.Add(Fact("start before enlargement authority", ("result", refused.IsFailure ?
            refused.Failure.Code.ToString() : "SUCCESS")));
        if (refused.IsSuccess) throw new InvalidOperationException("TIFF started without explicit enlargement authority.");
        Guid currentOffer = v.Preflight?.EnlargementOfferId ??
            throw new ScenarioGap("Current enlargement offer disappeared before explicit confirmation.");
        v = Must(await c.Sessions.AuthoriseCurrentEnlargementAsync(v.Id, currentOffer,
            Operator, ct), "authorise current enlargement");
        await Capture(c, e, v, "authorise current enlargement", ct);
        if (v.Preflight?.EnlargementAuthorised != true)
            throw new ScenarioGap("The exact current enlargement offer did not become authorised.");
        c.FakePhotoshop.SetTiffOutput(FakePhotoshopTiffOutput.ValidTiff);
        v = await Command(c, e, v, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "produce enlarged fake TIFF", ct);
        await CheckTiff(c, e, v, 1800, 1200, ct);
        v = await Approve(c, e, v, StepKind.PhotoshopOutput, ct);
        v = await Command(c, e, v, new WorkflowCommand.Complete(), "complete first size", ct);
        v = await Command(c, e, v, new WorkflowCommand.AddAnotherSize(), "add another size", ct);
        v = await Command(c, e, v, new WorkflowCommand.SetCustomTargetEdgeSize(TargetEdge.LongEdge, 101.6m),
            "change size and withdraw prior authority", ct);
        if (v.Preflight?.EnlargementAuthorised == true)
            throw new InvalidOperationException("Changing size retained old enlargement authority.");
    }

    private static async Task F4(ScenarioServices c, ScenarioEntry e, CancellationToken ct)
    {
        SessionView v = await Import(c, e, WorkflowType.PrepareAsset, 640, 480, "correction", ct,
            (x, y) => true);
        v = await Command(c, e, v, new WorkflowCommand.ConfirmOriginal(), "confirm original", ct);
        v = await ProduceAndApprove(c, e, v, StepKind.Enhancement, ct);
        v = await AuthoriseBackground(c, e, v, ct);
        v = await Command(c, e, v, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval),
            "background R1 review", ct);
        if (!v.CanAskColleague || v.CurrentStep?.CurrentRevisionId is null ||
            v.CurrentStep.CurrentRevisionSha256 is null)
            throw new ScenarioGap("The product did not offer correction for exact background R1.");
        Guid request = Guid.NewGuid();
        v = Must(await c.Sessions.RequestColleagueCorrectionAsync(v.Id, request,
            v.CurrentStep.CurrentRevisionId.Value, v.CurrentStep.CurrentRevisionSha256.Value,
            "synthetic fixture request", CorrectionFileNaming.English, Operator, ct), "prepare correction request");
        await Capture(c, e, v, "prepare correction request", ct);
        if (v.Correction is not { CanImport: true } correction || correction.RequestId != request)
            throw new ScenarioGap("Correction request did not persist as importable.");
        e.Facts.Add(Fact("synthetic cancellation boundary without picker", ("requestId", request.ToString()),
            ("effect", "no picker opened; no import command invoked")));
        string wrong = WritePng(c, e, "returns", "wrong-size.png", 12, 10, (_, _) => true);
        OperationResult<SessionView> invalid = await c.Sessions.ImportCorrectedImageAsync(v.Id, request,
            correction.HandedOutRevisionId, correction.HandedOutSha256, wrong, Operator, ct);
        e.Facts.Add(Fact("wrong-size return", ("result", invalid.IsFailure ? invalid.Failure.Code.ToString() : "SUCCESS")));
        if (invalid.IsSuccess) throw new InvalidOperationException("Mismatched correction dimensions were accepted.");
        SessionView afterRefusal = Must(await c.Sessions.LoadAsync(v.Id, ct), "reload refused correction");
        if (afterRefusal.Correction?.RequestId != request || !afterRefusal.CanImportCorrectedImage)
            throw new InvalidOperationException("Refused corrected image did not preserve the exact open request.");
        string valid = WritePng(c, e, "returns", "corrected.png", correction.RequiredPixelWidth,
            correction.RequiredPixelHeight, (x, y) => x > 20 && y > 20);
        v = Must(await c.Sessions.ImportCorrectedImageAsync(v.Id, request,
            correction.HandedOutRevisionId, correction.HandedOutSha256, valid, Operator, ct),
            "import bound synthetic corrected image");
        await Capture(c, e, v, "import bound synthetic corrected image", ct);
        if (v.CurrentStep?.Step != StepKind.BackgroundRemoval ||
            v.CurrentStep.State != StepState.ReviewRequired || v.CorrectionReturn?.RequestId != request)
            throw new ScenarioGap("Corrected return did not become its own background review.");
        e.Facts.Add(Fact("remaining recovery limit", ("bound interrupted import", "NOT_RUN"),
            ("generic recovery", "OPEN_PRODUCT_GAP")));
    }

    private static async Task F5(ScenarioServices c, ScenarioEntry e, CancellationToken ct)
    {
        string sameSource = WritePng(c, e, "inputs", "F5-same-name.png", 20, 16, (_, _) => true);
        SessionView a = Must(await c.Sessions.ImportAsync(WorkflowType.PrepareAsset,
            sameSource, "same-name", Operator, ct), "import first same-name job");
        await Capture(c, e, a, "import first same-name job", ct);
        SessionView b = Must(await c.Sessions.ImportAsync(WorkflowType.PrepareAsset,
            sameSource, "same-name", Operator, ct), "import second same-name job");
        await Capture(c, e, b, "import second same-name job", ct);
        if (a.Id == b.Id) throw new InvalidOperationException("Same-name imports reused a session identity.");
        if (a.OutputName != b.OutputName || a.SourceFileName != b.SourceFileName ||
            a.SourceFileName != Path.GetFileName(sameSource))
            throw new InvalidOperationException("The two distinct jobs did not persist matching display/source names.");
        e.Facts.Add(Fact("same-name persisted names", ("outputName", a.OutputName.ToString()),
            ("sourceFileName", a.SourceFileName), ("firstSessionId", a.Id.ToString()),
            ("secondSessionId", b.Id.ToString())));
        a = await Command(c, e, a, new WorkflowCommand.ConfirmOriginal(), "confirm first", ct);
        c.FakeMeitu.SetScenario(FakeAdapterScenario.FailWith(FailureCode.MeituLaunchFailed));
        try
        {
            OperationResult<SessionView> failed = await c.Sessions.ExecuteAsync(a.Id,
                new WorkflowCommand.StartStep(StepKind.Enhancement), Operator, ct);
            e.Facts.Add(Fact("scripted fake failure result", ("code", failed.IsFailure ?
                failed.Failure.Code.ToString() : "SUCCESS")));
            a = Must(await c.Sessions.LoadAsync(a.Id, ct), "reload after scripted fake failure");
            await Capture(c, e, a, "fake failure", ct);
            if (a.CurrentStep?.State != StepState.Failed || a.CurrentFailureAttemptId is null)
                throw new InvalidOperationException("Scripted fake failure did not yield a real failed attempt.");
            OperationResult<ErrorDetailsView> details = await c.Sessions.LoadErrorDetailsAsync(
                a.Id, a.CurrentFailureAttemptId.Value, ct);
            e.Facts.Add(Fact("error details", ("attemptId", a.CurrentFailureAttemptId.ToString()),
                ("result", details.IsSuccess ? "loaded" : details.Failure.Code.ToString())));
            if (details.IsFailure || details.Value.SessionId != a.Id ||
                details.Value.AttemptId != a.CurrentFailureAttemptId.Value || !details.Value.IsCurrent)
                throw new InvalidOperationException("Full Details did not resolve the exact current failed attempt.");
        }
        finally { c.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed); }
        a = await Command(c, e, a, new WorkflowCommand.Retry(StepKind.Enhancement), "retry failure", ct);
        a = await ProduceAndApprove(c, e, a, StepKind.Enhancement, ct);
        OperationResult<IReadOnlyList<SessionListItem>> recent = await c.Sessions.ListRecentAsync(ct);
        IReadOnlyList<SessionListItem> rows = Must(recent, "recent sessions");
        if (!rows.Any(x => x.Id == a.Id) || !rows.Any(x => x.Id == b.Id))
            throw new InvalidOperationException("Both distinct same-name sessions were not present in Recent.");
        if (rows.Single(x => x.Id == a.Id).OutputName != rows.Single(x => x.Id == b.Id).OutputName)
            throw new InvalidOperationException("Recent displayed different names for the same-name jobs.");
        e.Facts.Add(Fact("recent identities", ("first", a.Id.ToString()), ("second", b.Id.ToString()),
            ("firstState", rows.Single(x => x.Id == a.Id).State.ToString()),
            ("secondState", rows.Single(x => x.Id == b.Id).State.ToString())));
        b = await Command(c, e, b, new WorkflowCommand.AbandonSession("synthetic abandoned variant"),
            "abandon second session", ct);
        await ExerciseMeituStop(c, e, AutomationStopMode.StopOperation, ct);
        await ExerciseMeituStop(c, e, AutomationStopMode.TakeOver, ct);
        e.Facts.Add(Fact("unsupported Photoshop stop slice",
            ("Photoshop phase stop", "UNSUPPORTED_CURRENT_PORT")));
    }

    private static async Task ExerciseMeituStop(ScenarioServices c, ScenarioEntry e,
        AutomationStopMode mode, CancellationToken ct)
    {
        SessionView v = await Import(c, e, WorkflowType.PrepareAsset, 20, 16,
            mode.ToString(), ct, (_, _) => true);
        v = await Command(c, e, v, new WorkflowCommand.ConfirmOriginal(), "confirm stop candidate", ct);
        c.FakeMeitu.SetScenario(FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));
        using CancellationTokenSource bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(TimeSpan.FromSeconds(15));
        Task<OperationResult<SessionView>>? running = null;
        try
        {
            running = c.Sessions.ExecuteAsync(v.Id,
                new WorkflowCommand.StartStep(StepKind.Enhancement), Operator, bounded.Token);
            c.TrackOwnedTask(running);
            await c.FakeMeitu.HangStarted.WaitAsync(TimeSpan.FromSeconds(10), bounded.Token);
            AutomationRuntimeView runtime = c.Sessions.GetAutomationRuntime(v.Id);
            e.Facts.Add(Fact("synthetic fake attempt in flight", ("sessionId", v.Id.ToString()),
                ("runtime", runtime.ToString()), ("stopMode", mode.ToString()),
                ("external application", "none; fake adapter only")));
            OperationResult<Unit> requested = c.Sessions.RequestStop(v.Id, mode);
            if (requested.IsFailure)
                throw new ScenarioGap("Lawful " + mode + " request refused: " + requested.Failure.Code);
            OperationResult<SessionView> finished = await running.WaitAsync(TimeSpan.FromSeconds(10), bounded.Token);
            v = Must(await c.Sessions.LoadAsync(v.Id, ct), "reload stopped attempt");
            await Capture(c, e, v, mode + " stopped attempt", ct);
            e.Facts.Add(Fact("stop result", ("commandResult", finished.IsSuccess ? "SUCCESS" :
                finished.Failure.Code.ToString()), ("sessionState", v.State.ToString()),
                ("stepState", v.Steps.Single(x => x.Step == StepKind.Enhancement).State.ToString())));
            if (mode == AutomationStopMode.TakeOver && v.State != SessionState.HandedOff)
                throw new InvalidOperationException("Take Over did not leave a real handed-off session.");
            if (mode == AutomationStopMode.StopOperation &&
                v.Steps.Single(x => x.Step == StepKind.Enhancement).State != StepState.Interrupted)
                throw new InvalidOperationException("Stop did not leave an interrupted attempt.");
        }
        finally
        {
            bounded.Cancel();
            try
            {
                if (running is not null)
                {
                    try { await running.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); }
                    catch (OperationCanceledException) { }
                    catch (TimeoutException)
                    {
                        throw new ScenarioFatal("Owned synthetic Meitu task did not quiesce after cancellation.");
                    }
                }
            }
            finally { c.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed); }
        }
    }

    private static async Task F6(ScenarioServices c, ScenarioEntry e, CancellationToken ct)
    {
        SessionView v = await Import(c, e, WorkflowType.GeneratePrintTiff, 6, 5, "max-box-negative", ct,
            (_, _) => true);
        v = await Command(c, e, v, new WorkflowCommand.ConfirmOriginal(), "confirm original", ct);
        v = await Command(c, e, v, new WorkflowCommand.SetPrintDimensions(
            PrintDimensions.FromMillimetres(200, 150, SizePreset.Custom)), "set 200x150 maximum box", ct);
        v = await Command(c, e, v, new WorkflowCommand.SelectWhiteUnderbaseBranch(
            WhiteUnderbaseBranch.W1_1px, "synthetic fixture"), "select W1 branch", ct);
        c.FakePhotoshop.SetTiffOutput(FakePhotoshopTiffOutput.ValidTiff);
        v = await Command(c, e, v, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput),
            "produce 6x5 TIFF", ct);
        var output = v.Outputs.Single();
        var review = Must(await c.TiffReview.GetReviewAsync(v.Id,
            v.CurrentStep!.CurrentRevisionId!.Value, ct), "decode F6 TIFF review");
        if (review.PixelWidth != 6 || review.PixelHeight != 5)
            throw new InvalidOperationException("F6 did not reproduce the source-derived 6x5 preparation.");
        if (Math.Abs(output.Dimensions.WidthMm - 200) > 0.001 ||
            Math.Abs(output.Dimensions.HeightMm - 150) > 0.001)
            throw new InvalidOperationException("F6 output row did not retain the 200x150 maximum box.");
        e.Facts.Add(Fact("negative maximum-box comparison", ("outputId", output.Id.ToString()),
            ("rowMaximumMm", $"{output.Dimensions.WidthMm}x{output.Dimensions.HeightMm}"),
            ("decodedPixels", $"{review.PixelWidth}x{review.PixelHeight}"),
            ("decodedPhysicalMm", $"{review.PhysicalWidthMm:0.###}x{review.PhysicalHeightMm:0.###}"),
            ("conclusion", "EXPECTED_SEMANTIC_DISAGREEMENT_NOT_SIZE_CONSISTENCY")));
        if (review.PhysicalWidthMm >= 200 || review.PhysicalHeightMm >= 150)
            throw new InvalidOperationException("F6 no longer exhibits the historical maximum-box mismatch.");
    }

    private static async Task<SessionView> Import(ScenarioServices c, ScenarioEntry e,
        WorkflowType workflow, int width, int height, string name, CancellationToken ct,
        Func<int, int, bool> opaque)
    {
        string path = WritePng(c, e, "inputs", $"{e.Id}-{name}.png", width, height, opaque);
        SessionView view = Must(await c.Sessions.ImportAsync(workflow, path, name, Operator, ct), "import fixture");
        if (view.ProcessingMode != AdapterExecutionMode.Fake || view.RootRevisionId is null)
            throw new InvalidOperationException("Imported session lacks Fake processing or a root revision.");
        await Capture(c, e, view, "import", ct);
        return view;
    }

    private static string WritePng(ScenarioServices c, ScenarioEntry e, string role, string leaf,
        int width, int height, Func<int, int, bool> opaque)
    {
        string path = Path.Combine(c.Root, "fixtures", role, leaf);
        c.ValidateOwnedWrite(path);
        byte[] pixels = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            pixels[i] = 0x20;
            pixels[i + 1] = 0x40;
            pixels[i + 2] = 0x80;
            pixels[i + 3] = opaque(x, y) ? (byte)255 : (byte)0;
        }
        BitmapSource bitmap = BitmapSource.Create(width, height, 300, 300, PixelFormats.Bgra32,
            null, pixels, width * 4);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            encoder.Save(stream);
        c.AdmitFixture(path);
        e.Facts.Add(Fact("immutable synthetic fixture", ("role", role), ("path", path),
            ("pixelWidth", width.ToString()), ("pixelHeight", height.ToString()),
            ("dpi", "300"), ("sha256", Hash(path))));
        return path;
    }

    private static async Task<SessionView> ProduceAndApprove(ScenarioServices c, ScenarioEntry e,
        SessionView view, StepKind step, CancellationToken ct)
    {
        view = await Command(c, e, view, new WorkflowCommand.StartStep(step), "produce " + step, ct);
        if (view.CurrentStep?.State != StepState.ReviewRequired)
            throw new ScenarioGap(step + " did not produce a review target.");
        return await Approve(c, e, view, step, ct);
    }

    private static async Task<SessionView> AuthoriseBackground(ScenarioServices c, ScenarioEntry e,
        SessionView view, CancellationToken ct)
    {
        ArtefactView subject = view.CurrentArtefact ??
            throw new ScenarioGap("No current source to bind background decision.");
        return await Command(c, e, view, new WorkflowCommand.SetBackgroundRemovalDecision(
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            subject.RevisionId, subject.Sha256), "authorise fake background selection", ct);
    }

    private static async Task<SessionView> Approve(ScenarioServices c, ScenarioEntry e, SessionView view,
        StepKind step, CancellationToken ct)
    {
        var current = view.Steps.Single(x => x.Step == step);
        if (current.State != StepState.ReviewRequired || current.CurrentRevisionId is null ||
            current.CurrentRevisionSha256 is null)
            throw new ScenarioGap("No exact review target for " + step);
        SessionView approved = Must(await c.Sessions.ApproveExactReviewAsync(view.Id, step,
            current.CurrentRevisionId.Value, current.CurrentRevisionSha256.Value, Operator, ct),
            "explicit fixture approval " + step);
        await Capture(c, e, approved, "explicit fixture approval " + step, ct);
        return approved;
    }

    private static async Task<SessionView> Command(ScenarioServices c, ScenarioEntry e, SessionView view,
        WorkflowCommand command, string label, CancellationToken ct)
    {
        SessionView next = Must(await c.Sessions.ExecuteAsync(view.Id, command, Operator, ct), label);
        await Capture(c, e, next, label, ct);
        return next;
    }

    private static async Task CheckTiff(ScenarioServices c, ScenarioEntry e, SessionView view,
        int width, int height, CancellationToken ct)
    {
        if (view.CurrentStep?.Step != StepKind.PhotoshopOutput ||
            view.CurrentStep.State != StepState.ReviewRequired || view.Outputs.Count == 0 ||
            view.AttemptPreparation is null)
            throw new ScenarioGap("TIFF output lacks current exact review/preparation.");
        PrintOutputView output = view.Outputs.Last();
        TiffReviewPayload review = Must(await c.TiffReview.GetReviewAsync(view.Id,
            view.CurrentStep.CurrentRevisionId!.Value, ct), "decode TIFF review");
        if (review.PrintOutputId != output.Id || review.PixelWidth != width || review.PixelHeight != height ||
            view.AttemptPreparation.ProjectedPixelWidth != width ||
            view.AttemptPreparation.ProjectedPixelHeight != height ||
            review.Sha256 != view.CurrentStep.CurrentRevisionSha256 ||
            Math.Abs(review.PhysicalWidthMm - PrintDimensions.MillimetresFromPixels(width)) > 0.02 ||
            Math.Abs(review.PhysicalHeightMm - PrintDimensions.MillimetresFromPixels(height)) > 0.02)
            throw new InvalidOperationException("Decoded TIFF, preparation and exact review identity disagree.");
        e.Facts.Add(Fact("decoded production TIFF facts from synthetic bytes",
            ("outputId", output.Id.ToString()), ("revisionId", review.RevisionId.ToString()),
            ("sha256", review.Sha256.ToString()), ("pixels", $"{review.PixelWidth}x{review.PixelHeight}"),
            ("physicalMm", $"{review.PhysicalWidthMm:0.###}x{review.PhysicalHeightMm:0.###}"),
            ("preparationSemantics", view.AttemptPreparation.Semantics.ToString()),
            ("fake", view.AttemptPreparation.IsFakeProjection.ToString())));
    }

    private static async Task Deliver(ScenarioServices c, ScenarioEntry e, SessionView view,
        ArtifactKind kind, Guid id, string leaf, CancellationToken ct)
    {
        ArtifactKey key = new(view.Id, kind, id);
        (DeliveryOffer? offer, ArtifactRefusal refusal) = await c.Delivery.GetOfferAsync(key, ct);
        if (offer is null || refusal != ArtifactRefusal.None)
            throw new ScenarioGap("Approved exact artifact offer unavailable: " + refusal);
        Guid requestId = Guid.NewGuid();
        DeliveryRequest request = new(requestId, key, offer.Artifact.ApprovedSha256,
            offer.Artifact.ReviewId, Path.Combine(c.Root, "delivery"), leaf, offer.OfferVersion);
        DeliveryOutcome outcome = await c.Delivery.DeliverAsync(request, null, ct);
        e.Facts.Add(Fact("real isolated NTFS delivery", ("artifactKind", kind.ToString()),
            ("artifactId", id.ToString()), ("offerSha256", offer.Artifact.ApprovedSha256.ToString()),
            ("reviewId", offer.Artifact.ReviewId.ToString()), ("requestId", requestId.ToString()),
            ("deliveryId", outcome.DeliveryId?.ToString()), ("code", outcome.Code.ToString()),
            ("path", outcome.FinalPath)));
        if (outcome.Code != DeliveryCode.Delivered || outcome.FinalPath is null || outcome.DeliveryId is null)
            throw new InvalidOperationException("Delivery did not publish the exact approved bytes.");
        DeliveryFileObservation observed = await c.Delivery.CheckDeliveredFileAsync(outcome.DeliveryId.Value, ct);
        e.Facts.Add(Fact("real delivery readback", ("deliveryId", observed.DeliveryId.ToString()),
            ("availability", observed.Availability.ToString()), ("path", observed.FinalPath)));
        if (observed.Availability != DeliveryAvailability.VerifiedNow ||
            !string.Equals(observed.FinalPath, outcome.FinalPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The published exact file was not verified by the delivery service.");
        IReadOnlyList<DeliveryState> history = Must(await c.Delivery.GetDeliveryStateAsync(
            view.Id, key, ct), "read delivery history");
        if (!history.Any(x => x.DeliveryId == outcome.DeliveryId))
            throw new InvalidOperationException("Delivered result missing from journal history.");
        e.Facts.Add(Fact("delivery journal", ("deliveryId", outcome.DeliveryId.ToString()),
            ("status", history.Single(x => x.DeliveryId == outcome.DeliveryId).Status)));
        DeliveryOutcome secondNameClaim = await c.Delivery.DeliverAsync(request with
        {
            RequestId = Guid.NewGuid(),
            SelectionVersion = Guid.NewGuid(),
        }, null, ct);
        e.Facts.Add(Fact("no-overwrite second request", ("code", secondNameClaim.Code.ToString()),
            ("path", secondNameClaim.FinalPath)));
        if (secondNameClaim.Code == DeliveryCode.Delivered)
            throw new InvalidOperationException("A second request overwrote the delivered destination.");
    }

    private static async Task Capture(ScenarioServices c, ScenarioEntry e, SessionView view,
        string action, CancellationToken ct)
    {
        SessionAggregate aggregate = Must(await c.Repository.LoadAsync(view.Id, ct),
            "read exact session aggregate") ?? throw new ScenarioGap("Persisted session disappeared.");
        e.Facts.Add(Fact(action,
            ("sessionId", view.Id.ToString()), ("state", view.State.ToString()),
            ("currentStep", view.CurrentStep?.Step.ToString()),
            ("stepState", view.CurrentStep?.State.ToString()),
            ("inputSnapshotId", aggregate.Snapshot?.Id.ToString()),
            ("rootRevisionId", view.RootRevisionId?.ToString()),
            ("rootRevisionSha256", aggregate.Revisions.SingleOrDefault(x => x.IsRoot)?.Sha256.ToString()),
            ("currentRevisionId", view.CurrentStep?.CurrentRevisionId?.ToString()),
            ("currentSha256", view.CurrentStep?.CurrentRevisionSha256?.ToString()),
            ("attempts", string.Join(";", aggregate.Attempts.Select(x =>
                $"{x.Id}|{x.Step}|{x.AdapterId}|{x.Status}|input:{x.InputRevisionId}|output:{x.OutputRevisionId}|failure:{x.Failure?.Code}"))),
            ("revisionIdsAndHashes", string.Join(",", aggregate.Revisions.Select(x =>
                x.Id + ":" + x.Sha256))),
            ("reviews", string.Join(";", aggregate.Reviews.Select(x =>
                $"{x.Id}|{x.Step}|{x.SubjectKind}:{x.SubjectId}|sha:{x.ReviewedSha256}|approved:{x.IsApproved}"))),
            ("outputIdsAndHashes", string.Join(",", aggregate.Outputs.Select(x =>
                x.Id + ":" + x.Sha256))),
            ("correctionRequestIds", string.Join(",", aggregate.CorrectionRequests.Select(x => x.Id.ToString()))),
            ("preparationSemantics", view.AttemptPreparation?.Semantics.ToString()),
            ("projectedPixels", view.AttemptPreparation is null ? null :
                $"{view.AttemptPreparation.ProjectedPixelWidth}x{view.AttemptPreparation.ProjectedPixelHeight}"),
            ("enlargementAuthorised", view.AttemptPreparation?.WasAuthorisedEnlargement.ToString())));
    }

    private static T Must<T>(OperationResult<T> result, string action) => result.IsSuccess
        ? result.Value
        : throw new InvalidOperationException(action + ": " + result.Failure.Code + " " + result.Failure.TechnicalDetail);

    private static ScenarioFact Fact(string action, params (string Key, string? Value)[] values) =>
        new(action, values.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));

    private static string Hash(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void AddCoverage(ScenarioLedger ledger)
    {
        string Evidence(params string[] ids) => string.Join("; ", ids.Select(id =>
            id + "=" + (ledger.Scenarios.SingleOrDefault(x => x.Id == id)?.Status ?? "NOT_RUN")));
        ledger.Coverage.AddRange([
            new("W01", "Parent shell/navigation smoke; scenario runner has no W01 result",
                "Readiness scripted; native/physical input NOT RUN"),
            new("W02", Evidence("F1", "F2D", "F2C", "F3", "F4", "F5", "F6"),
                "Home route selection belongs to parent shell smoke"),
            new("W03", Evidence("F1", "F2D", "F2C", "F3"), "Fresh focus/input NOT RUN"),
            new("W04", Evidence("F1", "F2D", "F2C"),
                "Held/repeat/double-click and physical keyboard NOT RUN"),
            new("W05", Evidence("F1", "F1K"), "UI crop-editor cancel NOT RUN; legacy exact binding OPEN"),
            new("W06", Evidence("F4"), "Real colleague and interrupted bound import NOT RUN"),
            new("W07", Evidence("F2D", "F2C", "F3", "F6"),
                "F6 maximum-box mismatch remains OPEN"),
            new("W08", Evidence("F1", "F2D", "F2C"),
                "Native Explorer/uncertain retry NOT RUN"),
            new("W09", Evidence("F5"), "Notice-to-full-Details route may remain OPEN"),
            new("W10", Evidence("F5", "F3"), "Physical phase stimulus NOT RUN"),
        ]);
    }

    private sealed class ScenarioGap(string message) : Exception(message);
    private sealed class ScenarioFatal(string message) : Exception(message);
}
