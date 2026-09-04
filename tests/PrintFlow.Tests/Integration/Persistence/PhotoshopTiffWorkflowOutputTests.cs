using System.IO;
using System.Text.RegularExpressions;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using Shouldly;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// What a successful Photoshop output attempt is allowed to leave behind
/// (Epic 11400 Part C2A §10–§24).
/// </summary>
/// <remarks>
/// Driven through the Fake adapter on purpose, and that is the point rather than a convenience.
/// §25 requires the success boundary to be adapter-agnostic: <c>SessionService</c> must not have
/// a production-Photoshop-only route to a Revision. Every rule below is therefore stated against
/// the seam both adapters return through, and the controlled workstation smoke proves the real
/// adapter reaches the same seam with a real TIFF.
/// </remarks>
public sealed class PhotoshopTiffWorkflowOutputTests
{
    // -----------------------------------------------------------------------------------
    // §7 — where the TIFF is written
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The TIFF is produced into the attempt's own Working directory, under the name the
    /// workflow's naming authority rendered (§7).
    /// </summary>
    /// <remarks>
    /// Working rather than Approved because C2A ends at ReviewRequired: nothing has been reviewed
    /// yet, and an artefact sitting in <c>Approved\</c> before an operator has seen it would say
    /// otherwise. Promotion is C2B's (§28). The attempt-id folder is what makes a retry's
    /// destination new without any collision handling (§21).
    /// </remarks>
    [Fact]
    public async Task The_TIFF_is_produced_into_the_attempts_own_Working_directory_under_the_rendered_name()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate aggregate = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(aggregate);
        ProcessingAttempt attempt = PhotoshopAttempt(aggregate);

        tiff.File.Area.ShouldBe(WorkspaceArea.Working);
        tiff.File.FileName.ShouldBe("tiff-out_200mm_CMYK_W.tif");
        tiff.File.RelativePath.ShouldContain($"/Working/{attempt.Id.Value:D}/");
        File.Exists(harness.FileWorkspace.ResolveAbsolute(tiff.File)).ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------------
    // §11, §12, §14, §24 — the success transaction
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// One successful attempt leaves exactly one Revision, one PrintOutput, a succeeded attempt
    /// pointing at that Revision, and a step in ReviewRequired (§11, §12).
    /// </summary>
    /// <remarks>
    /// Asserted together rather than as four tests because the property is their consistency:
    /// an AttemptSucceeded with no Revision, or a ReviewRequired pointing at nothing, is exactly
    /// what §11 exists to prevent, and each of those states passes three of four separate
    /// assertions.
    /// </remarks>
    [Fact]
    public async Task One_successful_attempt_creates_exactly_one_Revision_and_reaches_ReviewRequired()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate aggregate = await LoadAsync(harness, id);

        // Exactly one Revision for the whole Photoshop step: no intermediate revision for the
        // size preparation, none for the CMYK/W1 result, none before validation. The TIFF is the
        // artefact, and the in-memory stages produce nothing that could be reviewed (§12, §14).
        aggregate.Revisions.Count(r => r.Operation == OperationKind.PhotoshopOutput).ShouldBe(1);
        aggregate.Attempts.Count(a => a.Step == StepKind.PhotoshopOutput).ShouldBe(1);
        aggregate.Outputs.Count.ShouldBe(1);

        Revision tiff = TiffRevision(aggregate);
        ProcessingAttempt attempt = PhotoshopAttempt(aggregate);
        SessionStep step = aggregate.Steps.Single(s => s.Step == StepKind.PhotoshopOutput);

