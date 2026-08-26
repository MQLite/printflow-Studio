using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Automation;

/// <summary>
/// The signed Busy cancel: which element PrintFlow will invoke, when it will invoke it, how
/// many times, and every case in which it refuses instead
/// (Epic 11300 Part D2A §6–§11, §36.2–§36.5).
/// </summary>
/// <remarks>
/// Every refusal here asserts that <see cref="RecordingUiElementProvider.Invocations"/> contains
/// no cancel. A failure code proves PrintFlow reported a problem; a zero invocation count proves
/// it did not click something and report the problem afterwards — which is the whole of §10.
/// <para>
/// The decoy tests are the ones worth reading twice. The fake picker's Cancel is named
/// <b>exactly</b> 取消, because the real one is: the workstation discovery found it sitting
/// beside the operation's cancel in every run. A rule that matched on the name would find it,
/// invoke it, and dismiss a file dialog instead of cancelling anything.
/// </para>
/// </remarks>
public sealed class GuardedMeituBusyCancelTests
{
    private const string ExpectedFile = "PF_D2A_A.png";
    private const string CancelId = MeituFakes.BusyCancelAutomationId;

    private static readonly MeituAutomationOptions FastOptions = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(5),
        ActivationTimeout = TimeSpan.FromMilliseconds(40),
        CancelSettleTimeout = TimeSpan.FromMilliseconds(60),
    };

    // -----------------------------------------------------------------------------
    // §36.2, §36.3 — exactly one signed cancel, for each operation
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A stop during Enhancement Busy invokes the signed cancel exactly once and observes
    /// Meitu leave Busy (§9, §11).
    /// </summary>
    /// <remarks>
    /// "Exactly once" is asserted as a count rather than as "at least once", because §9 forbids
    /// automatic retry and the failure mode it guards against — a cancel that was accepted and a
    /// cancel that was ignored looking identical from outside — is only prevented if the second
    /// invocation never happens.
    /// </remarks>
    [Fact]
    public async Task A_stop_during_enhancement_busy_invokes_the_signed_cancel_exactly_once()
    {
        Scenario s = Build(MeituFakes.BusyTexts());
        s.AfterCancel(MeituFakes.CompletedTexts());

        OperationResult<MeituCancelOutcome> cancelled = await s.Driver.CancelRunningOperationAsync(
            s.Editor, MeituOperation.Enhance, ExpectedFile, CancellationToken.None);

        cancelled.IsSuccess.ShouldBeTrue(
            cancelled.IsFailure ? cancelled.Failure.TechnicalDetail : string.Empty);
        s.Invocations(CancelId).ShouldBe(1);
        cancelled.Value.Operation.ShouldBe(MeituOperation.Enhance);
        cancelled.Value.LeftBusy.ShouldBeTrue();
        cancelled.Value.BusyBeforeCancel.State.ShouldBe(MeituStartingState.Busy);
    }

    /// <summary>The same for Background Removal, whose Busy signature is its own (§7).</summary>
    [Fact]
    public async Task A_stop_during_background_removal_busy_invokes_the_signed_cancel_exactly_once()
    {
        Scenario s = Build(MeituFakes.BackgroundBusyTexts());
        s.AfterCancel(MeituFakes.BackgroundCompletedTexts());

        OperationResult<MeituCancelOutcome> cancelled = await s.Driver.CancelRunningOperationAsync(
            s.Editor, MeituOperation.RemoveBackground, ExpectedFile, CancellationToken.None);

        cancelled.IsSuccess.ShouldBeTrue(
            cancelled.IsFailure ? cancelled.Failure.TechnicalDetail : string.Empty);
        s.Invocations(CancelId).ShouldBe(1);
        cancelled.Value.Operation.ShouldBe(MeituOperation.RemoveBackground);
        cancelled.Value.LeftBusy.ShouldBeTrue();
    }

    /// <summary>
    /// A cancel that does not take effect is reported honestly and never invoked a second time
    /// (§9, §11).
    /// </summary>
    /// <remarks>
    /// Meitu stays Busy for the whole settle window. The result is still a success — a cancel
    /// <i>was</i> invoked — carrying <c>LeftBusy: false</c>, which is what lets the audit say
    /// "the operation may still be running" rather than guessing either way.
    /// </remarks>
    [Fact]
    public async Task A_cancel_that_does_not_take_effect_is_reported_and_never_repeated()
    {
        Scenario s = Build(MeituFakes.BusyTexts());

        OperationResult<MeituCancelOutcome> cancelled = await s.Driver.CancelRunningOperationAsync(
            s.Editor, MeituOperation.Enhance, ExpectedFile, CancellationToken.None);

        cancelled.IsSuccess.ShouldBeTrue();
        cancelled.Value.LeftBusy.ShouldBeFalse();
        s.Invocations(CancelId).ShouldBe(1, "§9 permits exactly one invocation, effective or not");
    }

    // -----------------------------------------------------------------------------
    // §36.4 — ambiguous, missing, wrong-shaped, disabled
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The file picker's own Cancel is not the operation's cancel, and is never invoked (§8).
    /// </summary>
    /// <remarks>
    /// The decoy is present <i>alone</i> here, so a name-based implementation would find exactly
    /// one match and invoke it happily. That is the failure this test exists to catch, and it is
    /// not hypothetical: the workstation discovery recorded this element in every run.
    /// </remarks>
    [Fact]
    public async Task The_file_pickers_own_cancel_is_never_invoked()
    {
        Scenario s = Build(MeituFakes.BusyTexts(), withSignedCancel: false, withDecoy: true);

        OperationResult<MeituCancelOutcome> cancelled = await s.Driver.CancelRunningOperationAsync(
            s.Editor, MeituOperation.Enhance, ExpectedFile, CancellationToken.None);

        cancelled.IsFailure.ShouldBeTrue();
        s.Elements.Invocations.ShouldBeEmpty("nothing may be invoked when the signed cancel is absent");
        cancelled.Failure.Context["meituCancelInvoked"].ShouldBe("false");
    }

    /// <summary>
    /// Two structurally valid cancels are refused rather than chosen between (§8, §10).
    /// </summary>
    [Fact]
    public async Task Two_structurally_valid_cancels_are_refused()
    {
        Scenario s = Build(MeituFakes.BusyTexts());
        s.Elements.AddBusyCancel(s.Editor.Window.Handle, processId: s.Editor.Process.ProcessId);

        OperationResult<MeituCancelOutcome> cancelled = await s.Driver.CancelRunningOperationAsync(
            s.Editor, MeituOperation.Enhance, ExpectedFile, CancellationToken.None);

        cancelled.IsFailure.ShouldBeTrue();
        s.Elements.Invocations.ShouldBeEmpty();
        cancelled.Failure.TechnicalDetail.ShouldContain("will not choose between them");
    }

    /// <summary>No element named 取消 at all is a refusal with nothing sent (§10).</summary>
    [Fact]
    public async Task A_missing_cancel_control_produces_no_input()
    {
        Scenario s = Build(MeituFakes.BusyTexts(), withSignedCancel: false);

        OperationResult<MeituCancelOutcome> cancelled = await s.Driver.CancelRunningOperationAsync(
            s.Editor, MeituOperation.Enhance, ExpectedFile, CancellationToken.None);

        cancelled.IsFailure.ShouldBeTrue();
        s.Elements.Invocations.ShouldBeEmpty();
    }

    /// <summary>A disabled cancel is refused rather than invoked (§10).</summary>
    [Fact]
    public async Task A_disabled_cancel_control_produces_no_input()
    {
        Scenario s = Build(MeituFakes.BusyTexts(), withSignedCancel: false);
        s.Elements.AddBusyCancel(
            s.Editor.Window.Handle, processId: s.Editor.Process.ProcessId, enabled: false);

        OperationResult<MeituCancelOutcome> cancelled = await s.Driver.CancelRunningOperationAsync(
            s.Editor, MeituOperation.Enhance, ExpectedFile, CancellationToken.None);

        cancelled.IsFailure.ShouldBeTrue();
        s.Elements.Invocations.ShouldBeEmpty();
    }

    /// <summary>
    /// A cancel sitting outside the signed progress-mask ancestry is refused (§6, §8).
    /// </summary>
    /// <remarks>
    /// The automation id and every property of the button itself match; only the chain above it
    /// differs. That is the case the ancestor check exists for, and without it the id substring
    /// would carry the whole decision.
    /// </remarks>
    [Fact]
    public async Task A_cancel_outside_the_signed_ancestry_is_refused()
    {
        Scenario s = Build(MeituFakes.BusyTexts(), withSignedCancel: false);
        s.Elements.AddBusyCancel(
            s.Editor.Window.Handle,
            ancestorClasses: ["QWidget", "QFrame", "SomeOtherDialog"],
            processId: s.Editor.Process.ProcessId);

        OperationResult<MeituCancelOutcome> cancelled = await s.Driver.CancelRunningOperationAsync(
            s.Editor, MeituOperation.Enhance, ExpectedFile, CancellationToken.None);

        cancelled.IsFailure.ShouldBeTrue();
        s.Elements.Invocations.ShouldBeEmpty();
    }

    /// <summary>A cancel of the wrong class is refused even with the right id and name (§8).</summary>
    [Fact]
    public async Task A_cancel_of_the_wrong_class_is_refused()
    {
        Scenario s = Build(MeituFakes.BusyTexts(), withSignedCancel: false);
        s.Elements.AddBusyCancel(
            s.Editor.Window.Handle, className: "Button", processId: s.Editor.Process.ProcessId);

        OperationResult<MeituCancelOutcome> cancelled = await s.Driver.CancelRunningOperationAsync(
            s.Editor, MeituOperation.Enhance, ExpectedFile, CancellationToken.None);

        cancelled.IsFailure.ShouldBeTrue();
        s.Elements.Invocations.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // §7 — operation correlation
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A cancel is refused when the named operation's own Busy signature does not match, even
    /// though the other operation's does (§7).
    /// </summary>
    /// <remarks>
    /// This is the rule the shared control makes necessary. Meitu raises one progress mask for
    /// both operations, so resolving the control says nothing about which operation is running —
    /// and cancelling a cutout while claiming to have cancelled an enhancement would be exactly
    /// the mislabelled action §7 exists to prevent.
    /// </remarks>
    [Fact]
    public async Task A_cancel_for_the_wrong_operation_is_refused_even_though_something_is_busy()
    {
        Scenario s = Build(MeituFakes.BackgroundBusyTexts());

        OperationResult<MeituCancelOutcome> cancelled = await s.Driver.CancelRunningOperationAsync(
            s.Editor, MeituOperation.Enhance, ExpectedFile, CancellationToken.None);

        cancelled.IsFailure.ShouldBeTrue();
        s.Elements.Invocations.ShouldBeEmpty();
        cancelled.Failure.TechnicalDetail.ShouldContain("does not match the current screen");
    }

    /// <summary>A cancel is refused when nothing is Busy at all (§7).</summary>
    [Fact]
    public async Task A_cancel_with_no_operation_running_is_refused()
    {
        Scenario s = Build(MeituFakes.CompletedTexts());

        OperationResult<MeituCancelOutcome> cancelled = await s.Driver.CancelRunningOperationAsync(
            s.Editor, MeituOperation.Enhance, ExpectedFile, CancellationToken.None);

        cancelled.IsFailure.ShouldBeTrue();
        s.Elements.Invocations.ShouldBeEmpty();
    }

    /// <summary>
    /// A preset that vouches for no cancel evidence leaves the cancel unreachable (§10).
    /// </summary>
    /// <remarks>
    /// The fail-closed direction, and the same one every other optional signature takes: no
    /// evidence means no action, never "act on the remaining evidence".
    /// </remarks>
    [Fact]
    public async Task An_unsigned_cancel_is_never_invoked()
    {
        Scenario s = Build(
            MeituFakes.BusyTexts(), baseline: MeituFakes.BaselineWithout(busyCancel: true));

        OperationResult<MeituCancelOutcome> cancelled = await s.Driver.CancelRunningOperationAsync(
            s.Editor, MeituOperation.Enhance, ExpectedFile, CancellationToken.None);

        cancelled.IsFailure.ShouldBeTrue();
        s.Elements.Invocations.ShouldBeEmpty();
        cancelled.Failure.TechnicalDetail.ShouldContain("vouches for no Meitu cancel evidence");
    }

    // -----------------------------------------------------------------------------
    // §36.5 — target loss, and §10's blocking modal
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Losing the foreground before the cancel produces zero cancel input (§10, §36.5).
    /// </summary>
    /// <remarks>
    /// The same guard every other input path in the driver takes, asserted here because a Stop
    /// is the one moment an implementation is most tempted to skip it: the operator is waiting,
    /// and clicking anyway would be sending a cancel into whatever is now in front.
    /// </remarks>
    [Fact]
    public async Task Losing_the_target_before_the_cancel_produces_no_input()
    {
        Scenario s = Build(MeituFakes.BusyTexts());
        s.Locator.RefuseActivation = true;
        s.Locator.Foreground = new ForegroundIdentity(new WindowHandle(0xDEAD), 999, "explorer");

        OperationResult<MeituCancelOutcome> cancelled = await s.Driver.CancelRunningOperationAsync(
            s.Editor, MeituOperation.Enhance, ExpectedFile, CancellationToken.None);

        cancelled.IsFailure.ShouldBeTrue();
        cancelled.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        s.Elements.Invocations.ShouldBeEmpty();
    }

    /// <summary>
    /// A Meitu-owned modal over the running operation stops the cancel; nothing is dismissed
    /// (§10, §20).
    /// </summary>
    /// <remarks>
    /// The cancel may well be underneath the modal, and PrintFlow does not reach past a surface
    /// it cannot identify to get at it. The operator resolves the modal, or takes over — which
    /// §20 permits precisely because it requires no interaction with the unknown UI.
    /// </remarks>
    [Fact]
    public async Task A_blocking_modal_over_the_operation_stops_the_cancel_and_dismisses_nothing()
    {
        Scenario s = Build(MeituFakes.BusyTexts());
        s.Locator.OwnedDialogs.Add(MeituFakes.Window(
            handle: 0x7000, owningProcessId: s.Editor.Process.ProcessId, title: "温馨提示"));

        OperationResult<MeituCancelOutcome> cancelled = await s.Driver.CancelRunningOperationAsync(
            s.Editor, MeituOperation.Enhance, ExpectedFile, CancellationToken.None);

        cancelled.IsFailure.ShouldBeTrue();
        cancelled.Failure.Code.ShouldBe(FailureCode.MeituBlockingDialog);
        s.Elements.Invocations.ShouldBeEmpty();
    }

    /// <summary>
    /// A cancel in another process is refused (§10).
    /// </summary>
    [Fact]
    public async Task A_cancel_belonging_to_another_process_is_refused()
    {
        Scenario s = Build(MeituFakes.BusyTexts(), withSignedCancel: false);
        s.Elements.AddBusyCancel(s.Editor.Window.Handle, processId: 999);

        OperationResult<MeituCancelOutcome> cancelled = await s.Driver.CancelRunningOperationAsync(
            s.Editor, MeituOperation.Enhance, ExpectedFile, CancellationToken.None);

        cancelled.IsFailure.ShouldBeTrue();
        s.Elements.Invocations.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // §11 — the post-cancel state is observed, never assumed
    // -----------------------------------------------------------------------------

    /// <summary>
    /// An unrecognised screen after a successful cancel is reported, not treated as a failure
    /// (§11).
    /// </summary>
    /// <remarks>
    /// §11 forbids assuming a cancelled operation returns to a known editor. The honest
    /// consequence is that an unrecognised post-cancel screen is a <i>state to report</i>, not
    /// an error about a cancel that may well have worked — so the outcome is a success carrying
    /// <c>Unknown</c>, and the operator is told what PrintFlow last saw.
    /// </remarks>
    [Fact]
    public async Task An_unrecognised_screen_after_a_cancel_is_reported_rather_than_failed()
    {
        Scenario s = Build(MeituFakes.BusyTexts());
        s.AfterCancel(["SOMETHING-PRINTFLOW-HAS-NEVER-SEEN"]);

        OperationResult<MeituCancelOutcome> cancelled = await s.Driver.CancelRunningOperationAsync(
            s.Editor, MeituOperation.Enhance, ExpectedFile, CancellationToken.None);

        cancelled.IsSuccess.ShouldBeTrue();
        cancelled.Value.LeftBusy.ShouldBeTrue("the operation's Busy signature stopped matching");
        cancelled.Value.StateAfterCancel.State.ShouldBe(MeituStartingState.Unknown);
    }

    /// <summary>
    /// The outcome record makes no claim about the document or about readiness to retry (§11, §12).
    /// </summary>
    /// <remarks>
    /// A structural assertion rather than a behavioural one, and deliberately so: the way this
    /// rule gets broken is somebody adding a convenient <c>Succeeded</c> or <c>ReadyForRetry</c>
    /// member, and the next caller reading it as permission to skip the safe-start checks.
    /// </remarks>
    [Fact]
    public void The_cancel_outcome_claims_nothing_about_success_or_readiness()
    {
        string[] members = [.. typeof(MeituCancelOutcome).GetProperties().Select(p => p.Name)];

        members.ShouldNotContain("Succeeded");
        members.ShouldNotContain("OutputPath");
        members.ShouldNotContain("Revision");
        members.ShouldNotContain("ReadyForRetry");
        members.ShouldNotContain("DocumentIntact");
    }

    // -----------------------------------------------------------------------------
    // Fixture
    // -----------------------------------------------------------------------------

    private sealed class Scenario
    {
        public required FakeWindowLocator Locator { get; init; }

        public required RecordingUiElementProvider Elements { get; init; }

        public required GuardedMeituUiDriver Driver { get; init; }

        public required MeituTarget Editor { get; init; }

        public int Invocations(string automationId) =>
            Elements.Invocations.Count(i => i == automationId);

        /// <summary>What the editor shows once the cancel has been invoked.</summary>
        public void AfterCancel(string[] texts) => Elements.OnInvoke = invoked =>
        {
            if (invoked == CancelId)
            {
                Elements.SetTexts(Editor.Window.Handle, texts);
            }
        };
    }

    /// <summary>
    /// Builds a fake Meitu showing <paramref name="busyTexts"/>, with the signed cancel present
    /// unless a test is about its absence.
    /// </summary>
    private static Scenario Build(
        string[] busyTexts,
        bool withSignedCancel = true,
        bool withDecoy = false,
        MeituBaseline? baseline = null)
    {
        ExternalProcessRef process = MeituFakes.Process();
        ExternalWindowRef editor = MeituFakes.Window(title: MeituFakes.EditorTitle);

        FakeWindowLocator locator = new();
        RecordingUiElementProvider elements = new();

        locator.Register(process, editor);
        locator.PutInForeground(editor);
        elements.SetTexts(editor.Handle, busyTexts);

        if (withSignedCancel)
        {
            elements.AddBusyCancel(editor.Handle, processId: process.ProcessId);
        }

        if (withDecoy)
        {
            elements.AddDecoyPickerCancel(editor.Handle, process.ProcessId);
        }

        return new Scenario
        {
            Locator = locator,
            Elements = elements,
            Driver = new GuardedMeituUiDriver(
                locator, elements, new RecordingInputSink(), new RecordingEvidenceSink(),
                new StubMeituBaselineProvider(baseline ?? MeituFakes.Baseline()),
                FastOptions,
                TimeProvider.System),
            Editor = new MeituTarget(process, editor),
        };
    }
}
