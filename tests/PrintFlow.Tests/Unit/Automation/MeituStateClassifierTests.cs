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
        string? expectedFile = null) =>
        new(title,
            [.. texts ?? []],
            [.. dialogs ?? []],
            enabled,
            expectedFile);

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
    public void The_editor_is_recognised_only_by_the_file_name_PrintFlow_itself_chose()
    {
        Classify(Observe(title: "美图秀秀-图片编辑", texts: ["working_a1.png"], expectedFile: "working_a1.png"))
            .State.ShouldBe(MeituStartingState.KnownEditorWithExpectedWorkingCopy);

        // A different document being open proves nothing about PrintFlow's file, so it stops.
        Classify(Observe(title: "美图秀秀-图片编辑", texts: ["某位客户的图.png"], expectedFile: "working_a1.png"))
            .State.ShouldBe(MeituStartingState.Unknown);
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
}