        attempt.Status.ShouldBe(AttemptStatus.Succeeded);
        attempt.OutputRevisionId.ShouldBe(tiff.Id);
        step.State.ShouldBe(StepState.ReviewRequired);
        step.CurrentRevisionId.ShouldBe(tiff.Id);
        step.CurrentRevisionSha256.ShouldBe(tiff.Facts.Sha256);
        aggregate.Outputs.Single().Sha256.ShouldBe(tiff.Facts.Sha256);
    }

    /// <summary>
    /// The output Revision descends from the exact upstream Revision the attempt consumed, not
    /// from an intermediate working copy or an earlier output (§14).
    /// </summary>
    [Fact]
    public async Task The_output_Revision_descends_from_the_Revision_the_attempt_consumed()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate aggregate = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(aggregate);
        ProcessingAttempt attempt = PhotoshopAttempt(aggregate);

        tiff.SourceRevisionId.ShouldBe(attempt.InputRevisionId);
        tiff.SourceRevisionId.ShouldNotBe(tiff.Id);
        aggregate.Outputs.Single().SourceRevisionId.ShouldBe(attempt.InputRevisionId!.Value);

        // The source Revision is untouched: still present, still valid, still its own bytes.
        Revision source = aggregate.Revisions.Single(r => r.Id == attempt.InputRevisionId!.Value);
        source.InvalidatedAtUtc.ShouldBeNull();
        File.Exists(harness.FileWorkspace.ResolveAbsolute(source.File)).ShouldBeTrue();
    }

    /// <summary>
    /// The recorded facts describe the produced TIFF, and the TIFF-specific facts the Revision
    /// cannot express are kept on the producing attempt instead (§13, §16).
    /// </summary>
    [Fact]
    public async Task The_Revision_records_the_TIFF_facts_and_the_attempt_keeps_the_adapter_note()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate aggregate = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(aggregate);
        string absolute = harness.FileWorkspace.ResolveAbsolute(tiff.File);

        // The Revision's hash is the hash of the bytes on disk, re-read here by a path that
        // shares nothing with the one that recorded it.
        FileFacts onDisk = (await new WicFileInspector()
            .InspectAsync(absolute, CancellationToken.None)).Value;
        tiff.Facts.Sha256.ShouldBe(onDisk.Sha256);
        tiff.Facts.ByteLength.ShouldBe(onDisk.ByteLength);
        tiff.Facts.ByteLength.ShouldBe(new FileInfo(absolute).Length);

        // W1, compression and the sample layout have no home in FileFacts, so they are on the
        // attempt rather than deforming the Revision into carrying them.
        PhotoshopAttempt(aggregate).AdapterNotes.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The succeeded attempt still describes what it was asked to do, and a later change of mind
    /// on the session cannot relabel it (§15).
    /// </summary>
    /// <remarks>
    /// The preparation snapshot on the attempt row is the audit authority for the sizing
    /// semantics this output was produced under. It is written by the opening transaction and
    /// left out of the attempt upsert's update clause, so the success transaction cannot
    /// overwrite it — and the size the operator picks <i>afterwards</i> describes the next run,
    /// never this one.
    /// </remarks>
    [Fact]
    public async Task The_producing_attempt_keeps_its_own_preparation_after_the_session_moves_on()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        AttemptId producing = PhotoshopAttempt(before).Id;
        PhotoshopPreparation produced = PhotoshopAttempt(before).Preparation.ShouldNotBeNull();
        produced.ProductionDpi.ShouldBe(300);

        // The operator goes back and chooses a different size after the output exists.
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.ReturnToStep(StepKind.PrintDimensions), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SetPrintDimensions(
                PrintDimensions.FromMillimetres(120, 90, SizePreset.Custom)),
            "tester",
            CancellationToken.None));

        SessionAggregate after = await LoadAsync(harness, id);
        after.Session.PrintPreparationPlan.ShouldNotBe(produced as object,
            "the session now describes the next run");

        // The attempt that produced the existing TIFF still says what it actually did.
        after.Attempts.Single(a => a.Id == producing).Preparation.ShouldBe(produced);
    }

    /// <summary>
    /// The success boundary is keyed on the step, never on which adapter ran it (§25).
    /// </summary>
    /// <remarks>
    /// Stated as the absence of an adapter branch rather than as a behavioural assertion, because
    /// behaviour cannot show the difference: a production-only Revision path would look identical
    /// from a Fake run. What has to hold is that no such path exists to be taken, so the Fake and
    /// the production adapter reach one boundary rather than two that happen to agree today.
    /// <para>
    /// Scoped to the success transaction itself, because <c>SessionService</c> legitimately reads
    /// an adapter's mode elsewhere — the environment gate asks which mode a step is backed by, and
    /// the read model reports whether this installation's output is synthetic. Neither is a branch
    /// in the path that creates a Revision, and banning the words outright would have made those
    /// two honest uses fail.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_success_transaction_branches_on_no_adapter_identity()
    {
        string source = File.ReadAllText(Path.Combine(
            WorkflowProjectDirectory(), "Services", "SessionService.cs"));

        int start = source.IndexOf(
            "private async Task<OperationResult<SessionView>> CompleteProducingStepAsync",
            StringComparison.Ordinal);
        start.ShouldBeGreaterThan(-1, "the success transaction should still be one named method");

        int end = source.IndexOf(
            "private async Task<OperationResult<StepWork>> PerformStepWorkAsync",
            start,
            StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);

        string successTransaction = source[start..end];
        foreach (string banned in new[]
        {
            "AdapterExecutionMode",
            "AdapterKind.Photoshop",
            "photoshop-cc2019-production-v1",
            "fake-photoshop-v1",
            ".Mode",
            "AdapterId",
        })
        {
            successTransaction.ShouldNotContain(banned, Case.Sensitive);
        }

        // And there is exactly one place a Revision is created for a producing step.
        Regex.Matches(source, @"Revision\.Create\(").Count.ShouldBe(2,
            "one for the import root Revision, one for every producing step");
    }

    // -----------------------------------------------------------------------------------
    // §17, §18 — failure creates nothing reviewable
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// An adapter failure leaves a failed attempt, no Revision, and a step that is not
    /// reviewable (§17).
    /// </summary>
    [Theory]
    [InlineData(FakeAdapterScenarioKind.FailWith)]
    [InlineData(FakeAdapterScenarioKind.ProduceMissingFile)]
    [InlineData(FakeAdapterScenarioKind.ProduceUnreadableFile)]
    public async Task A_failed_or_invalid_output_creates_no_Revision_and_no_ReviewRequired(
        FakeAdapterScenarioKind kind)
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(harness, service);

        harness.FakePhotoshop.SetScenario(kind == FakeAdapterScenarioKind.FailWith
            ? FakeAdapterScenario.FailWith(FailureCode.OutputValidationFailed)
            : new FakeAdapterScenario(kind));

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);

        started.IsFailure.ShouldBeTrue();

        SessionAggregate aggregate = await LoadAsync(harness, id);
        aggregate.Revisions.ShouldNotContain(r => r.Operation == OperationKind.PhotoshopOutput);
        aggregate.Outputs.ShouldBeEmpty();

        ProcessingAttempt attempt = PhotoshopAttempt(aggregate);
        attempt.Status.ShouldBe(AttemptStatus.Failed);
        attempt.OutputRevisionId.ShouldBeNull();

        SessionStep step = aggregate.Steps.Single(s => s.Step == StepKind.PhotoshopOutput);
        step.State.ShouldBe(StepState.Failed);
        step.CurrentRevisionId.ShouldBeNull();
    }

    // -----------------------------------------------------------------------------------
    // §19, §20 — cancellation on either side of the success boundary
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Cancellation before the success commit ends the attempt as Cancelled and creates no
    /// Revision, so a stopped run cannot become a reviewable result (§19).
    /// </summary>
    /// <remarks>
    /// The run is cancelled from another task while it is genuinely in flight, because a token
    /// cancelled before the call would test a different and easier thing: the property is that a
    /// run already underway cannot finish into a success.
    /// </remarks>
    [Fact]
    public async Task Cancellation_before_the_success_commit_creates_no_Revision()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(harness, service);

        harness.FakePhotoshop.SetScenario(FakeAdapterScenario.HangUntilCancelled);
        using CancellationTokenSource cancellation = new();

        Task<OperationResult<SessionView>> run = service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", cancellation.Token);

        await harness.FakePhotoshop.HangStarted;
        await cancellation.CancelAsync();

        (await run).IsFailure.ShouldBeTrue("a cancelled run produces no result");

        SessionAggregate aggregate = await LoadAsync(harness, id);
        aggregate.Revisions.ShouldNotContain(r => r.Operation == OperationKind.PhotoshopOutput);
        aggregate.Outputs.ShouldBeEmpty();
        PhotoshopAttempt(aggregate).OutputRevisionId.ShouldBeNull();
        aggregate.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State
            .ShouldNotBe(StepState.ReviewRequired);
    }

    /// <summary>
    /// A Stop requested after the success transaction has committed cannot erase it (§20).
    /// </summary>
    /// <remarks>
    /// The same D2A rule the Meitu steps already follow, asserted for Photoshop rather than
    /// restated as new Photoshop-specific semantics — inventing a second answer for the same
    /// question is exactly what §20 warns against. The run is unregistered before
    /// <c>ExecuteAsync</c> returns, so the late Stop is refused: there is no window in which it
    /// could reach a committed attempt, and that refusal <i>is</i> the boundary.
    /// </remarks>
    [Fact]
    public async Task A_stop_requested_after_the_TIFF_success_cannot_erase_it()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        OperationResult<PrintFlow.Domain.Results.Unit> late =
            service.RequestStop(id, AutomationStopMode.StopOperation);

        late.IsFailure.ShouldBeTrue("a run that has finished is no longer stoppable");

        SessionAggregate aggregate = await LoadAsync(harness, id);
        PhotoshopAttempt(aggregate).Status.ShouldBe(AttemptStatus.Succeeded);
        PhotoshopAttempt(aggregate).OutputRevisionId.ShouldBe(TiffRevision(aggregate).Id);
        aggregate.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State
            .ShouldBe(StepState.ReviewRequired);

        SessionView reloaded = (await service.LoadAsync(id, CancellationToken.None)).Value;
        reloaded.LastAutomationStop.ShouldBeNull("nothing about this attempt was stopped");
    }

    // -----------------------------------------------------------------------------------
    // §21 — retry
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A retry after a failed attempt runs as a new attempt, in a new Working directory, against
    /// a new output path, and leaves the failed attempt's artefacts where they are (§21).
    /// </summary>
    [Fact]
    public async Task Retry_uses_a_new_attempt_a_new_Working_directory_and_a_new_output_path()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(harness, service);

        harness.FakePhotoshop.SetScenario(FakeAdapterScenario.FailWith(FailureCode.OutputValidationFailed));
        (await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None))
            .IsFailure.ShouldBeTrue();

        AttemptId first = PhotoshopAttempt(await LoadAsync(harness, id)).Id;

        harness.FakePhotoshop.SetScenario(FakeAdapterScenario.Succeed);
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Retry(StepKind.PhotoshopOutput), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));

        SessionAggregate aggregate = await LoadAsync(harness, id);
        ProcessingAttempt retry = aggregate.Attempts
            .Where(a => a.Step == StepKind.PhotoshopOutput)
            .Single(a => a.Id != first);

        retry.RetryOfAttemptId.ShouldBe(first);
        retry.Status.ShouldBe(AttemptStatus.Succeeded);

        Revision tiff = TiffRevision(aggregate);
        tiff.File.RelativePath.ShouldContain($"/Working/{retry.Id.Value:D}/");
        tiff.File.RelativePath.ShouldNotContain($"/Working/{first.Value:D}/");

        // Still exactly one Revision: the failed attempt contributed none (§12).
        aggregate.Revisions.Count(r => r.Operation == OperationKind.PhotoshopOutput).ShouldBe(1);
    }

    // -----------------------------------------------------------------------------------
    // §22, §23 — restart
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A crash before the success transaction leaves an Interrupted attempt and no Revision, even
    /// when a complete TIFF is sitting in the attempt's Working directory (§22).
    /// </summary>
    /// <remarks>
    /// The file is placed by hand at exactly the path a successful run would have produced,
    /// because that is the situation the rule is about: recovery must not reason "there is a
    /// valid-looking TIFF on disk, so the run must have worked". It quarantines the orphan rather
    /// than adopting it, and the step stays without a result.
    /// </remarks>
    [Fact]
    public async Task Restart_before_the_success_commit_interrupts_the_attempt_and_adopts_no_TIFF()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(harness, service);

        // A Running attempt with a finished-looking TIFF beside its working copy — the exact
        // state a crash between the write and the commit leaves behind.
        SessionAggregate aggregate = await LoadAsync(harness, id);
        AttemptId attemptId = AttemptId.From(Guid.NewGuid());
        Revision upstream = aggregate.Revisions[^1];
        WorkspaceFileRef working = (await harness.FileWorkspace.CreateWorkingCopyAsync(
            aggregate.Session.Workspace, attemptId, upstream.File, CancellationToken.None)).Value;

        string orphan = Path.Combine(
            Path.GetDirectoryName(harness.FileWorkspace.ResolveAbsolute(working))!,
            "tiff-out_200mm_CMYK_W.tif");
        ProductionTiffFixture.WriteAt(orphan);
        File.Exists(orphan).ShouldBeTrue();

        await CommitRunningAttemptAsync(harness, aggregate, attemptId, upstream.Id);

        IStartupRecoveryService recovery = harness.CreateRecoveryService(new FakeProcessLiveness());
        (await recovery.RecoverAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();

        SessionAggregate after = await LoadAsync(harness, id);
        after.Attempts.Single(a => a.Id == attemptId).Status.ShouldBe(AttemptStatus.Interrupted);
        after.Revisions.ShouldNotContain(r => r.Operation == OperationKind.PhotoshopOutput);
        after.Outputs.ShouldBeEmpty();
        after.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).CurrentRevisionId.ShouldBeNull();

        // The orphan was moved out of the way, never adopted and never deleted.
        File.Exists(orphan).ShouldBeFalse();
    }

    /// <summary>
    /// A restart after the success transaction re-reads the same artefact: no duplicate Revision,
    /// no second Photoshop run, the same ReviewRequired step (§23).
    /// </summary>
    [Fact]
    public async Task Restart_after_the_success_commit_duplicates_nothing_and_reruns_nothing()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(before);

        // A different process would find nothing to recover; run recovery anyway, because the
        // rule is that it changes nothing here rather than that it is never invoked.
        IStartupRecoveryService recovery = harness.CreateRecoveryService(new FakeProcessLiveness());
        (await recovery.RecoverAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();

        ISessionService restarted = harness.CreateService();
        SessionView reloaded = (await restarted.LoadAsync(id, CancellationToken.None)).Value;
        SessionAggregate after = await LoadAsync(harness, id);

        after.Revisions.Count(r => r.Operation == OperationKind.PhotoshopOutput).ShouldBe(1);
        after.Outputs.Count.ShouldBe(1);
        TiffRevision(after).Id.ShouldBe(tiff.Id);
        TiffRevision(after).Facts.Sha256.ShouldBe(tiff.Facts.Sha256);
        PhotoshopAttempt(after).Status.ShouldBe(AttemptStatus.Succeeded);

        reloaded.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State
            .ShouldBe(StepState.ReviewRequired);
        File.Exists(harness.FileWorkspace.ResolveAbsolute(tiff.File)).ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------------
    // §24 — the read model
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The artefact on screen at ReviewRequired is the TIFF Revision, and it is the current
    /// step's own result rather than the file the step consumed (§24).
    /// </summary>
    [Fact]
    public async Task The_current_artefact_at_ReviewRequired_is_the_TIFF_Revision()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionView view = (await service.LoadAsync(id, CancellationToken.None)).Value;
        SessionAggregate aggregate = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(aggregate);

        ArtefactView artefact = view.CurrentArtefact.ShouldNotBeNull();
        artefact.RevisionId.ShouldBe(tiff.Id);
        artefact.FileName.ShouldBe("tiff-out_200mm_CMYK_W.tif");
        artefact.IsCurrentStepResult.ShouldBeTrue();
        artefact.Sha256.ShouldBe(tiff.Facts.Sha256);
        artefact.SourceRevisionId.ShouldBe(tiff.SourceRevisionId);

        view.CurrentStep.ShouldNotBeNull().Step.ShouldBe(StepKind.PhotoshopOutput);
        view.Outputs.Count.ShouldBe(1);
        view.Outputs.Single().ReviewState.ShouldBe(ReviewState.NotReviewed);
    }

    // -----------------------------------------------------------------------------------
    // §27, §28 — what C2A still refuses to do
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The environment gate still refuses a Production Photoshop adapter, so reaching
    /// ReviewRequired through the controlled seam changes nothing about normal operation (§27).
    /// </summary>
    [Fact]
    public void The_environment_gate_still_refuses_a_production_Photoshop_adapter()
    {
        using SessionServiceHarness harness = new();

        OperationResult<PrintFlow.Domain.Results.Unit> production = harness.EnvironmentGate.Verify(AdapterExecutionMode.Production);

        production.IsFailure.ShouldBeTrue();
        production.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
        harness.EnvironmentGate.Verify(AdapterExecutionMode.Fake).IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// C2A stops at ReviewRequired: nothing about it approves, rejects or completes the session
    /// on the operator's behalf (§28).
    /// </summary>
    [Fact]
    public async Task Reaching_ReviewRequired_approves_rejects_and_completes_nothing()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate aggregate = await LoadAsync(harness, id);

        aggregate.Session.State.ShouldNotBe(SessionState.Completed);
        aggregate.Outputs.Single().ReviewState.ShouldBe(ReviewState.NotReviewed);
        aggregate.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State
            .ShouldBe(StepState.ReviewRequired);

        // No review decision was recorded by the run itself; the operator has yet to act.
        aggregate.Reviews.ShouldNotContain(r => r.Step == StepKind.PhotoshopOutput);

        // And the TIFF has not been promoted into Approved or moved to Rejected.
        TiffRevision(aggregate).File.Area.ShouldBe(WorkspaceArea.Working);
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    /// <summary>Imports a GeneratePrintTiff session and leaves PhotoshopOutput ready to start.</summary>
    private static async Task<SessionId> ReadyForPhotoshopAsync(
        SessionServiceHarness harness, ISessionService service)
    {
        SessionId id = (await service.ImportAsync(
            WorkflowType.GeneratePrintTiff,
            harness.WriteBorderedSourcePng(),
            "tiff-out",
            "tester",
            CancellationToken.None)).Value.Id;

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal("finished design"), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SetPrintDimensions(
                PrintDimensions.FromMillimetres(200, 150, SizePreset.Custom)),
            "tester",
            CancellationToken.None));
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "ordinary design"),
            "tester",
            CancellationToken.None));

        return id;
    }

    private static async Task<SessionId> ReachReviewRequiredAsync(
        SessionServiceHarness harness, ISessionService service)
    {
        SessionId id = await ReadyForPhotoshopAsync(harness, service);
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));
        return id;
    }

    /// <summary>Writes a Running Photoshop attempt row, as the opening transaction would have.</summary>
    private static async Task CommitRunningAttemptAsync(
        SessionServiceHarness harness, SessionAggregate aggregate, AttemptId attemptId, RevisionId input)
    {
        ProcessingAttempt running = ProcessingAttempt.Start(
            attemptId, aggregate.Session.Id, StepKind.PhotoshopOutput, input,
            OperationKind.PhotoshopOutput, "fake-photoshop-v1", harness.Clock.GetUtcNow());

        SessionMutation mutation = new(
            aggregate.Session, aggregate.Steps, [], [], [running], [], [], null, null);
        (await harness.Repository.CommitAsync(mutation, CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    private static Revision TiffRevision(SessionAggregate aggregate) =>
        aggregate.Revisions.Single(r => r.Operation == OperationKind.PhotoshopOutput);

    private static ProcessingAttempt PhotoshopAttempt(SessionAggregate aggregate) =>
        aggregate.Attempts.Last(a => a.Step == StepKind.PhotoshopOutput);

    private static string WorkflowProjectDirectory()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        current.ShouldNotBeNull();
        return Path.Combine(current.FullName, "src", "PrintFlow.Workflow");
    }

    private static async Task<SessionAggregate> LoadAsync(SessionServiceHarness harness, SessionId id) =>
        (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;

    private static async Task Must(Task<OperationResult<SessionView>> resultTask)
    {
        OperationResult<SessionView> result = await resultTask;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }
}
