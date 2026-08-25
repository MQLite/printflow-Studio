using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Automation;

/// <summary>
/// The guarded Enhancement sequence: identity, then the one invocation, then Busy, then
/// positively evidenced completion, then identity again
/// (Epic 11300 Part B2A §5, §6, §10–§15, §22, §24, §25, §26).
/// </summary>
/// <remarks>
/// The fake Meitu here is a state machine rather than a fixed screen, because everything worth
/// proving about this route is a <i>sequence</i>: that the Save probe happens before the action
/// and is cancelled before the action, that exactly one Enhancement invocation is produced, and
/// that a run which never reaches a positively evidenced completion fails rather than returning
/// a success carrying "unknown".
///
/// Every refusal asserts that the Enhancement control does not appear in
/// <see cref="RecordingUiElementProvider.Invocations"/>. A failure code proves PrintFlow
/// reported a problem; a zero invocation count proves it did not start an irreversible
/// operation and report the problem afterwards.
/// </remarks>
public sealed class GuardedMeituEnhancementTests
{
    private const string ExpectedFile = "PF_B2A_A.png";
    private const string ExpectedIdentity = "PF_B2A_A_副本";
    private const string ModuleId = MeituFakes.ModuleAutomationId;
    private const string SaveId = "MainWindow.editorPage.saveButton";
    private const string CloseId = "MainWindow.editorPage.closeButton";
    private const string CancelId = "MainWindow.titleFrame.closeButton";
    private const string FileNameId = "MainWindow.wName.fileNameEdit";

