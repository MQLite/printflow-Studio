using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Tests.Fixtures;

using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Automation;

/// <summary>
/// What Meitu did with a document on its own initiative, and what PrintFlow is entitled to
/// conclude from it (Epic 11300 Part B2B §20, §21, §22).
/// </summary>
/// <remarks>
/// The hazard these tests exist for is subtle enough to be worth restating. Meitu keeps its
/// selected module across document loads, and the module's parameter panel <i>is</i> the signed
/// completion signature — so the panel from the previous document is still on screen when the
/// next one opens. It was observed still matching on an editor holding nothing at all.
///
/// The consequence is that a completion match says nothing whatever about the document now in
/// front of PrintFlow, and the only thing that ties an enhancement to a particular load is
/// having positively watched Busy happen between the open and the claim. Every test below is a
/// variation on that one distinction.
/// </remarks>
public sealed class MeituLoadObservationTests
{
    private const string ExpectedFile = "PF_B2B_A.png";

    private static readonly MeituAutomationOptions FastOptions = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(5),
        ActivationTimeout = TimeSpan.FromMilliseconds(40),
        DialogTimeout = TimeSpan.FromMilliseconds(40),
        AutoEnhancementWatchTimeout = TimeSpan.FromMilliseconds(60),
        EnhancementCompletionTimeout = TimeSpan.FromMilliseconds(200),
    };

    private sealed class Scenario
    {
        public required RecordingUiElementProvider Elements { get; init; }

        public required RecordingInputSink Input { get; init; }

        public required GuardedMeituUiDriver Driver { get; init; }

        public required MeituTarget Editor { get; init; }

        /// <summary>Screens shown on successive reads; the last one reached persists.</summary>
        public Queue<string[]> Screens { get; } = new();
    }

    private static Scenario Build(params string[][] screens)
    {
        ExternalProcessRef process = MeituFakes.Process();
        ExternalWindowRef editor = MeituFakes.Window(title: MeituFakes.EditorTitle);

        FakeWindowLocator locator = new();
        RecordingUiElementProvider elements = new();
        RecordingInputSink input = new();

        locator.Register(process, editor);
        locator.PutInForeground(editor);

        Scenario scenario = new()
        {
            Elements = elements,
            Input = input,
            Driver = new GuardedMeituUiDriver(
                locator, elements, input, new RecordingEvidenceSink(),
                new StubMeituBaselineProvider(MeituFakes.Baseline()), FastOptions, TimeProvider.System),
            Editor = new MeituTarget(process, editor),
        };

        foreach (string[] screen in screens)
        {
            scenario.Screens.Enqueue(screen);
        }

        elements.SetTexts(editor.Handle, scenario.Screens.Count > 0 ? scenario.Screens.Dequeue() : []);
        elements.OnReadTextSnapshot = _ =>
        {
            if (scenario.Screens.Count > 0)
            {
                elements.SetTexts(editor.Handle, scenario.Screens.Dequeue());
            }
        };

        return scenario;
    }

    private static string[] Idle() => [.. MeituFakes.EditorMarkers];

    private static string[] Busy() => MeituFakes.BusyTexts();

    private static string[] Panel() => MeituFakes.CompletedTexts();

    // -----------------------------------------------------------------------------

    /// <summary>
    /// A document that opens into an unselected module reports nothing and costs nothing.
    /// </summary>
    /// <remarks>
    /// The ordinary case, and the early exit that keeps it fast: Meitu cannot auto-start without
    /// a selected module, and a selected module always shows its panel, so a first reading with
    /// no panel ends the watch immediately rather than spending the whole budget proving a
    /// negative.
    /// </remarks>
    [Fact]
    public async Task A_quiet_load_reports_no_auto_started_enhancement()
    {
        Scenario s = Build(Idle(), Idle(), Idle());

        OperationResult<MeituLoadObservation> load =
            await s.Driver.ObserveLoadedDocumentAsync(s.Editor, ExpectedFile, InertAutomationStopSignal.Instance, CancellationToken.None);

        load.IsSuccess.ShouldBeTrue();
        load.Value.PhaseAtOpen.ShouldBe(MeituEnhancementPhase.Unobserved);
        load.Value.AutoStartedEnhancement.ShouldBeFalse();
        load.Value.Busy.ShouldBeNull();
        load.Value.Completion.ShouldBeNull();

        // Read-only throughout.
        s.Elements.Invocations.ShouldBeEmpty();
        s.Elements.ValueWrites.ShouldBeEmpty();
        s.Input.Sends.ShouldBeEmpty();
    }

    /// <summary>
    /// An enhancement already running when the document opens is watched to completion, not
    /// restarted.
    /// </summary>
    [Fact]
    public async Task Busy_at_the_moment_of_opening_is_waited_out_and_reported_as_auto_started()
    {
        Scenario s = Build(Busy(), Busy(), Busy(), Panel());

        OperationResult<MeituLoadObservation> load =
            await s.Driver.ObserveLoadedDocumentAsync(s.Editor, ExpectedFile, InertAutomationStopSignal.Instance, CancellationToken.None);

        load.IsSuccess.ShouldBeTrue(load.IsFailure ? load.Failure.TechnicalDetail : string.Empty);
        load.Value.PhaseAtOpen.ShouldBe(MeituEnhancementPhase.Busy);
        load.Value.AutoStartedEnhancement.ShouldBeTrue();
        load.Value.Busy!.State.ShouldBe(MeituStartingState.Busy);
        load.Value.Completion.ShouldNotBeNull();

        s.Elements.Invocations.ShouldBeEmpty();
    }

    /// <summary>
    /// An enhancement that starts a moment after the document appears is still caught.
    /// </summary>
    /// <remarks>
    /// The live timing: Busy appeared half a second after the open. Reading once and concluding
    /// would classify the gap between the document appearing and Meitu's first progress paint as
    /// "nothing is happening" — and PrintFlow would then be deciding what to do about a panel
    /// belonging to work that was about to start.
    /// </remarks>
    [Fact]
    public async Task An_enhancement_that_starts_just_after_the_open_is_still_observed()
    {
        Scenario s = Build(Panel(), Panel(), Busy(), Busy(), Panel());

        OperationResult<MeituLoadObservation> load =
            await s.Driver.ObserveLoadedDocumentAsync(s.Editor, ExpectedFile, InertAutomationStopSignal.Instance, CancellationToken.None);

        load.IsSuccess.ShouldBeTrue(load.IsFailure ? load.Failure.TechnicalDetail : string.Empty);
        load.Value.PhaseAtOpen.ShouldBe(MeituEnhancementPhase.Complete);
        load.Value.AutoStartedEnhancement.ShouldBeTrue();
    }

    /// <summary>
    /// A module panel with no work behind it is <b>not</b> an auto-started enhancement.
    /// </summary>
    /// <remarks>
    /// The §21 refusal, and the most important assertion in this file. The panel outlives the
    /// document it belonged to — it was observed matching on an empty editor — so treating it as
    /// evidence would attribute the previous document's enhancement to this one, export the
    /// unenhanced image, and create a Revision for it.
    /// </remarks>
    [Fact]
    public async Task A_stale_module_panel_is_not_reported_as_an_enhancement_of_this_document()
    {
        Scenario s = Build(Panel(), Panel(), Panel(), Panel(), Panel(), Panel(), Panel(), Panel());

        OperationResult<MeituLoadObservation> load =
            await s.Driver.ObserveLoadedDocumentAsync(s.Editor, ExpectedFile, InertAutomationStopSignal.Instance, CancellationToken.None);

        load.IsSuccess.ShouldBeTrue();
        load.Value.PhaseAtOpen.ShouldBe(MeituEnhancementPhase.Complete);
        load.Value.AutoStartedEnhancement.ShouldBeFalse();
        load.Value.Busy.ShouldBeNull();
        load.Value.Completion.ShouldNotBeNull();

        s.Elements.Invocations.ShouldBeEmpty();
    }

    /// <summary>
    /// An auto-started enhancement that never finishes times out rather than being claimed.
    /// </summary>
    [Fact]
    public async Task Busy_that_never_ends_fails_rather_than_reporting_an_unknown_success()
    {
        Scenario s = Build(Busy());

        OperationResult<MeituLoadObservation> load =
            await s.Driver.ObserveLoadedDocumentAsync(s.Editor, ExpectedFile, InertAutomationStopSignal.Instance, CancellationToken.None);

        load.IsFailure.ShouldBeTrue();
        load.Failure.Context["exported"].ShouldBe("false");
        load.Failure.Context["revisionCreated"].ShouldBe("false");
    }

    /// <summary>
    /// The transient window Meitu leaves behind after its picker closes does not abandon the run.
    /// </summary>
    /// <remarks>
    /// Observed live: the instant after the open, an owned pop-up of Meitu's own is in front and
    /// the editor classifies as a blocking modal for a fraction of a second. Stopping on it would
    /// abandon a perfectly ordinary open — while a modal that stays is still a stop, which the
    /// enhancement route's own observation enforces.
    /// </remarks>
    [Fact]
    public async Task A_transient_owned_window_right_after_the_open_does_not_end_the_observation()
    {
        ExternalProcessRef process = MeituFakes.Process();
        ExternalWindowRef editor = MeituFakes.Window(title: MeituFakes.EditorTitle);
        ExternalWindowRef transient = MeituFakes.Window(
            handle: 0x9999, owningProcessId: process.ProcessId, title: "Form", className: "QtTransient");

        FakeWindowLocator locator = new();
        RecordingUiElementProvider elements = new();
        locator.Register(process, editor);
        locator.PutInForeground(editor);
        locator.OwnedDialogs.Add(transient);
        elements.SetTexts(editor.Handle, Idle());

        // The pop-up is gone by the second read, which is what "transient" means.
        int reads = 0;
        elements.OnReadTextSnapshot = _ =>
        {
            if (++reads >= 2)
            {
                locator.OwnedDialogs.Remove(transient);
            }
        };

        GuardedMeituUiDriver driver = new(
            locator, elements, new RecordingInputSink(), new RecordingEvidenceSink(),
            new StubMeituBaselineProvider(MeituFakes.Baseline()), FastOptions, TimeProvider.System);

        OperationResult<MeituLoadObservation> load = await driver.ObserveLoadedDocumentAsync(
            new MeituTarget(process, editor), ExpectedFile, InertAutomationStopSignal.Instance,
            CancellationToken.None);

        load.IsSuccess.ShouldBeTrue(load.IsFailure ? load.Failure.TechnicalDetail : string.Empty);
        load.Value.AutoStartedEnhancement.ShouldBeFalse();
        reads.ShouldBeGreaterThan(1);
        elements.Invocations.ShouldBeEmpty();
    }

    /// <summary>Without signed enhancement evidence there is nothing to observe, and it says so.</summary>
    [Fact]
    public async Task A_chain_that_vouches_for_no_enhancement_evidence_refuses_the_observation()
    {
        ExternalProcessRef process = MeituFakes.Process();
        ExternalWindowRef editor = MeituFakes.Window(title: MeituFakes.EditorTitle);
        FakeWindowLocator locator = new();
        RecordingUiElementProvider elements = new();
        locator.Register(process, editor);
        locator.PutInForeground(editor);

        GuardedMeituUiDriver driver = new(
            locator, elements, new RecordingInputSink(), new RecordingEvidenceSink(),
            new StubMeituBaselineProvider(MeituFakes.BaselineWithout(enhancement: true)),
            FastOptions, TimeProvider.System);

        OperationResult<MeituLoadObservation> load = await driver.ObserveLoadedDocumentAsync(
            new MeituTarget(process, editor), ExpectedFile, InertAutomationStopSignal.Instance,
            CancellationToken.None);

        load.IsFailure.ShouldBeTrue();
        elements.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancellation_stops_the_observation_without_sending_anything()
    {
        Scenario s = Build(Busy());
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            s.Driver.ObserveLoadedDocumentAsync(s.Editor, ExpectedFile, InertAutomationStopSignal.Instance, cancelled.Token));

        s.Elements.Invocations.ShouldBeEmpty();
        s.Input.Sends.ShouldBeEmpty();
    }
}
