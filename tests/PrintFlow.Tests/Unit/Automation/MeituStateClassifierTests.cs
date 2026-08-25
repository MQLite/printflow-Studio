using System.Collections.Immutable;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Unit.Automation;

/// <summary>
/// The starting-state rules, exercised as pure functions (Epic 11300 Part A §12, §13).
/// </summary>
/// <remarks>
/// No window, no process and no screen appears in this file, which is the point of having split
/// observation from classification: the rules that decide whether PrintFlow is allowed to touch
/// Meitu at all are the ones that most need exhaustive tests, and they are the ones that would
/// otherwise be hardest to write.
/// </remarks>
public sealed class MeituStateClassifierTests
{
    private static MeituObservation Observe(
        string title = "美图秀秀",
        IEnumerable<string>? texts = null,
        IEnumerable<string>? dialogs = null,
        bool enabled = true,
        string? expectedFile = null,
        string? observedIdentity = null) =>
        new(title,
            [.. texts ?? []],
            [.. dialogs ?? []],
            enabled,
            expectedFile,
            observedIdentity);

    private static MeituStateSnapshot Classify(MeituObservation observation) =>
        MeituStateClassifier.Classify(MeituFakes.Baseline(), observation);

    [Fact]
    public void The_signed_welcome_markers_identify_the_clean_start_page()
    {
        MeituStateSnapshot snapshot = Classify(Observe(texts: MeituFakes.WelcomeMarkers));

        snapshot.State.ShouldBe(MeituStartingState.KnownWelcome);
        snapshot.IsSafeStartingState.ShouldBeTrue();
        snapshot.MatchedMarkers.Length.ShouldBe(MeituFakes.WelcomeMarkers.Length);
    }

    /// <summary>
    /// One marker is not "multiple markers".
    /// </summary>
    /// <remarks>
    /// Epic 11000's recognition rule requires several stable markers precisely because a single
    /// coincidental label — a menu item, a tooltip, a recently-used entry — proves nothing about
    /// which screen is showing.
    /// </remarks>
    [Fact]
    public void A_single_coincidental_marker_does_not_make_a_welcome_page()
    {
        Classify(Observe(texts: ["图片编辑"])).State.ShouldBe(MeituStartingState.Unknown);
    }

    [Fact]
    public void Exactly_the_minimum_number_of_markers_is_enough()
    {
        ImmutableArray<string> justEnough = [.. MeituFakes.WelcomeMarkers.Take(MeituStateClassifier.MinimumWelcomeMarkers)];

        Classify(Observe(texts: justEnough)).State.ShouldBe(MeituStartingState.KnownWelcome);
        Classify(Observe(texts: justEnough.Take(MeituStateClassifier.MinimumWelcomeMarkers - 1)))
            .State.ShouldBe(MeituStartingState.Unknown);
    }

    /// <summary>
    /// A window titled like Meitu but showing an unrecognised screen is still unrecognised.
    /// </summary>
    [Fact]
    public void An_accepted_title_alone_never_makes_a_state_safe()
    {
        MeituStateSnapshot snapshot = Classify(Observe(title: "美图秀秀", texts: ["某个未知界面"]));

        snapshot.State.ShouldBe(MeituStartingState.Unknown);
        snapshot.IsSafeStartingState.ShouldBeFalse();
    }

    /// <summary>
    /// A window in another application that copies Meitu's title is not Meitu's start page.
    /// </summary>
    /// <remarks>
    /// The classifier's contribution to this is refusing an unaccepted title outright. Its
    /// counterpart — process ownership — lives in the locator, and the two together are what
    /// make a look-alike window unusable as a target.
    /// </remarks>
    [Fact]
    public void An_unaccepted_title_is_unknown_even_with_every_marker_visible()
    {
        Classify(Observe(title: "Definitely Not Meitu", texts: MeituFakes.WelcomeMarkers))
            .State.ShouldBe(MeituStartingState.Unknown);
    }

