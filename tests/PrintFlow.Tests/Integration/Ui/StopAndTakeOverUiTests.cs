using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// The Stop and Take Over operator surface, driven end to end against the real session service,
/// workspace, fake Meitu adapter and SQLite database (Epic 11300 Part D2A §24–§28).
/// </summary>
/// <remarks>
/// The screen's job here is narrow: offer two clearly different controls exactly while they mean
/// something, and offer neither when they do not. Every test below is one way that could go
/// wrong — and the most likely one is offering Stop whenever the screen is busy, which would put
/// "stop the operation" beside an approval.
/// <para>
/// Outcomes that are claims about what happened are read from persistence rather than from the
/// view model: a screen reporting a takeover while the database holds an Active session is the
/// defect this suite exists to catch.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class StopAndTakeOverUiTests
{
    // -------------------------------------------------------------------------------------
    // §24, §25 — neither control appears where it means nothing
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// An idle screen offers neither Stop nor Take Over (§24, §25).
    /// </summary>
    /// <remarks>
    /// The state §24 names explicitly: a step waiting to be run, and a result waiting to be
    /// reviewed. Nothing is running, so a Stop control would be offering to stop nothing.
    /// </remarks>
    [Fact]
    public async Task An_idle_screen_offers_neither_stop_nor_take_over()
    {
        using HomeScreenHarness harness = new();
        (SessionViewModel screen, _) = await AtEnhancementAsync(harness, "stop-idle.png");

        // Waiting.
        screen.CanStopAutomation.ShouldBeFalse();
        screen.CanTakeOverAutomation.ShouldBeFalse();
        screen.IsStopping.ShouldBeFalse();
        screen.StoppingNotice.ShouldBeNull();

        harness.Inner.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);
        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;

        // ReviewRequired.
        screen.Notice.ShouldBeNull();
        screen.CanStopAutomation.ShouldBeFalse();
        screen.CanTakeOverAutomation.ShouldBeFalse();
        screen.HasRetainedExternalState.ShouldBeFalse();
        screen.CanReenterAutomation.ShouldBeFalse();
    }

    /// <summary>
    /// Both controls appear while an external operation is genuinely running, and disappear when
    /// it ends (§24, §25, §28).
    /// </summary>
    /// <remarks>
    /// The assertion that carries §24 is the pairing with <c>IsBusy</c>: the screen is busy in
    /// both the running state and the idle-review state above, and only one of them offers Stop.
    /// A control bound to <c>IsBusy</c> would pass the first half of this test and fail the
    /// other one entirely.
    /// </remarks>
    [Fact]
    public async Task Both_controls_appear_only_while_an_external_operation_is_running()
    {
        using HomeScreenHarness harness = new();
        (SessionViewModel screen, _) = await AtEnhancementAsync(harness, "stop-running.png");

        harness.Inner.FakeMeitu.SetScenario(
            FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));

        Task running = screen.RunStepCommand.ExecuteAsync(null);
        await harness.Inner.FakeMeitu.HangStarted;

        screen.IsBusy.ShouldBeTrue();
        screen.CanStopAutomation.ShouldBeTrue();
        screen.CanTakeOverAutomation.ShouldBeTrue();

        screen.StopAutomationCommand.Execute(null);
        screen.IsStopping.ShouldBeTrue();
        screen.StoppingNotice.ShouldNotBeNullOrWhiteSpace();

        await running;
        await screen.PreviewsLoaded;

        screen.CanStopAutomation.ShouldBeFalse();
        screen.CanTakeOverAutomation.ShouldBeFalse();
        screen.IsStopping.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------------------
    // §26 — the confirmation
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Opening the Take Over confirmation changes nothing (§26).
    /// </summary>
    /// <remarks>
    /// It cannot leave a trace because it has nothing to leave one with — it issues no command
    /// and reaches no service — and the assertion is that the run is still running afterwards
    /// and the session is still Active.
    /// </remarks>
    [Fact]
    public async Task Opening_the_take_over_confirmation_changes_nothing()
    {
        using HomeScreenHarness harness = new();
        (SessionViewModel screen, SessionId id) = await AtEnhancementAsync(harness, "stop-confirm.png");

        harness.Inner.FakeMeitu.SetScenario(
            FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));

        Task running = screen.RunStepCommand.ExecuteAsync(null);
        await harness.Inner.FakeMeitu.HangStarted;

        screen.BeginTakeOverCommand.Execute(null);
        screen.IsConfirmingTakeOver.ShouldBeTrue();
        screen.TakeOverConfirmQuestion.ShouldNotBeNullOrWhiteSpace();

        SessionAggregate midRun = (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        midRun.Session.State.ShouldBe(SessionState.Active);
        midRun.Session.HandedOffAtUtc.ShouldBeNull();
        midRun.Attempts.Single(a => a.Step == StepKind.Enhancement)
            .Status.ShouldBe(AttemptStatus.Running);

        // Thinking better of it leaves the run alone as well.
        screen.CancelTakeOverCommand.Execute(null);
        screen.IsConfirmingTakeOver.ShouldBeFalse();

        screen.StopAutomationCommand.Execute(null);
        await running;
    }

    /// <summary>
    /// Confirming the takeover hands the session over and records it (§17, §18, §26).
    /// </summary>
    [Fact]
    public async Task Confirming_take_over_hands_the_session_to_the_operator()
    {
        using HomeScreenHarness harness = new();
        (SessionViewModel screen, SessionId id) = await AtEnhancementAsync(harness, "stop-takeover.png");

        harness.Inner.FakeMeitu.SetScenario(
            FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));

        Task running = screen.RunStepCommand.ExecuteAsync(null);
        await harness.Inner.FakeMeitu.HangStarted;

        screen.BeginTakeOverCommand.Execute(null);
        screen.ConfirmTakeOverCommand.Execute(null);
        screen.IsConfirmingTakeOver.ShouldBeFalse("confirming closes the panel");

        await running;
        await screen.PreviewsLoaded;

        SessionAggregate persisted = (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        persisted.Session.State.ShouldBe(SessionState.HandedOff);
        persisted.Attempts.Single(a => a.Step == StepKind.Enhancement)
            .Status.ShouldBe(AttemptStatus.Cancelled);
        persisted.Revisions.ShouldNotContain(
            r => r.Operation == OperationKind.Enhance, "a takeover produces no Revision");
    }

    // -------------------------------------------------------------------------------------
    // §21, §37 — the retained-state warning
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// After a stop that could not cancel Meitu, the screen warns about what may still be
    /// running (§21, §37).
    /// </summary>
    /// <remarks>
    /// The warning is read from the closed attempt row, so it survives the run ending. What it
    /// must not do is claim Meitu is safe — asserted here as the presence of the honest message
    /// rather than the absence of a dishonest one, with the wording itself guarded in
    /// <c>LocalisationResourceTests</c>.
    /// </remarks>
    [Fact]
    public async Task After_a_stop_the_screen_warns_about_retained_external_state()
    {
        using HomeScreenHarness harness = new();
        (SessionViewModel screen, _) = await AtEnhancementAsync(harness, "stop-retained.png");

        harness.Inner.FakeMeitu.SetScenario(
            FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));

        Task running = screen.RunStepCommand.ExecuteAsync(null);
        await harness.Inner.FakeMeitu.HangStarted;
        screen.StopAutomationCommand.Execute(null);
        await running;
        await screen.PreviewsLoaded;

        screen.HasRetainedExternalState.ShouldBeTrue();
        screen.RetainedExternalStateNotice.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>A successful run shows no retained-state warning (§21).</summary>
    [Fact]
    public async Task A_successful_run_shows_no_retained_state_warning()
    {
        using HomeScreenHarness harness = new();
        (SessionViewModel screen, _) = await AtEnhancementAsync(harness, "stop-clean.png");

        harness.Inner.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);
        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;

        screen.HasRetainedExternalState.ShouldBeFalse();
        screen.RetainedExternalStateNotice.ShouldBeNull();
    }

    // -------------------------------------------------------------------------------------
    // §22 — re-entry
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// After a takeover the screen offers explicit re-entry, and only then can a new attempt
    /// start (§22).
    /// </summary>
    /// <remarks>
    /// The order is the rule. Re-entry returns the session to Active and the step to Waiting; it
    /// starts nothing. The operator presses Run Step afterwards, and <i>that</i> is what produces
    /// the new attempt against a fresh working copy.
    /// </remarks>
    [Fact]
    public async Task Re_entry_is_offered_after_a_take_over_and_starts_nothing_by_itself()
    {
        using HomeScreenHarness harness = new();
        (SessionViewModel screen, SessionId id) = await AtEnhancementAsync(harness, "stop-reentry.png");

        harness.Inner.FakeMeitu.SetScenario(
            FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));

        Task running = screen.RunStepCommand.ExecuteAsync(null);
        await harness.Inner.FakeMeitu.HangStarted;
        screen.BeginTakeOverCommand.Execute(null);
        screen.ConfirmTakeOverCommand.Execute(null);
        await running;
        await screen.PreviewsLoaded;

        screen.CanReenterAutomation.ShouldBeTrue();
        screen.ReenterAutomationLabel.ShouldNotBeNullOrWhiteSpace();

        await screen.ReenterAutomationCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;
        screen.Notice.ShouldBeNull();

        SessionAggregate afterReentry = (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        afterReentry.Session.State.ShouldBe(SessionState.Active);
        afterReentry.Attempts.Count(a => a.Step == StepKind.Enhancement)
            .ShouldBe(1, "re-entry itself starts no attempt");
        screen.CanReenterAutomation.ShouldBeFalse();

        harness.Inner.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);
        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;
        screen.Notice.ShouldBeNull();

        SessionAggregate afterRun = (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        afterRun.Attempts.Count(a => a.Step == StepKind.Enhancement).ShouldBe(2);
        afterRun.Attempts.Count(a => a.Step == StepKind.Enhancement && a.Status == AttemptStatus.Cancelled)
            .ShouldBe(1, "the handed-off attempt stays exactly as it was");
    }

    // -------------------------------------------------------------------------------------
    // §27 — the two controls are distinguishable
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Stop and Take Over carry different wording and neither offers to close Meitu (§27).
    /// </summary>
    [Fact]
    public async Task The_two_controls_are_worded_as_different_actions()
    {
        using HomeScreenHarness harness = new();
        (SessionViewModel screen, _) = await AtEnhancementAsync(harness, "stop-wording.png");

        screen.StopLabel.ShouldNotBe(screen.TakeOverLabel);

        // The hint carries both halves, because §27 asks the operator to understand the
        // *distinction* and each half read alone is easy to mistake for the other.
        screen.StopHint.ShouldContain("safely", Case.Insensitive);
        screen.StopHint.ShouldContain("leaves Meitu as it is", Case.Insensitive);

        // Neither offers to close Meitu. Note that saying "Meitu is not closed" is the opposite
        // of offering to — so the assertion is about the promise, not the word.
        screen.StopHint.ShouldContain("Meitu is not closed", Case.Insensitive);
        screen.StopHint.ShouldNotContain("closes Meitu", Case.Insensitive);
        screen.TakeOverConfirmQuestion.ShouldNotContain("closes Meitu", Case.Insensitive);
        screen.TakeOverConfirmQuestion.ShouldContain("not closed", Case.Insensitive);
    }

    // -------------------------------------------------------------------------------------
    // §37 — the representative states actually render
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Every state this slice adds renders at 1000×700 with no binding errors (§37).
    /// </summary>
    /// <remarks>
    /// A mistyped binding path is silent at compile time and silent at run time — WPF writes a
    /// trace line and shows an empty control — so a Stop button bound to a property that does
    /// not exist would be an invisible button on the screen that stops Meitu. The three states
    /// are rendered separately because they are three different branches of the panel: the
    /// controls, the confirmation, and the after-the-fact warning plus re-entry.
    /// <para>
    /// The run is stopped before each render because rendering costs the view model something
    /// permanent (see <c>SessionSmokeTests.SessionScreen</c>), so a screen is rendered once and
    /// then not driven again.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_running_stop_controls_render_at_the_review_viewport()
    {
        using HomeScreenHarness harness = new();
        (SessionViewModel screen, SessionId id) = await AtEnhancementAsync(harness, "stop-render-running.png");

        harness.Inner.FakeMeitu.SetScenario(
            FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));

        Task running = screen.RunStepCommand.ExecuteAsync(null);
        await harness.Inner.FakeMeitu.HangStarted;

        SessionViewModel rendered = await SecondScreenAsync(harness, id);
        rendered.CanStopAutomation.ShouldBeTrue(
            "a screen opened mid-run reads the live runtime, not a cached view");
        rendered.CanTakeOverAutomation.ShouldBeTrue();

        WpfRendering.RenderExpectingNoBindingErrors(
            () => new SessionScreenView { DataContext = rendered }, WpfRendering.ReviewViewport);

        screen.StopAutomationCommand.Execute(null);
        await running;
    }

    /// <summary>The Take Over confirmation renders (§26, §37).</summary>
    [Fact]
    public async Task The_take_over_confirmation_renders_at_the_review_viewport()
    {
        using HomeScreenHarness harness = new();
        (SessionViewModel screen, SessionId id) = await AtEnhancementAsync(harness, "stop-render-confirm.png");

        harness.Inner.FakeMeitu.SetScenario(
            FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));

        Task running = screen.RunStepCommand.ExecuteAsync(null);
        await harness.Inner.FakeMeitu.HangStarted;

        SessionViewModel rendered = await SecondScreenAsync(harness, id);
        rendered.BeginTakeOverCommand.Execute(null);
        rendered.IsConfirmingTakeOver.ShouldBeTrue();

        WpfRendering.RenderExpectingNoBindingErrors(
            () => new SessionScreenView { DataContext = rendered }, WpfRendering.ReviewViewport);

        screen.StopAutomationCommand.Execute(null);
        await running;
    }

    /// <summary>
    /// The retained-state warning and the re-entry offer render together (§21, §22, §37).
    /// </summary>
    [Fact]
    public async Task The_handed_off_state_renders_at_the_review_viewport()
    {
        using HomeScreenHarness harness = new();
        (SessionViewModel screen, SessionId id) = await AtEnhancementAsync(harness, "stop-render-handedoff.png");

        harness.Inner.FakeMeitu.SetScenario(
            FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));

        Task running = screen.RunStepCommand.ExecuteAsync(null);
        await harness.Inner.FakeMeitu.HangStarted;
        screen.BeginTakeOverCommand.Execute(null);
        screen.ConfirmTakeOverCommand.Execute(null);
        await running;
        await screen.PreviewsLoaded;

        SessionViewModel rendered = await SecondScreenAsync(harness, id);
        rendered.HasRetainedExternalState.ShouldBeTrue();
        rendered.CanReenterAutomation.ShouldBeTrue();

        WpfRendering.RenderExpectingNoBindingErrors(
            () => new SessionScreenView { DataContext = rendered }, WpfRendering.ReviewViewport);
    }

    /// <summary>
    /// A throwaway screen on the session's current state, opened purely to be rendered.
    /// </summary>
    /// <remarks>
    /// Rendering costs a view model something permanent: an <c>ItemsControl</c> bound to its
    /// preview panes creates a <c>CollectionView</c> owned by the render thread's dispatcher,
    /// and that thread is gone by the time the next command would touch the collection. Only
    /// tests hit this — the application renders on the thread it drives from — so the pattern
    /// here is the one <c>SessionSmokeTests</c> already uses: render a screen that will not be
    /// driven again.
    /// <para>
    /// Opening it mid-run also proves something worth proving: the Stop controls come from the
    /// live runtime rather than from whatever a screen happened to be holding, so a screen that
    /// did not start the run still offers them.
    /// </para>
    /// </remarks>
    private static async Task<SessionViewModel> SecondScreenAsync(
        HomeScreenHarness harness, SessionId id)
    {
        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open((await harness.Sessions.LoadAsync(id, CancellationToken.None)).Value);
        await screen.PreviewsLoaded;
        return screen;
    }

    // -------------------------------------------------------------------------------------

    /// <summary>Imports a session and confirms the original, leaving the screen on Enhancement.</summary>
    private static async Task<Opened> AtEnhancementAsync(
        HomeScreenHarness harness, string fileName)
    {
        harness.FilePicker.Path = harness.Inner.WriteSourcePng(fileName);
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionView imported = harness.Navigation.WorkflowSelectionFor!;
        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open(imported);

        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;
        screen.Notice.ShouldBeNull();

        return new Opened(screen, imported.Id);
    }

    /// <summary>A screen sitting on Enhancement, with the session id its assertions need.</summary>
    private sealed record Opened(SessionViewModel Screen, SessionId Id);
}
