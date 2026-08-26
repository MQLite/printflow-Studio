using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Unit.Workflow;

/// <summary>
/// The phase-specific Stop rules of Epic 11300 Part D2A §4–§16, asserted as a table.
/// </summary>
/// <remarks>
/// §4 forbids one generic "cancel everything" implementation across all phases, and the way to
/// keep that honest is to state each phase's answer separately and then assert that <b>every</b>
/// phase has one. A rule that quietly collapsed the eight into "cancel if you can" would pass a
/// handful of these and fail the exhaustiveness test below.
/// </remarks>
public sealed class AutomationStopPolicyTests
{
    public static TheoryData<AutomationStopMode, ExternalOperationPhase> EveryPair()
    {
        TheoryData<AutomationStopMode, ExternalOperationPhase> data = [];
        foreach (AutomationStopMode mode in Enum.GetValues<AutomationStopMode>())
        {
            foreach (ExternalOperationPhase phase in Enum.GetValues<ExternalOperationPhase>())
            {
                data.Add(mode, phase);
            }
        }

        return data;
    }

    /// <summary>Every mode/phase pair resolves, and never to a half-answer.</summary>
    /// <remarks>
    /// The invariant worth stating is the second assertion: a resolution that permits an action
    /// while forbidding input is self-contradictory, and it is the shape a careless addition to
    /// the table would take.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryPair))]
    public void Every_pair_resolves_consistently(AutomationStopMode mode, ExternalOperationPhase phase)
    {
        AutomationStopResolution resolution = AutomationStopPolicy.Resolve(mode, phase);

        if (resolution.MayInvokeOperationCancel || resolution.MayCancelExportSurface)
        {
            resolution.MayProduceAnyInput.ShouldBeTrue(
                "an action that is permitted must also be permitted to produce input");
        }

        Enum.IsDefined(resolution.Retained).ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------
    // §19, §20 — a takeover produces no input, from anywhere
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A Take Over permits no input at all, from every phase (§19, §20).
    /// </summary>
    /// <remarks>
    /// The single most important row in the table, and the reason it is asserted across every
    /// phase rather than at the interesting ones: "take over" must never quietly become "cancel
    /// and then take over", and the way that happens is a phase somebody forgot.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryPair))]
    public void A_take_over_never_permits_any_input(
        AutomationStopMode mode, ExternalOperationPhase phase)
    {
        if (mode != AutomationStopMode.TakeOver)
        {
            return;
        }

        AutomationStopResolution resolution = AutomationStopPolicy.Resolve(mode, phase);

        resolution.MayProduceAnyInput.ShouldBeFalse();
        resolution.MayInvokeOperationCancel.ShouldBeFalse();
        resolution.MayCancelExportSurface.ShouldBeFalse();
    }

    // -----------------------------------------------------------------------------
    // §9 — the cancel is eligible in exactly one phase
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Only a Stop, and only during Busy, may invoke the operation cancel (§9).
    /// </summary>
    /// <remarks>
    /// Stated as "exactly one pair out of sixteen" rather than as "Busy is allowed", because the
    /// claim that matters is the exclusion. §6 and §7 tie the cancel affordance to the Busy
    /// surface and to the current load, so a phase in which PrintFlow has not positively seen
    /// Busy is one in which it has proven nothing about what it would be clicking.
    /// </remarks>
    [Fact]
    public void The_operation_cancel_is_eligible_in_exactly_one_mode_and_phase()
    {
        List<(AutomationStopMode Mode, ExternalOperationPhase Phase)> eligible = [];

        foreach (AutomationStopMode mode in Enum.GetValues<AutomationStopMode>())
        {
            foreach (ExternalOperationPhase phase in Enum.GetValues<ExternalOperationPhase>())
            {
                if (AutomationStopPolicy.Resolve(mode, phase).MayInvokeOperationCancel)
                {
                    eligible.Add((mode, phase));
                }
            }
        }

        eligible.ShouldBe([(AutomationStopMode.StopOperation, ExternalOperationPhase.Busy)]);
    }

    // -----------------------------------------------------------------------------
    // §5, §13, §14, §15, §16 — the rest of the phases, one by one
    // -----------------------------------------------------------------------------

    /// <summary>Before any operation input, a Stop produces nothing and Meitu retains nothing (§5).</summary>
    [Fact]
    public void Before_the_operation_starts_nothing_is_retained_and_nothing_is_sent()
    {
        AutomationStopResolution resolution = AutomationStopPolicy.Resolve(
            AutomationStopMode.StopOperation, ExternalOperationPhase.NotStarted);

        resolution.MayProduceAnyInput.ShouldBeFalse();
        resolution.Retained.ShouldBe(RetainedExternalState.None);
    }

    /// <summary>
    /// After the invoke but before Busy is seen, no cancel is eligible and the operation is
    /// reported as possibly running (§6, §7).
    /// </summary>
    [Fact]
    public void Between_the_invoke_and_busy_no_cancel_is_eligible()
    {
        AutomationStopResolution resolution = AutomationStopPolicy.Resolve(
            AutomationStopMode.StopOperation, ExternalOperationPhase.OperationRequested);

        resolution.MayInvokeOperationCancel.ShouldBeFalse();
        resolution.MayProduceAnyInput.ShouldBeFalse();
        resolution.Retained.ShouldBe(RetainedExternalState.OperationMayStillBeRunning);
    }

    /// <summary>
    /// After completion and before export, Stop means "do not export" and nothing else (§13).
    /// </summary>
    /// <remarks>
    /// The retained state is the assertion that matters. PrintFlow neither exports nor discards,
    /// and the operator has to be told the processed result is still sitting in Meitu.
    /// </remarks>
    [Fact]
    public void After_completion_and_before_export_the_result_is_reported_as_retained()
    {
        AutomationStopResolution resolution = AutomationStopPolicy.Resolve(
            AutomationStopMode.StopOperation, ExternalOperationPhase.CompletedBeforeExport);

        resolution.MayProduceAnyInput.ShouldBeFalse();
        resolution.Retained.ShouldBe(RetainedExternalState.ProcessedResultRetained);
    }

    /// <summary>
    /// With the save surface open and nothing confirmed, the signed export cancel is preferred
    /// to leaving a modal blocking (§14).
    /// </summary>
    [Fact]
    public void Before_the_export_confirm_the_signed_dialog_cancel_is_permitted()
    {
        AutomationStopResolution resolution = AutomationStopPolicy.Resolve(
            AutomationStopMode.StopOperation, ExternalOperationPhase.ExportPrepared);

        resolution.MayCancelExportSurface.ShouldBeTrue();
        resolution.MayProduceAnyInput.ShouldBeTrue();
        resolution.MayInvokeOperationCancel.ShouldBeFalse("the operation has already finished");
    }

    /// <summary>
    /// Once the confirm has been invoked, PrintFlow claims no ability to cancel the write (§15).
    /// </summary>
    [Fact]
    public void After_the_export_confirm_nothing_further_is_attempted()
    {
        AutomationStopResolution resolution = AutomationStopPolicy.Resolve(
            AutomationStopMode.StopOperation, ExternalOperationPhase.ExportConfirmed);

        resolution.MayProduceAnyInput.ShouldBeFalse();
        resolution.MayCancelExportSurface.ShouldBeFalse();
        resolution.Retained.ShouldBe(RetainedExternalState.OutputWriteConfirmed);
    }

    /// <summary>
    /// A validated output must survive a stop, whichever mode asked for it (§16).
    /// </summary>
    [Theory]
    [InlineData(AutomationStopMode.StopOperation)]
    [InlineData(AutomationStopMode.TakeOver)]
    public void A_validated_output_is_preserved(AutomationStopMode mode)
    {
        AutomationStopPolicy.Resolve(mode, ExternalOperationPhase.OutputValidated)
            .PreservesValidatedSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// A validated output is the <b>only</b> phase that preserves success (§16).
    /// </summary>
    /// <remarks>
    /// The exclusion is the point again. If an earlier phase claimed to preserve success there
    /// would be no success to preserve, and the flag would be a licence to skip the stop
    /// handling on a run that produced nothing.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryPair))]
    public void No_other_phase_claims_to_preserve_success(
        AutomationStopMode mode, ExternalOperationPhase phase)
    {
        if (phase == ExternalOperationPhase.OutputValidated)
        {
            return;
        }

        AutomationStopPolicy.Resolve(mode, phase).PreservesValidatedSuccess.ShouldBeFalse();
    }

    /// <summary>Unknown or blocked permits nothing and admits to knowing nothing (§10, §20).</summary>
    [Theory]
    [InlineData(AutomationStopMode.StopOperation)]
    [InlineData(AutomationStopMode.TakeOver)]
    public void An_unknown_or_blocked_screen_permits_nothing(AutomationStopMode mode)
    {
        AutomationStopResolution resolution = AutomationStopPolicy.Resolve(
            mode, ExternalOperationPhase.UnknownOrBlocked);

        resolution.MayProduceAnyInput.ShouldBeFalse();
        resolution.Retained.ShouldBe(RetainedExternalState.Unknown);
    }

    /// <summary>
    /// A phase value the table does not recognise fails closed (§4).
    /// </summary>
    /// <remarks>
    /// The direction a future addition takes if somebody adds an enum member and forgets the
    /// rule: refused and reported as unknown, never permitted.
    /// </remarks>
    [Fact]
    public void An_unrecognised_phase_fails_closed()
    {
        AutomationStopResolution resolution = AutomationStopPolicy.Resolve(
            AutomationStopMode.StopOperation, (ExternalOperationPhase)999);

        resolution.MayProduceAnyInput.ShouldBeFalse();
        resolution.MayInvokeOperationCancel.ShouldBeFalse();
        resolution.Retained.ShouldBe(RetainedExternalState.Unknown);
    }

    // -----------------------------------------------------------------------------
    // Retained state, and the audit that carries it
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A positively invoked cancel is the only thing that reports the operation as cancelled
    /// (§29).
    /// </summary>
    /// <remarks>
    /// Every phase, one assertion: "the operator pressed Stop" and "a signed cancel took effect"
    /// are different claims, and §10 turns entirely on the difference.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryPair))]
    public void Only_an_invoked_cancel_reports_the_operation_as_cancelled(
        AutomationStopMode mode, ExternalOperationPhase phase)
    {
        _ = mode;

        AutomationStopPolicy.RetainedFor(phase, cancelled: true)
            .ShouldBe(RetainedExternalState.OperationCancelled);
        AutomationStopPolicy.RetainedFor(phase, cancelled: false)
            .ShouldNotBe(RetainedExternalState.OperationCancelled);
    }

    /// <summary>
    /// The persisted audit reads back exactly what was written (§29).
    /// </summary>
    /// <remarks>
    /// The two halves of the context-key contract live in one file for this reason, and this is
    /// the test that keeps them honest: a rename on the writing side that missed the reading
    /// side would silently produce an audit nothing can interpret.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryPair))]
    public void The_stop_audit_round_trips(AutomationStopMode mode, ExternalOperationPhase phase)
    {
        RetainedExternalState retained = AutomationStopPolicy.RetainedFor(phase, cancelled: true);

        OperationFailure failure = OperationFailure.Create(
            FailureCode.Cancelled,
            "synthetic",
            isRetryable: true,
            context: new Dictionary<string, string>
            {
                [AutomationStopAudit.StopRequestedKey] = "true",
                [AutomationStopAudit.ModeKey] = mode.ToString(),
                [AutomationStopAudit.PhaseKey] = phase.ToString(),
                [AutomationStopAudit.CancelInvokedKey] = "true",
                [AutomationStopAudit.RetainedKey] = retained.ToString(),
            });

        AutomationStopAudit audit = AutomationStopAudit.Read(failure).ShouldNotBeNull();
        audit.Mode.ShouldBe(mode);
        audit.Phase.ShouldBe(phase);
        audit.SignedCancelInvoked.ShouldBeTrue();
        audit.Retained.ShouldBe(retained);
    }

    /// <summary>
    /// A cancellation nobody requested is not read as an operator stop (§29).
    /// </summary>
    /// <remarks>
    /// <c>FailureCode.Cancelled</c> alone is not evidence: a shutdown cancels the orchestration
    /// token and carries the same code. Reporting "the operator stopped this" about a run nobody
    /// stopped would be a worse error than reporting nothing.
    /// </remarks>
    [Fact]
    public void A_cancellation_without_a_stop_request_is_not_read_as_an_operator_stop()
    {
        OperationFailure shutdown = OperationFailure.Create(
            FailureCode.Cancelled, "the process is shutting down", isRetryable: true);

        AutomationStopAudit.Read(shutdown).ShouldBeNull();
        AutomationStopAudit.Read(null).ShouldBeNull();
    }

    /// <summary>The inert signal reports nothing and accepts everything silently.</summary>
    /// <remarks>
    /// A null object exists so an adapter can read <c>Stop</c> unconditionally. If it ever
    /// reported a requested mode, an unattended call would behave as though somebody had asked
    /// it to stop.
    /// </remarks>
    [Fact]
    public void The_inert_stop_signal_never_reports_a_request()
    {
        IAutomationStopSignal inert = InertAutomationStopSignal.Instance;

        inert.ReportPhase(ExternalOperationPhase.Busy);
        inert.ReportOperationCancelOutcome(leftBusy: true);

        inert.RequestedMode.ShouldBeNull();
        inert.Phase.ShouldBe(ExternalOperationPhase.NotStarted);
        inert.OperationCancelWasInvoked.ShouldBeFalse();
        inert.OperationLeftBusyAfterCancel.ShouldBeFalse();
        inert.Resolve().ShouldBeNull();
    }
}