    /// <summary>
    /// The editor is never mistaken for the start page, however much of the start page it shows.
    /// </summary>
    /// <remarks>
    /// This is the sharpest case in the file, and it came directly out of the workstation smoke.
    /// Meitu's editor is titled with the start page's title plus a feature suffix, and it keeps
    /// showing much of the same navigation — so markers alone, or a prefix title match, would
    /// read an open document as a clean start page and let automation proceed over the
    /// operator's unfinished work. Only the exact signed clean-start title makes it a start page.
    /// </remarks>
    [Fact]
    public void A_feature_suffixed_title_is_never_the_clean_start_page_even_with_every_marker_visible()
    {
        MeituStateSnapshot snapshot = Classify(Observe(title: "美图秀秀-图片编辑", texts: MeituFakes.WelcomeMarkers));

        snapshot.State.ShouldBe(MeituStartingState.Unknown);
        snapshot.IsSafeStartingState.ShouldBeFalse();

        // The markers were seen — the title is what refused it, which is what the evidence says.
        snapshot.MatchedMarkers.Length.ShouldBe(MeituFakes.WelcomeMarkers.Length);
    }

    [Fact]
    public void The_exact_signed_clean_start_title_is_what_admits_the_welcome_page()
    {
        Classify(Observe(title: "美图秀秀", texts: MeituFakes.WelcomeMarkers))
            .State.ShouldBe(MeituStartingState.KnownWelcome);
    }

    // -----------------------------------------------------------------------------
    // Blocking states win (§13)
    // -----------------------------------------------------------------------------

    [Fact]
    public void An_owned_dialog_outranks_an_otherwise_recognisable_welcome_page()
    {
        MeituStateSnapshot snapshot = Classify(
            Observe(texts: MeituFakes.WelcomeMarkers, dialogs: ["更新提示"]));

        snapshot.State.ShouldBe(MeituStartingState.KnownModal);
        snapshot.IsSafeStartingState.ShouldBeFalse();
    }

    [Fact]
    public void A_disabled_main_window_is_treated_as_blocked_even_with_no_dialog_title_readable()
    {
        Classify(Observe(texts: MeituFakes.WelcomeMarkers, enabled: false))
            .State.ShouldBe(MeituStartingState.KnownModal);
    }

    // -----------------------------------------------------------------------------
    // The handed-over working copy (§12)
    // -----------------------------------------------------------------------------

    [Fact]
    public void The_editor_is_recognised_only_by_the_exact_derived_Save_identity()
    {
        Classify(Observe(
                title: MeituFakes.EditorTitle,
                texts: [.. MeituFakes.EditorMarkers],
                expectedFile: "working_a1.png",
                observedIdentity: "working_a1_副本"))
            .State.ShouldBe(MeituStartingState.KnownEditorWithExpectedWorkingCopy);

        Classify(Observe(
                title: MeituFakes.EditorTitle,
                texts: [.. MeituFakes.EditorMarkers],
                expectedFile: "working_a1.png",
                observedIdentity: "working_b2_副本"))
            .State.ShouldBe(MeituStartingState.Unknown);
    }

    [Fact]
    public void A_stale_A_value_cannot_validate_current_B()
    {
        Classify(Observe(
                title: MeituFakes.EditorTitle,
                texts: [.. MeituFakes.EditorMarkers],
                expectedFile: "PF_IDENTITY_B.png",
                observedIdentity: "PF_IDENTITY_A_副本"))
            .State.ShouldBe(MeituStartingState.Unknown);
    }

    [Fact]
    public void An_empty_editor_cannot_validate_an_old_document_identity()
    {
        Classify(Observe(
                title: MeituFakes.EditorTitle,
                texts: [.. MeituFakes.EmptyEditorMarkers],
                expectedFile: "PF_IDENTITY_B.png",
                observedIdentity: "PF_IDENTITY_A_副本"))
            .State.ShouldBe(MeituStartingState.Unknown);
    }

