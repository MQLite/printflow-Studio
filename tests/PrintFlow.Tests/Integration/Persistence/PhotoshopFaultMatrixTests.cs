using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using Shouldly;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// The complete SCRUM-11129 Photoshop outcome matrix, driven end to end through
/// <see cref="SessionService"/> against real files and a real database.
/// </summary>
/// <remarks>
/// SCRUM-11129 names nine outcomes — successful TIFF, missing white channel, wrong colour mode,
/// wrong physical or pixel dimensions, incorrect DPI, invalid/unreadable output, timeout,
/// interruption and unknown dialog — and adds two properties that hold across all of them:
/// invalid outputs cannot enter final approval, and every retry starts from a clean approved
/// upstream source. Each is covered here exactly once.
/// <para>
/// Every stimulus is applied to the Fake Photoshop adapter through the ordinary workflow, and
/// every conclusion is read from the persisted session. Nothing consults the adapter for a
/// verdict and nothing reaches around <c>SessionService</c>, so the assertions are about what
/// PrintFlow records rather than about what a double was told to say. The structural cases in
/// particular are produced as real TIFF bytes with one fact deliberately wrong — see
/// <see cref="Automation.FakePhotoshopStructuralFaultTests"/> for the proof that those files are
/// genuinely what they claim to be.
/// </para>
/// <para>
/// No Photoshop, Meitu or Maintop process is started, and no click order is asserted anywhere:
/// SCRUM-11097 rules that out, and the fake has no application in which anything could be
/// clicked.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class PhotoshopFaultMatrixTests
{
    // -----------------------------------------------------------------------------------
    // The matrix: every invalid outcome
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Every invalid Photoshop outcome ends the attempt as a failure that creates no Revision,
    /// no <c>PrintOutput</c> and no reviewable step — and final approval stays unreachable.
    /// </summary>
    /// <remarks>
    /// One parameterised test rather than twelve, because the property being asserted is
    /// identical for all of them and it is the <i>uniformity</i> that matters: a structural fault
    /// must not be handled more leniently than an export failure merely because a file exists on
    /// disk at the end of it. Twelve copies of this session setup would also be twelve chances for
    /// one of them to drift into asserting something slightly weaker.
    /// <para>
    /// The approval attempt is made here rather than in a separate test for the same reason. "The
    /// invalid output cannot enter final approval" is a claim about each of these outcomes, not a
    /// thirteenth outcome, and proving it once for one representative fault would leave the other
    /// eleven asserted only as far as the attempt row.
    /// </para>
    /// </remarks>
    [Theory]
    // -- structural output faults: a real TIFF is written, and the Product refuses it -----
    [InlineData("missing white channel", FakePhotoshopTiffOutput.MissingWhiteChannel)]
    [InlineData("empty white channel", FakePhotoshopTiffOutput.EmptyWhiteChannel)]
    [InlineData("wrong colour mode", FakePhotoshopTiffOutput.WrongColourMode)]
    [InlineData("wrong pixel dimensions (width)", FakePhotoshopTiffOutput.WrongPixelWidth)]
    [InlineData("wrong pixel dimensions (height)", FakePhotoshopTiffOutput.WrongPixelHeight)]
    [InlineData("incorrect DPI", FakePhotoshopTiffOutput.IncorrectDpi)]
    [InlineData("incorrect output metadata (compression)", FakePhotoshopTiffOutput.IncorrectMetadata)]
    [InlineData("incorrect output metadata (channel)", FakePhotoshopTiffOutput.IncorrectChannelMetadata)]
    public async Task A_structural_output_fault_is_refused_and_can_never_be_approved(
        string acCase, FakePhotoshopTiffOutput output)
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(harness, service);

        harness.FakePhotoshop.SetTiffOutput(output);

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);

        started.IsFailure.ShouldBeTrue($"{acCase} was accepted by the workflow.");
        started.Failure.Code.ShouldBe(FailureCode.OutputValidationFailed);

        // A real file really was produced and really was refused. Without this the case would be
        // indistinguishable from an export that never wrote anything.
        string producedPath = started.Failure.Context[FailureEvidence.ExpectedOutputPathKey];
        File.Exists(producedPath).ShouldBeTrue(acCase);

        await AssertNothingReviewableAsync(harness, service, id, AttemptStatus.Failed, acCase);

        // The strongest form of "cannot enter final approval": the refused TIFF is still on disk,
        // so its own hash is a value a caller could genuinely present. Approving a failed step is
        // refused by the state machine whatever hash is offered, and this proves the refusal
        // covers the invalid artefact itself rather than only the upstream stand-in.
        OperationResult<SessionView> approval = await service.ExecuteAsync(
            id,
            new WorkflowCommand.Approve(StepKind.PhotoshopOutput, Hash(producedPath)),
            "tester",
            CancellationToken.None);

        approval.IsFailure.ShouldBeTrue($"{acCase}: the refused TIFF's own hash was approved.");
        (await LoadAsync(harness, id)).Outputs.ShouldBeEmpty(acCase);
    }

    /// <summary>
    /// The behavioural outcomes — export failure, missing output, unreadable output, timeout and
    /// unknown dialog — reach the same closed end state.
    /// </summary>
    /// <remarks>
    /// Separated from the structural theory only because these stimuli are scripted on the other
    /// half of the fake's vocabulary and produce no output class at all. The assertions are the
    /// same ones, deliberately: SCRUM-11129 asks for one matrix, not two standards.
    /// </remarks>
    /// <remarks>
    /// <paramref name="scriptedCode"/> is the code a <c>FailWith</c> row scripts and is null for
    /// every other kind, so the scenario is built from <paramref name="kind"/> alone. An earlier
    /// draft used the code's presence as the discriminator, which silently routed the timeout row
    /// down the generic scripted-failure path and left
    /// <see cref="FakeAdapterScenarioKind.Timeout"/> untested — the test still passed, because
    /// both paths return <see cref="FailureCode.Timeout"/>.
    /// </remarks>
    [Theory]
    [InlineData("export failure", FakeAdapterScenarioKind.FailWith,
        FailureCode.OutputValidationFailed, FailureCode.OutputValidationFailed, false)]
    [InlineData("unknown dialog", FakeAdapterScenarioKind.FailWith,
        FailureCode.PhotoshopUnknownState, FailureCode.PhotoshopUnknownState, false)]
    [InlineData("blocking dialog", FakeAdapterScenarioKind.FailWith,
        FailureCode.PhotoshopBlockingDialog, FailureCode.PhotoshopBlockingDialog, false)]
    [InlineData("timeout", FakeAdapterScenarioKind.Timeout, null, FailureCode.Timeout, false)]
    [InlineData("invalid/unreadable output", FakeAdapterScenarioKind.ProduceUnreadableFile,
        null, FailureCode.OutputUnreadable, true)]
    [InlineData("missing output", FakeAdapterScenarioKind.ProduceMissingFile,
        null, FailureCode.OutputMissing, false)]
    public async Task A_behavioural_failure_is_refused_and_can_never_be_approved(
        string acCase, FakeAdapterScenarioKind kind, FailureCode? scriptedCode,
        FailureCode expectedCode, bool fileExpected)
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(harness, service);

        // Set an output class that would otherwise succeed, so a scenario that leaked into the
        // file-writing path would be visible as an accepted run rather than hidden by a default
        // that fails anyway.
        harness.FakePhotoshop.SetTiffOutput(FakePhotoshopTiffOutput.ValidTiff);
        harness.FakePhotoshop.SetScenario(kind == FakeAdapterScenarioKind.FailWith
            ? FakeAdapterScenario.FailWith(scriptedCode!.Value)
            : new FakeAdapterScenario(kind));

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);

        started.IsFailure.ShouldBeTrue($"{acCase} was accepted by the workflow.");
        started.Failure.Code.ShouldBe(expectedCode, acCase);

        string producedPath = started.Failure.Context[FailureEvidence.ExpectedOutputPathKey];
        File.Exists(producedPath).ShouldBe(fileExpected, acCase);

        await AssertNothingReviewableAsync(harness, service, id, AttemptStatus.Failed, acCase);
    }

    /// <summary>
    /// An interruption in flight ends the attempt without a Revision, a <c>PrintOutput</c> or a
    /// reachable approval.
    /// </summary>
    /// <remarks>
    /// Its own test because it is the one outcome that cannot be scripted synchronously: the run
    /// has to be genuinely underway before it is interrupted, or the test would be proving that a
    /// pre-cancelled token is refused, which is a different and much easier property.
    /// </remarks>
    [Fact]
    public async Task An_interrupted_run_is_refused_and_can_never_be_approved()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(harness, service);

        harness.FakePhotoshop.SetTiffOutput(FakePhotoshopTiffOutput.ValidTiff);
        harness.FakePhotoshop.SetScenario(FakeAdapterScenario.HangUntilCancelled);
        using CancellationTokenSource cancellation = new();

        Task<OperationResult<SessionView>> run = service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", cancellation.Token);

        await harness.FakePhotoshop.HangStarted;
        await cancellation.CancelAsync();

        (await run).IsFailure.ShouldBeTrue("an interrupted run produces no result");

        SessionAggregate aggregate = await LoadAsync(harness, id);
        aggregate.Revisions.ShouldNotContain(r => r.Operation == OperationKind.PhotoshopOutput);
        aggregate.Outputs.ShouldBeEmpty();
        PhotoshopAttempt(aggregate).Status.ShouldNotBe(AttemptStatus.Succeeded);
        PhotoshopAttempt(aggregate).OutputRevisionId.ShouldBeNull();
        aggregate.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State
            .ShouldNotBe(StepState.ReviewRequired);
    }

    // -----------------------------------------------------------------------------------
    // The matrix: the successful outcome
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A valid production TIFF from the fake reaches ReviewRequired, and approval binds a
    /// <c>PrintOutput</c> to that exact validated hash.
    /// </summary>
    /// <remarks>
    /// The control the whole matrix rests on. Eleven refusals prove nothing unless the accepted
    /// case is genuinely accepted through the same route, and this is also the regression that
    /// stops the new structural vocabulary from quietly making every run fail.
    /// </remarks>
    [Fact]
    public async Task A_valid_fake_TIFF_reaches_review_and_binds_final_approval_to_its_own_hash()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(harness, service);

        harness.FakePhotoshop.SetTiffOutput(FakePhotoshopTiffOutput.ValidTiff);

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Failure.ToString() : string.Empty);

        SessionAggregate produced = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(produced);
        PhotoshopAttempt(produced).Status.ShouldBe(AttemptStatus.Succeeded);
        produced.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State
            .ShouldBe(StepState.ReviewRequired);

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.PhotoshopOutput, tiff.Sha256), "tester",
            CancellationToken.None));

        SessionAggregate approved = await LoadAsync(harness, id);
        PrintOutput output = approved.Outputs.Single();
        output.Sha256.ShouldBe(tiff.Sha256);
        output.File.Area.ShouldBe(WorkspaceArea.Approved);
    }

    // -----------------------------------------------------------------------------------
    // Retry starts from a clean approved upstream source
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// After a structural output failure, the retry works from the approved upstream Revision —
    /// not from the failed TIFF and not from the failed attempt's working copy — and its success
    /// leaves the failed attempt intact as history.
    /// </summary>
    /// <remarks>
    /// Asserted against the wrong-dimension fault specifically, because that is the one where a
    /// retry reading the previous output would still be handed a structurally perfect TIFF: every
    /// absolute check would pass, and only the comparison against the preparation would catch it.
    /// A retry that silently inherited a bad canvas is therefore exactly the defect this case can
    /// detect and the other faults cannot.
    /// <para>
    /// Ancestry is proven from the persisted rows rather than from file contents alone: the
    /// second attempt's input Revision is the same approved upstream Revision the first consumed,
    /// its output lives under its own attempt id, and the upstream bytes are unchanged by either
    /// run. Nothing here adds a second retry mechanism — it is the ordinary
    /// <see cref="WorkflowCommand.Retry"/> path.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_retry_after_a_structural_failure_starts_from_the_clean_approved_upstream_Revision()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(harness, service);

        // The approved upstream source, read before anything runs, so the "unchanged" assertion
        // below is against bytes observed before either attempt rather than after the first.
        SessionAggregate before = await LoadAsync(harness, id);
        Revision upstream = before.Revisions.Last(r => r.IsValid);
        string upstreamPath = harness.FileWorkspace.ResolveAbsolute(upstream.File);
        Sha256 upstreamBytesBefore = Hash(upstreamPath);

        // Attempt 1: a structurally perfect TIFF at the wrong canvas.
        harness.FakePhotoshop.SetTiffOutput(FakePhotoshopTiffOutput.WrongPixelWidth);
        OperationResult<SessionView> failed = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);
        failed.IsFailure.ShouldBeTrue();
        string failedTiffPath = failed.Failure.Context[FailureEvidence.ExpectedOutputPathKey];
        File.Exists(failedTiffPath).ShouldBeTrue();

        SessionAggregate afterFailure = await LoadAsync(harness, id);
        ProcessingAttempt firstAttempt = PhotoshopAttempt(afterFailure);
        firstAttempt.Status.ShouldBe(AttemptStatus.Failed);

        // Attempt 2, through the ordinary retry path.
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Retry(StepKind.PhotoshopOutput), "tester", CancellationToken.None));
        harness.FakePhotoshop.SetTiffOutput(FakePhotoshopTiffOutput.ValidTiff);
        OperationResult<SessionView> retried = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);
        retried.IsSuccess.ShouldBeTrue(retried.IsFailure ? retried.Failure.ToString() : string.Empty);

        SessionAggregate afterRetry = await LoadAsync(harness, id);
        ProcessingAttempt secondAttempt = PhotoshopAttempt(afterRetry);
        secondAttempt.Id.ShouldNotBe(firstAttempt.Id);
        secondAttempt.Status.ShouldBe(AttemptStatus.Succeeded);

        // Same clean upstream source, unchanged on disk by either attempt.
        secondAttempt.InputRevisionId.ShouldBe(firstAttempt.InputRevisionId);
        secondAttempt.InputRevisionId.ShouldBe(upstream.Id);
        Hash(upstreamPath).ShouldBe(upstreamBytesBefore);

        // The retry's working copy and output belong to the retry, not to the failed attempt.
        Revision output = TiffRevision(afterRetry);
        output.File.RelativePath.ShouldContain(secondAttempt.Id.Value.ToString("D"));
        output.File.RelativePath.ShouldNotContain(firstAttempt.Id.Value.ToString("D"));
        output.SourceRevisionId.ShouldBe(upstream.Id);

        // The failed TIFF is not the production output and did not become one.
        Sha256 failedTiffHash = Hash(failedTiffPath);
        output.Sha256.ShouldNotBe(failedTiffHash);
        harness.FileWorkspace.ResolveAbsolute(output.File)
            .Equals(failedTiffPath, StringComparison.OrdinalIgnoreCase)
            .ShouldBeFalse("the retry must not reuse the failed attempt's output path");

        // History is preserved: the failed attempt is still there, still failed, still without a
        // Revision, and its diagnostic evidence is intact.
        List<ProcessingAttempt> attempts =
            [.. afterRetry.Attempts.Where(a => a.Step == StepKind.PhotoshopOutput)];
        attempts.Count.ShouldBe(2);
        ProcessingAttempt persistedFailure = attempts.Single(a => a.Id == firstAttempt.Id);
        persistedFailure.Status.ShouldBe(AttemptStatus.Failed);
        persistedFailure.OutputRevisionId.ShouldBeNull();
        persistedFailure.Failure.ShouldNotBeNull().Code.ShouldBe(FailureCode.OutputValidationFailed);
        persistedFailure.Failure!.Context[FailureEvidence.ExpectedOutputPathKey].ShouldBe(failedTiffPath);
        persistedFailure.Failure!.Context["actualPixels"].ShouldNotBeNullOrWhiteSpace();

        // Final approval binds only to the successful validated output.
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.PhotoshopOutput, output.Sha256), "tester",
            CancellationToken.None));

        SessionAggregate completed = await LoadAsync(harness, id);
        PrintOutput printOutput = completed.Outputs.Single();
        printOutput.Sha256.ShouldBe(output.Sha256);
        printOutput.Sha256.ShouldNotBe(failedTiffHash);
    }

    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The closed end state every invalid outcome must reach: nothing produced, nothing
    /// reviewable, nothing approvable.
    /// </summary>
    /// <remarks>
    /// The approval is attempted with the upstream Revision's own hash, because that is the most
    /// plausible way an invalid run could leak into final review: there is no output hash to
    /// offer, so the only value a caller could reach for is the one artefact that does exist.
    /// Refusing it is the property; refusing an obviously fabricated hash would not be.
    /// </remarks>
    private static async Task AssertNothingReviewableAsync(
        SessionServiceHarness harness, ISessionService service, SessionId id,
        AttemptStatus expectedStatus, string acCase)
    {
        SessionAggregate aggregate = await LoadAsync(harness, id);

        aggregate.Revisions.ShouldNotContain(r => r.Operation == OperationKind.PhotoshopOutput, acCase);
        aggregate.Outputs.ShouldBeEmpty(acCase);

        ProcessingAttempt attempt = PhotoshopAttempt(aggregate);
        attempt.Status.ShouldBe(expectedStatus, acCase);
        attempt.OutputRevisionId.ShouldBeNull(acCase);
        attempt.Failure.ShouldNotBeNull(acCase);

        SessionStep step = aggregate.Steps.Single(s => s.Step == StepKind.PhotoshopOutput);
        step.State.ShouldBe(StepState.Failed, acCase);
        step.CurrentRevisionId.ShouldBeNull(acCase);

        Revision upstream = aggregate.Revisions.Single(r => r.Id == CurrentInputRevisionId(aggregate));
        OperationResult<SessionView> approval = await service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.PhotoshopOutput, upstream.Sha256), "tester",
            CancellationToken.None);

        approval.IsFailure.ShouldBeTrue($"{acCase} reached final approval.");
        approval.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet, acCase);
        (await LoadAsync(harness, id)).Outputs.ShouldBeEmpty(acCase);
    }

    /// <summary>The Revision the Photoshop step is working from.</summary>
    private static RevisionId CurrentInputRevisionId(SessionAggregate aggregate) =>
        PhotoshopAttempt(aggregate).InputRevisionId
        ?? throw new InvalidOperationException("The Photoshop attempt recorded no input Revision.");

    /// <summary>Imports a GeneratePrintTiff session and takes it to the Photoshop step.</summary>
    private static async Task<SessionId> ReadyForPhotoshopAsync(
        SessionServiceHarness harness, ISessionService service)
    {
        SessionId id = (await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, harness.WriteBorderedSourcePng(), "tiff-out", "tester",
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

    private static Revision TiffRevision(SessionAggregate aggregate) =>
        aggregate.Revisions.Last(r => r.Operation == OperationKind.PhotoshopOutput);

    private static ProcessingAttempt PhotoshopAttempt(SessionAggregate aggregate) =>
        aggregate.Attempts.Last(a => a.Step == StepKind.PhotoshopOutput);

    private static async Task<SessionAggregate> LoadAsync(SessionServiceHarness harness, SessionId id) =>
        (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;

    private static Sha256 Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Sha256.FromBytes(SHA256.HashData(stream));
    }

    private static async Task Must(Task<OperationResult<SessionView>> resultTask)
    {
        OperationResult<SessionView> result = await resultTask;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }
}