    private static readonly MeituAutomationOptions FastOptions = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(5),
        ActivationTimeout = TimeSpan.FromMilliseconds(40),
        DialogTimeout = TimeSpan.FromMilliseconds(40),
        EnhancementBusyTimeout = TimeSpan.FromMilliseconds(120),
        EnhancementCompletionTimeout = TimeSpan.FromMilliseconds(200),
    };

    /// <summary>
    /// A fake Meitu that behaves like the observed one: a Save surface that opens and cancels,
    /// and an Enhancement control that starts work and later finishes it.
    /// </summary>
    private sealed class Scenario
    {
        public required FakeWindowLocator Locator { get; init; }

        public required RecordingUiElementProvider Elements { get; init; }

        public required RecordingInputSink Input { get; init; }

        public required GuardedMeituUiDriver Driver { get; init; }

        public required MeituTarget Editor { get; init; }

        /// <summary>
        /// What the editor shows on each successive read <i>after</i> the Enhancement control is
        /// invoked. The last state reached persists.
        /// </summary>
        /// <remarks>
        /// A queue advanced by reads rather than a countdown, because the identity probe reads
        /// the screen several times before the action is invoked and a countdown would be spent
        /// on those. Scripting the states this way also lets a test say exactly what Meitu does
        /// next — including "Busy stops and nothing positive replaces it", which has no natural
        /// expression as a duration.
        /// </remarks>
        public Queue<string[]> AfterInvoke { get; } = new();

        public bool Invoked { get; set; }

        /// <summary>How many times the Enhancement control itself was invoked.</summary>
        public int EnhancementInvocations => Elements.Invocations.Count(i => i == ModuleId);

        public int Invocations(string automationId) =>
            Elements.Invocations.Count(i => i == automationId);

        /// <summary>Rewrites the automation names the editor window reports.</summary>
        public void Show(params string[] texts) => Elements.SetTexts(Editor.Window.Handle, texts);
    }

    /// <summary>The editor while an Enhancement is running: the panel exists and so does progress.</summary>
    private static string[] BusyScreen() =>
        [.. MeituFakes.EditorMarkers, .. MeituFakes.CompletionMarkers, "ENH-RUNNING", "ENH-ABORT"];

    /// <summary>The editor after an Enhancement: the panel remains, the progress is gone.</summary>
    private static string[] CompletedScreen() => MeituFakes.CompletedTexts();

    /// <summary>The editor with no Enhancement module selected.</summary>
    private static string[] IdleScreen() => [.. MeituFakes.EditorMarkers];

    /// <summary>
    /// Builds the fake Meitu.
    /// </summary>
    /// <param name="identityValue">What the Save surface reports as the document-derived name.</param>
    /// <param name="afterInvoke">
    /// The screens Meitu shows on successive reads after the action is invoked. The default is
    /// the observed sequence: Busy twice, then the settled panel.
    /// </param>
    /// <param name="raiseSaveSurface">
    /// Whether invoking Save presents the signed surface. False models a Save that writes
    /// immediately, which is unsafe to probe with.
    /// </param>
    /// <param name="initialScreen">What the editor shows before anything is invoked.</param>
    private static Scenario Build(
        string identityValue = ExpectedIdentity,
        string[][]? afterInvoke = null,
        bool raiseSaveSurface = true,
        bool enhancementPresent = true,
        MeituBaseline? baseline = null,
        string[]? initialScreen = null)
    {
        ExternalProcessRef process = MeituFakes.Process();
        ExternalWindowRef editorWindow = MeituFakes.Window(title: MeituFakes.EditorTitle);
        ExternalWindowRef saveSurface = MeituFakes.Window(
            handle: 0x6000, owningProcessId: process.ProcessId, title: "Form", className: "QtSaveDialog");

        FakeWindowLocator locator = new();
        RecordingUiElementProvider elements = new();
        RecordingInputSink input = new();

        locator.Register(process, editorWindow);
        locator.PutInForeground(editorWindow);

        elements.SetTexts(editorWindow.Handle, initialScreen ?? IdleScreen());
        elements.AddEditorSaveControl(editorWindow.Handle, process.ProcessId);
        elements.AddEditorCloseControl(editorWindow.Handle, process.ProcessId);
        if (enhancementPresent)
        {
            elements.AddEnhancementAction(editorWindow.Handle, processId: process.ProcessId);
        }

        elements.AddIdentityDialogControl(
            saveSurface.Handle, FileNameId, "", "Edit", "QLineEdit", UiPatternKind.Value, process.ProcessId);
        elements.AddIdentityDialogControl(
            saveSurface.Handle, CancelId, "\uE0E6", "Button", "IconFontButton",
            UiPatternKind.Invoke, process.ProcessId);
        elements.SetReadValue(FileNameId, identityValue);

        Scenario scenario = new()
        {
            Locator = locator,
            Elements = elements,
            Input = input,
            Driver = new GuardedMeituUiDriver(
                locator, elements, input, new RecordingEvidenceSink(),
                new StubMeituBaselineProvider(baseline ?? MeituFakes.Baseline()),
                FastOptions,
                TimeProvider.System),
            Editor = new MeituTarget(process, editorWindow),
        };

        foreach (string[] screen in afterInvoke ?? [BusyScreen(), BusyScreen(), CompletedScreen()])
        {
            scenario.AfterInvoke.Enqueue(screen);
        }

        elements.OnInvoke = invoked =>
        {
            if (invoked == SaveId && raiseSaveSurface)
            {
                locator.OwnedDialogs.Add(saveSurface);
                locator.PutInForeground(saveSurface);
            }
            else if (invoked == CancelId)
            {
                locator.OwnedDialogs.Remove(saveSurface);

                // The observed behaviour: Meitu hands the foreground to a short-lived window of
                // its own rather than back to the editor, so the driver has to reacquire it.
                locator.Foreground = new ForegroundIdentity(
                    new WindowHandle(0x9999), process.ProcessId, "XiuXiu");
            }
            else if (invoked == ModuleId)
            {
                scenario.Invoked = true;
            }
        };

        elements.OnReadTextSnapshot = _ =>
        {
            if (scenario.Invoked && scenario.AfterInvoke.Count > 0)
            {
                scenario.Show(scenario.AfterInvoke.Dequeue());
            }
        };

        return scenario;
    }

    // -----------------------------------------------------------------------------
    // The whole sequence, once (§16, §22)
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task Expected_A_observed_A_runs_Enhancement_once_and_reports_every_observation()
    {
        Scenario s = Build();

        OperationResult<MeituEnhancementOutcome> run =
            await s.Driver.RunEnhancementAsync(s.Editor, ExpectedFile, CancellationToken.None);

        run.IsSuccess.ShouldBeTrue(run.IsFailure ? run.Failure.TechnicalDetail : string.Empty);
        run.Value.ObservedDocumentIdentity.ShouldBe(ExpectedIdentity);
        run.Value.IdentityBeforeEnhancement.State
            .ShouldBe(MeituStartingState.KnownEditorWithExpectedWorkingCopy);
        run.Value.Busy.State.ShouldBe(MeituStartingState.Busy);
        run.Value.Completion.State.ShouldBe(MeituStartingState.KnownEditorWithExpectedWorkingCopy);
        run.Value.IdentityAfterEnhancement.State
            .ShouldBe(MeituStartingState.KnownEditorWithExpectedWorkingCopy);

        s.EnhancementInvocations.ShouldBe(1);
        s.Input.Sends.ShouldBeEmpty();
    }

    /// <summary>
    /// The Save surface is closed before the Enhancement control is touched.
    /// </summary>
    /// <remarks>
    /// §22 asks for this explicitly, and the ordering is checkable rather than a matter of
    /// reading the code: the recorded invocation list must show Save, then Cancel, then the
    /// module — with Cancel strictly before the module.
    /// </remarks>
    [Fact]
    public async Task The_Save_surface_is_cancelled_before_the_Enhancement_target_is_interacted_with()
    {
        Scenario s = Build();

        await s.Driver.RunEnhancementAsync(s.Editor, ExpectedFile, CancellationToken.None);

        int save = s.Elements.Invocations.IndexOf(SaveId);
        int cancel = s.Elements.Invocations.IndexOf(CancelId);
        int module = s.Elements.Invocations.IndexOf(ModuleId);

        save.ShouldBeGreaterThanOrEqualTo(0);
        cancel.ShouldBeGreaterThan(save);
        module.ShouldBeGreaterThan(cancel);
    }

    /// <summary>Nothing is saved, exported, renamed or written anywhere in the route.</summary>
    [Fact]
    public async Task Nothing_is_saved_exported_or_written_during_the_run()
    {
        Scenario s = Build();

        OperationResult<MeituEnhancementOutcome> run =
            await s.Driver.RunEnhancementAsync(s.Editor, ExpectedFile, CancellationToken.None);

        run.IsSuccess.ShouldBeTrue();
        s.Elements.ValueWrites.ShouldBeEmpty();
        s.Invocations(CloseId).ShouldBe(0);
        s.EnhancementInvocations.ShouldBe(1);
    }

    // -----------------------------------------------------------------------------
    // Identity before the action (§22)
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task Expected_A_observed_B_produces_zero_Enhancement_invocations()
    {
        Scenario s = Build(identityValue: "PF_B2A_B_副本");

        OperationResult<MeituEnhancementOutcome> run =
            await s.Driver.RunEnhancementAsync(s.Editor, ExpectedFile, CancellationToken.None);

        run.IsFailure.ShouldBeTrue();
        run.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
        run.Failure.Context["observedIdentity"].ShouldBe("PF_B2A_B_副本");
        s.EnhancementInvocations.ShouldBe(0);
    }

    /// <summary>A Save that writes instead of prompting cannot be probed with, so nothing runs.</summary>
    [Fact]
    public async Task An_identity_probe_that_never_presents_the_signed_surface_stops_before_Enhancement()
    {
        Scenario s = Build(raiseSaveSurface: false);

        OperationResult<MeituEnhancementOutcome> run =
            await s.Driver.RunEnhancementAsync(s.Editor, ExpectedFile, CancellationToken.None);

        run.IsFailure.ShouldBeTrue();
        s.EnhancementInvocations.ShouldBe(0);
    }

    /// <summary>
    /// No signed Enhancement evidence means nothing happens at all — not even the Save probe.
    /// </summary>
    /// <remarks>
    /// Checked before the probe on purpose. A chain that vouches for no Enhancement route should
    /// not cost the operator a Save surface opening and closing on their screen before PrintFlow
    /// admits it cannot proceed.
    /// </remarks>
    [Fact]
    public async Task Without_signed_Enhancement_evidence_no_Save_probe_is_even_attempted()
    {
        Scenario s = Build(baseline: MeituFakes.BaselineWithout(enhancement: true));

        OperationResult<MeituEnhancementOutcome> run =
            await s.Driver.RunEnhancementAsync(s.Editor, ExpectedFile, CancellationToken.None);

        run.IsFailure.ShouldBeTrue();
        run.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
        s.Elements.Invocations.ShouldBeEmpty();
        s.Elements.ValueReads.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // Target loss and ownership (§10, §18, §21, §26)
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task Losing_the_foreground_to_another_process_produces_zero_Enhancement_invocations()
    {
        Scenario s = Build();
        s.Locator.RefuseActivation = true;
        s.Locator.Foreground = new ForegroundIdentity(new WindowHandle(0xDEAD), 999, "explorer");

        OperationResult<MeituEnhancementOutcome> run =
            await s.Driver.RunEnhancementAsync(s.Editor, ExpectedFile, CancellationToken.None);

        run.IsFailure.ShouldBeTrue();
        run.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        run.Failure.Context["inputSent"].ShouldBe("false");
        s.Elements.Invocations.ShouldBeEmpty();
        s.Input.Sends.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_window_that_has_changed_owner_produces_zero_Enhancement_invocations()
    {
        Scenario s = Build();
        s.Locator.Replace(s.Editor.Process, s.Editor.Window with { OwningProcessId = 9999 });

        OperationResult<MeituEnhancementOutcome> run =
            await s.Driver.RunEnhancementAsync(s.Editor, ExpectedFile, CancellationToken.None);

        run.IsFailure.ShouldBeTrue();
        run.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        s.Elements.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_process_that_exits_produces_zero_Enhancement_invocations()
    {
        Scenario s = Build();
        s.Locator.DeadProcessIds.Add(s.Editor.Process.ProcessId);

        OperationResult<MeituEnhancementOutcome> run =
            await s.Driver.RunEnhancementAsync(s.Editor, ExpectedFile, CancellationToken.None);

        run.IsFailure.ShouldBeTrue();
        run.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        s.Elements.Invocations.ShouldBeEmpty();
    }

    /// <summary>An Enhancement control that is not in the tree is a refusal, not a guess.</summary>
    [Fact]
    public async Task A_missing_Enhancement_control_stops_after_the_probe_and_invokes_nothing_further()
    {
        Scenario s = Build(enhancementPresent: false);

        OperationResult<MeituEnhancementOutcome> run =
            await s.Driver.RunEnhancementAsync(s.Editor, ExpectedFile, CancellationToken.None);

        run.IsFailure.ShouldBeTrue();
        run.Failure.Context["inputSent"].ShouldBe("false");
        s.EnhancementInvocations.ShouldBe(0);

        // The probe ran and cancelled cleanly; only the Enhancement step refused.
        s.Invocations(CancelId).ShouldBe(1);
        s.Locator.OwnedDialogs.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // The toggle hazard (§10)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A module that is already selected is never toggled off.
    /// </summary>
    /// <remarks>
    /// The live workstation produced this twice. The control is a Qt <c>CheckBox</c> that Meitu
    /// uses as a module selector, and Meitu remembers the selection across document loads — so
    /// "invoke the Enhancement control" can mean "cancel the Enhancement someone else started".
    /// The completion signature matching <i>before</i> the invocation is exactly the evidence
    /// that the module is already selected.
    /// </remarks>
    [Fact]
    public async Task An_already_selected_Enhancement_module_is_not_toggled_off()
    {
        Scenario s = Build(initialScreen: CompletedScreen(), afterInvoke: []);

        OperationResult<MeituEnhancementOutcome> run =
            await s.Driver.RunEnhancementAsync(s.Editor, ExpectedFile, CancellationToken.None);

        run.IsFailure.ShouldBeTrue();
        run.Failure.Context["phase"].ShouldBe("Complete");
        run.Failure.Context["inputSent"].ShouldBe("false");
        run.Failure.TechnicalDetail.ShouldContain("deselect it");
        s.EnhancementInvocations.ShouldBe(0);
    }

    /// <summary>
    /// A document Meitu is still computing over reports that, not a filename mismatch.
    /// </summary>
    /// <remarks>
    /// Regression test for a reporting defect the first live B2A smoke produced. Meitu starts
    /// an enhancement by itself when a document is opened while a module is still selected, so
    /// the identity probe read the exactly correct derived name and the classifier still
    /// declined — because Busy outranks the document state. The failure said the Save value did
    /// not identify the file, which sent the reader looking for a filename problem that did not
    /// exist. The observed value is now reported alongside the state that actually refused.
    /// </remarks>
    [Fact]
    public async Task A_Busy_editor_reports_Busy_rather_than_an_identity_mismatch()
    {
        Scenario s = Build(initialScreen: BusyScreen(), afterInvoke: []);

        OperationResult<MeituStateSnapshot> confirmed = await s.Driver.ConfirmWorkingCopyIdentityAsync(
            s.Editor, ExpectedFile, CancellationToken.None);

        confirmed.IsFailure.ShouldBeTrue();
        confirmed.Failure.Context["state"].ShouldBe("Busy");
        confirmed.Failure.Context["observedIdentity"].ShouldBe(ExpectedIdentity);
        confirmed.Failure.TechnicalDetail.ShouldContain("computing");
        confirmed.Failure.TechnicalDetail.ShouldNotContain("did not exactly identify");
        confirmed.Failure.IsRetryable.ShouldBeTrue();
        s.EnhancementInvocations.ShouldBe(0);
    }

    /// <summary>
    /// An Enhancement already running is never started a second time.
    /// </summary>
    /// <remarks>
    /// Refused by the classifier before the route's own guard is reached: an editor showing the
    /// signed Busy markers classifies as <see cref="MeituStartingState.Busy"/>, which is not the
    /// expected-working-copy state the invocation requires.
    /// </remarks>
    [Fact]
    public async Task An_Enhancement_already_in_flight_is_not_started_again()
    {
        Scenario s = Build(initialScreen: BusyScreen(), afterInvoke: []);

        OperationResult<MeituEnhancementOutcome> run =
            await s.Driver.RunEnhancementAsync(s.Editor, ExpectedFile, CancellationToken.None);

        run.IsFailure.ShouldBeTrue();
        s.EnhancementInvocations.ShouldBe(0);
    }

    // -----------------------------------------------------------------------------
    // Bounded waits, and never Ok(Unknown) (§17, §24, §26)
    // -----------------------------------------------------------------------------

    /// <summary>An action that produces no observable work fails rather than being assumed to have run.</summary>
    [Fact]
    public async Task An_Enhancement_that_never_shows_Busy_fails_within_the_bounded_budget()
    {
        Scenario s = Build(afterInvoke: []);

        OperationResult<MeituEnhancementOutcome> run =
            await s.Driver.RunEnhancementAsync(s.Editor, ExpectedFile, CancellationToken.None);

        run.IsFailure.ShouldBeTrue();
        run.Failure.Context["wantedPhase"].ShouldBe("Busy");
        run.Failure.Context["lastPhase"].ShouldBe("Unobserved");
        run.Failure.Context["exported"].ShouldBe("false");
        run.Failure.Context["revisionCreated"].ShouldBe("false");
        s.EnhancementInvocations.ShouldBe(1);
    }

    /// <summary>
    /// Work that never ends is a structured timeout, never a success carrying "unknown".
    /// </summary>
    /// <remarks>
    /// §24 rules out <c>Ok(Unknown)</c> by name. The route could have returned the last snapshot
    /// it saw, so the test states that it does not.
    /// </remarks>
    [Fact]
    public async Task Busy_that_never_ends_times_out_rather_than_returning_an_unknown_success()
    {
        Scenario s = Build(afterInvoke: [BusyScreen()]);

        OperationResult<MeituEnhancementOutcome> run =
            await s.Driver.RunEnhancementAsync(s.Editor, ExpectedFile, CancellationToken.None);

        run.IsFailure.ShouldBeTrue();
        run.Failure.Context["wantedPhase"].ShouldBe("Complete");
        run.Failure.Context["lastPhase"].ShouldBe("Busy");
        run.Failure.Context["exported"].ShouldBe("false");
        s.EnhancementInvocations.ShouldBe(1);
    }

    /// <summary>
    /// Busy stopping without positive completion evidence is not completion.
    /// </summary>
    /// <remarks>
    /// The end-to-end form of the rule the unit tests pin down. Meitu shows Busy, then drops it
    /// and puts nothing positive in its place; the route keeps waiting and reports a timeout
    /// whose last observed phase is <c>Unobserved</c> — never a completed enhancement.
    /// </remarks>
    [Fact]
    public async Task Busy_ending_with_no_positive_completion_evidence_is_not_treated_as_complete()
    {
        Scenario s = Build(afterInvoke: [BusyScreen(), BusyScreen(), IdleScreen()]);

        OperationResult<MeituEnhancementOutcome> run =
            await s.Driver.RunEnhancementAsync(s.Editor, ExpectedFile, CancellationToken.None);

        run.IsFailure.ShouldBeTrue();
        run.Failure.Context["wantedPhase"].ShouldBe("Complete");
        run.Failure.Context["lastPhase"].ShouldBe("Unobserved");
        s.EnhancementInvocations.ShouldBe(1);
    }

    [Fact]
    public async Task Cancellation_during_observation_stops_the_run_and_sends_nothing_further()
    {
        Scenario s = Build(afterInvoke: [BusyScreen()]);
        using CancellationTokenSource cancellation = new();

        int reads = 0;
        s.Elements.OnReadTextSnapshot = _ =>
        {
            if (s.Invoked && ++reads >= 2)
            {
                cancellation.Cancel();
            }
        };

        await Should.ThrowAsync<OperationCanceledException>(
            () => s.Driver.RunEnhancementAsync(s.Editor, ExpectedFile, cancellation.Token));

        s.EnhancementInvocations.ShouldBe(1);
        s.Input.Sends.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // Modal behaviour during observation (§19)
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task A_Meitu_owned_modal_during_observation_stops_the_run_without_touching_it()
    {
        Scenario s = Build(afterInvoke: [BusyScreen()]);

        ExternalWindowRef prompt = MeituFakes.Window(
            handle: 0x7000,
            owningProcessId: s.Editor.Process.ProcessId,
            title: "温馨提示",
            className: "QtSaveDialog");

        int reads = 0;
        s.Elements.OnReadTextSnapshot = _ =>
        {
            if (s.Invoked && ++reads == 2)
            {
                s.Locator.OwnedDialogs.Add(prompt);
            }
        };

        OperationResult<MeituEnhancementOutcome> run =
            await s.Driver.RunEnhancementAsync(s.Editor, ExpectedFile, CancellationToken.None);

        run.IsFailure.ShouldBeTrue();
        run.Failure.Code.ShouldBe(FailureCode.MeituBlockingDialog);
        run.Failure.Context["inputSent"].ShouldBe("false");
        s.EnhancementInvocations.ShouldBe(1);

        // The dialog was read about and left alone.
        s.Locator.OwnedDialogs.ShouldContain(prompt);
    }

    // -----------------------------------------------------------------------------
    // Identity after completion (§15, §25)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The document changing identity under the run is a B2A failure, and claims nothing.
    /// </summary>
    /// <remarks>
    /// §25 requires expected A / observed B after completion to fail. The interesting part is
    /// what the failure must <i>not</i> contain: the enhancement really did run and really did
    /// complete, and it is still not a result anyone may build on, because PrintFlow can no
    /// longer say which document it happened to.
    /// </remarks>
    [Fact]
    public async Task A_document_whose_identity_changes_after_completion_fails_the_run()
    {
        Scenario s = Build();

        Action<string>? previous = s.Elements.OnInvoke;
        s.Elements.OnInvoke = invoked =>
        {
            previous?.Invoke(invoked);
            if (invoked == SaveId && s.Invoked)
            {
                s.Elements.SetReadValue(FileNameId, "PF_B2A_B_副本");
            }
        };

        OperationResult<MeituEnhancementOutcome> run =
            await s.Driver.RunEnhancementAsync(s.Editor, ExpectedFile, CancellationToken.None);

        run.IsFailure.ShouldBeTrue();
        run.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
        run.Failure.Context["observedIdentity"].ShouldBe("PF_B2A_B_副本");
        s.EnhancementInvocations.ShouldBe(1);
    }

    /// <summary>The post-completion probe cancels its Save surface too, leaving nothing blocking.</summary>
    [Fact]
    public async Task The_post_completion_probe_cancels_its_own_Save_surface()
    {
        Scenario s = Build();

        OperationResult<MeituEnhancementOutcome> run =
            await s.Driver.RunEnhancementAsync(s.Editor, ExpectedFile, CancellationToken.None);

        run.IsSuccess.ShouldBeTrue();
        s.Invocations(SaveId).ShouldBe(2);
        s.Invocations(CancelId).ShouldBe(2);
        s.Locator.OwnedDialogs.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // Closing the document (§2, §19, §28)
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task Closing_the_document_reaches_the_signed_empty_editor()
    {
        Scenario s = Build(afterInvoke: []);
        s.Elements.OnInvoke = invoked =>
        {
            if (invoked == CloseId)
            {
                s.Show([.. MeituFakes.EmptyEditorMarkers]);
            }
        };

        OperationResult<MeituTarget> closed =
            await s.Driver.CloseDocumentAsync(s.Editor, CancellationToken.None);

        closed.IsSuccess.ShouldBeTrue(closed.IsFailure ? closed.Failure.TechnicalDetail : string.Empty);
        s.Invocations(CloseId).ShouldBe(1);
    }

    /// <summary>
    /// A close that raises a prompt stops, and says the working file must not be deleted.
    /// </summary>
    /// <remarks>
    /// The observed behaviour after an Enhancement: Meitu asks whether to save the modified
    /// image. PrintFlow does not answer it, and the failure carries the consequence for the
    /// caller — Meitu may still hold the file, so §28's cleanup permission has not been earned.
    /// </remarks>
    [Fact]
    public async Task A_close_that_raises_a_prompt_stops_and_forbids_deleting_the_working_file()
    {
        Scenario s = Build(afterInvoke: []);
        s.Elements.OnInvoke = invoked =>
        {
            if (invoked == CloseId)
            {
                s.Locator.OwnedDialogs.Add(MeituFakes.Window(
                    handle: 0x7000,
                    owningProcessId: s.Editor.Process.ProcessId,
                    title: "温馨提示",
                    className: "QtSaveDialog"));
            }
        };

        OperationResult<MeituTarget> closed =
            await s.Driver.CloseDocumentAsync(s.Editor, CancellationToken.None);

        closed.IsFailure.ShouldBeTrue();
        closed.Failure.Code.ShouldBe(FailureCode.MeituBlockingDialog);
        closed.Failure.TechnicalDetail.ShouldContain("retained");
        s.Invocations(CloseId).ShouldBe(1);
    }

    [Fact]
    public async Task Without_signed_close_evidence_nothing_is_invoked()
    {
        Scenario s = Build(baseline: MeituFakes.BaselineWithout(closeDocument: true), afterInvoke: []);

        OperationResult<MeituTarget> closed =
            await s.Driver.CloseDocumentAsync(s.Editor, CancellationToken.None);

        closed.IsFailure.ShouldBeTrue();
        closed.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
        s.Elements.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_close_that_never_reaches_the_empty_editor_forbids_deleting_the_working_file()
    {
        Scenario s = Build(afterInvoke: []);

        OperationResult<MeituTarget> closed =
            await s.Driver.CloseDocumentAsync(s.Editor, CancellationToken.None);

        closed.IsFailure.ShouldBeTrue();
        closed.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
        closed.Failure.TechnicalDetail.ShouldContain("must not be deleted");
        s.Invocations(CloseId).ShouldBe(1);
    }
}