    /// <summary>
    /// The expected file name alone does not make a screen the editor.
    /// </summary>
    /// <remarks>
    /// The Part A rule accepted any screen whose title was acceptable and on which the expected
    /// name appeared anywhere. That is weaker than it reads: a file name shows up in places that
    /// say nothing about what is loaded — a recent-files list being the obvious one — so the
    /// name has to be corroborated by the editor's own signed markers before it counts as
    /// confirmation (§13, §14).
    /// </remarks>
    [Fact]
    public void The_expected_file_name_without_the_signed_editor_markers_is_not_accepted()
    {
        Classify(Observe(
                title: MeituFakes.EditorTitle,
                texts: ["working_a1.png"],
                expectedFile: "working_a1.png"))
            .State.ShouldBe(MeituStartingState.Unknown);
    }

    /// <summary>
    /// The editor's markers without the expected name are not accepted either.
    /// </summary>
    /// <remarks>
    /// The counterpart of the test above, and the one that rules out "some document is open" as
    /// sufficient (§14). Both halves of the signature have to hold, because either on its own is
    /// satisfied by a screen PrintFlow's file is not on.
    /// </remarks>
    [Fact]
    public void The_signed_editor_markers_without_the_expected_file_name_are_not_accepted()
    {
        Classify(Observe(
                title: MeituFakes.EditorTitle,
                texts: [.. MeituFakes.EditorMarkers],
                expectedFile: "working_a1.png"))
            .State.ShouldBe(MeituStartingState.Unknown);
    }

    /// <summary>
    /// A feature-suffixed title is never enough on its own, in either direction.
    /// </summary>
    /// <remarks>
    /// Part A's defect was a prefix rule that let the editor pass for the start page. The
    /// editor signature is matched by <i>exact</i> title equality for the same reason, so a
    /// title that merely starts with the signed one — Meitu appends feature suffixes freely —
    /// cannot carry an editor recognition either (§22).
    /// </remarks>
    [Fact]
    public void A_title_that_merely_extends_the_signed_editor_title_is_not_the_editor()
    {
        Classify(Observe(
                title: MeituFakes.EditorTitle + "-批处理",
                texts: [.. MeituFakes.EditorMarkers],
                expectedFile: "working_a1.png",
                observedIdentity: "working_a1_副本"))
            .State.ShouldBe(MeituStartingState.Unknown);
    }

    /// <summary>
    /// Expecting a file and not seeing it never resolves to a safe state.
    /// </summary>
    /// <remarks>
    /// The trap this pins is specific. <see cref="MeituStartingState.KnownEditorEmpty"/> is a
    /// <i>safe</i> starting state, so an empty-editor signature loose enough to match the editor
    /// generally would turn "PrintFlow expected A.png and the editor is showing B.png" into a
    /// green light. The classifier therefore only considers the empty editor when nothing has
    /// been handed over at all (§14, §15).
    /// </remarks>
    [Fact]
    public void An_editor_showing_another_document_is_never_KnownEditorEmpty()
    {
        MeituBaseline permissive = MeituFakes.Baseline() with
        {
            EditorEmpty = MeituFakes.EditorWithDocument(),
        };

        MeituStateSnapshot snapshot = MeituStateClassifier.Classify(
            permissive,
            Observe(
                title: MeituFakes.EditorTitle,
                texts: [.. MeituFakes.EditorMarkers, "某位客户的图.png"],
                expectedFile: "working_a1.png"));

        snapshot.State.ShouldBe(MeituStartingState.Unknown);
        snapshot.IsSafeStartingState.ShouldBeFalse();
    }

    /// <summary>
    /// An editor that merely looks empty is not evidence that it is empty.
    /// </summary>
    /// <remarks>
    /// Recorded as a test rather than left as a comment because it is a deliberate, documented
    /// gap: Epic 11000 signed a clean-start signature for the welcome page only, so
    /// <see cref="MeituStartingState.KnownEditorEmpty"/> is defined but unreachable until Part B
    /// captures one. Until then the editor stops, which is the safe direction — and this test
    /// will start failing the day someone makes it reachable without the evidence.
    /// </remarks>
    [Fact]
    public void An_editor_with_nothing_recognisable_on_it_is_Unknown_not_KnownEditorEmpty()
    {
        Classify(Observe(title: "美图秀秀-图片编辑", texts: ["撤销", "重做"]))
            .State.ShouldBe(MeituStartingState.Unknown);
    }

    [Fact]
    public void An_empty_observation_is_Unknown()
    {
        Classify(Observe(title: string.Empty)).State.ShouldBe(MeituStartingState.Unknown);
    }

    /// <summary>
    /// Only the three named states are safe; everything else stops.
    /// </summary>
    /// <remarks>
    /// Written over the whole enum so that a value added later is unsafe by default. A
    /// deny-list would have the opposite failure mode.
    /// </remarks>
    [Theory]
    [InlineData(MeituStartingState.NotRunning, false)]
    [InlineData(MeituStartingState.KnownWelcome, true)]
    [InlineData(MeituStartingState.KnownEditorEmpty, true)]
    [InlineData(MeituStartingState.KnownEditorWithExpectedWorkingCopy, true)]
    [InlineData(MeituStartingState.Busy, false)]
    [InlineData(MeituStartingState.KnownModal, false)]
    [InlineData(MeituStartingState.Unknown, false)]
    public void The_safe_starting_states_are_an_allow_list(MeituStartingState state, bool expected)
    {
        new MeituStateSnapshot(state, [], Observe()).IsSafeStartingState.ShouldBe(expected);
    }

    // -----------------------------------------------------------------------------
    // Busy outranks every content state (Part B2A §13, §23)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Busy beats the state that would otherwise be the safest one on the screen.
    /// </summary>
    /// <remarks>
    /// This is the whole reason Busy is classified before document identity. While Meitu
    /// computes, the editor keeps its title, its markers and its document, so an observation
    /// taken mid-enhancement satisfies <see cref="MeituStartingState.KnownEditorWithExpectedWorkingCopy"/>
    /// completely — and that state is on the safe-starting-state allow-list. A caller acting on
    /// it would send input into a running operation.
    /// </remarks>
    [Fact]
    public void Busy_outranks_the_expected_working_copy_it_is_running_over()
    {
        MeituBaseline baseline = MeituFakes.Baseline();
        MeituObservation busy = new(
            MeituFakes.EditorTitle,
            [.. MeituFakes.EditorMarkers, .. MeituFakes.BusyMarkers],
            [],
            MainWindowEnabled: true,
            "A.png",
            ObservedDocumentIdentity: "A_副本");

        MeituStateSnapshot snapshot = MeituStateClassifier.Classify(baseline, busy);

        snapshot.State.ShouldBe(MeituStartingState.Busy);
        snapshot.IsSafeStartingState.ShouldBeFalse();
    }

    [Fact]
    public void Busy_outranks_the_signed_empty_editor()
    {
        MeituObservation busy = new(
            MeituFakes.EditorTitle,
            [.. MeituFakes.EmptyEditorMarkers, .. MeituFakes.BusyMarkers],
            [],
            MainWindowEnabled: true,
            ExpectedWorkingCopyFileName: null);

        MeituStateClassifier.Classify(MeituFakes.Baseline(), busy).State
            .ShouldBe(MeituStartingState.Busy);
    }

    [Fact]
    public void Busy_outranks_the_signed_welcome_page()
    {
        MeituObservation busy = new(
            "美图秀秀",
            [.. MeituFakes.WelcomeMarkers, .. MeituFakes.BusyMarkers],
            [],
            MainWindowEnabled: true,
            ExpectedWorkingCopyFileName: null);

        MeituStateClassifier.Classify(MeituFakes.Baseline(), busy).State
            .ShouldBe(MeituStartingState.Busy);
    }

    /// <summary>A blocking dialog still outranks Busy: it is the more restrictive answer.</summary>
    [Fact]
    public void A_blocking_dialog_outranks_Busy()
    {
        MeituObservation blocked = new(
            MeituFakes.EditorTitle,
            [.. MeituFakes.EditorMarkers, .. MeituFakes.BusyMarkers],
            ["Form"],
            MainWindowEnabled: false,
            "A.png");

        MeituStateClassifier.Classify(MeituFakes.Baseline(), blocked).State
            .ShouldBe(MeituStartingState.KnownModal);
    }

    /// <summary>
    /// Without signed enhancement evidence, Busy is unreachable.
    /// </summary>
    /// <remarks>
    /// The Part A and B1 position, kept intact: a chain that has never been shown a processing
    /// overlay classifies one as <see cref="MeituStartingState.Unknown"/> and stops, rather than
    /// guessing at what "computing" looks like.
    /// </remarks>
    [Fact]
    public void Without_signed_enhancement_evidence_Busy_is_unreachable()
    {
        MeituObservation busy = new(
            MeituFakes.EditorTitle,
            [.. MeituFakes.BusyMarkers],
            [],
            MainWindowEnabled: true,
            ExpectedWorkingCopyFileName: null);

        MeituStateClassifier.Classify(MeituFakes.BaselineWithout(enhancement: true), busy).State
            .ShouldBe(MeituStartingState.Unknown);
    }

    /// <summary>
    /// The completion panel on its own is not Busy, and is not a safe state either.
    /// </summary>
    /// <remarks>
    /// After an enhancement finishes, the editor is showing a document and a panel. It classifies
    /// as the expected working copy — correctly, because that is what it is — and the enhancement
    /// route's own guard, not the classifier, is what refuses to invoke a module that is already
    /// selected.
    /// </remarks>
    [Fact]
    public void A_finished_enhancement_is_not_Busy()
    {
        MeituObservation finished = new(
            MeituFakes.EditorTitle,
            [.. MeituFakes.CompletedTexts()],
            [],
            MainWindowEnabled: true,
            "A.png",
            ObservedDocumentIdentity: "A_副本");

        MeituStateClassifier.Classify(MeituFakes.Baseline(), finished).State
            .ShouldBe(MeituStartingState.KnownEditorWithExpectedWorkingCopy);
    }

    [Fact]
    public void Background_Removal_Busy_outranks_the_loaded_editor()
    {
        MeituObservation busy = Observe(
            title: MeituFakes.EditorTitle,
            texts: [.. MeituFakes.EditorMarkers, .. MeituFakes.BackgroundBusyTexts()],
            expectedFile: "A.png",
            observedIdentity: "A_副本");

        MeituStateClassifier.Classify(MeituFakes.Baseline(), busy).State
            .ShouldBe(MeituStartingState.Busy);
    }

    [Fact]
    public void Background_Removal_completion_is_not_generic_Busy()
    {
        MeituObservation complete = Observe(
            title: MeituFakes.EditorTitle,
            texts:
            [
                .. MeituFakes.EditorMarkers,
                "自动选择", "局部抠图", "手动修补", "反选", "移除背景",
            ],
            expectedFile: "A.png",
            observedIdentity: "A_副本");

        MeituStateClassifier.Classify(MeituFakes.Baseline(), complete).State
            .ShouldBe(MeituStartingState.KnownEditorWithExpectedWorkingCopy);
    }

    [Fact]
    public void Enhancement_Busy_does_not_match_Background_Removal_Busy()
    {
        MeituObservation enhancementBusy = Observe(
            title: MeituFakes.EditorTitle,
            texts: [.. MeituFakes.EditorMarkers, .. MeituFakes.BusyMarkers],
            expectedFile: "A.png",
            observedIdentity: "A_副本");

        MeituBaseline backgroundOnly = MeituFakes.BaselineWithout(enhancement: true);

        MeituStateClassifier.Classify(backgroundOnly, enhancementBusy).State
            .ShouldBe(MeituStartingState.KnownEditorWithExpectedWorkingCopy);
    }
}
