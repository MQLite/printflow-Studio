using System.Collections.Immutable;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Unit.Automation;

/// <summary>
/// The Photoshop starting-state rules, exercised as pure functions (Epic 11400 Part A §7).
/// </summary>
/// <remarks>
/// No window, no process and no screen appears in this file, which is the point of having split
/// observation from classification: the rules that decide whether PrintFlow is allowed to touch
/// Photoshop at all are the ones that most need exhaustive tests, and they are the ones that
/// would otherwise be hardest to write.
/// </remarks>
public sealed class PhotoshopStateClassifierTests
{
    private static PhotoshopObservation Observe(
        string title = PhotoshopFakes.NoDocumentTitle,
        IEnumerable<string>? classes = null,
        IEnumerable<string>? dialogs = null,
        bool enabled = true,
        string? expectedFile = null,
        string? observedPath = null) =>
        new(title, [.. classes ?? []], [.. dialogs ?? []], enabled, expectedFile, observedPath);

    private static PhotoshopStateSnapshot Classify(
        PhotoshopObservation observation, PhotoshopBaseline? baseline = null) =>
        PhotoshopStateClassifier.Classify(baseline ?? PhotoshopFakes.Baseline(), observation);

    private static ImmutableArray<string> StartScreenClasses =>
        [.. PhotoshopFakes.EditorChromeClasses, PhotoshopFakes.StartScreenClass];

    private static ImmutableArray<string> DocumentClasses =>
        [.. PhotoshopFakes.EditorChromeClasses, PhotoshopFakes.DocumentClass, "OWL.Palette"];

    // -----------------------------------------------------------------------------------
    // The three recognised no-input states
    // -----------------------------------------------------------------------------------

    [Fact]
    public void The_welcome_view_marker_identifies_the_start_screen()
    {
        PhotoshopStateSnapshot snapshot = Classify(Observe(classes: StartScreenClasses));

        snapshot.State.ShouldBe(PhotoshopStartingState.KnownStartScreen);
        snapshot.IsSafeStartingState.ShouldBeTrue();
        snapshot.MatchedMarkers.ShouldContain(PhotoshopFakes.StartScreenClass);
    }

    /// <summary>
    /// An empty editor is recognised by what is present, not merely by what is absent.
    /// </summary>
    /// <remarks>
    /// The distinction matters because the two no-document screens share a window title. If the
    /// rule were "no document marker, therefore empty editor", any screen PrintFlow failed to
    /// read would classify as a safe empty editor — which is exactly the wrong direction to fail
    /// in.
    /// </remarks>
    [Fact]
    public void The_editor_chrome_without_either_marker_identifies_an_empty_editor()
    {
        PhotoshopStateSnapshot snapshot = Classify(Observe(classes: PhotoshopFakes.EditorChromeClasses));

        snapshot.State.ShouldBe(PhotoshopStartingState.KnownEditorNoDocument);
        snapshot.IsSafeStartingState.ShouldBeTrue();
    }

    [Fact]
    public void A_window_with_no_readable_children_is_unknown_rather_than_empty()
    {
        Classify(Observe(classes: [])).State.ShouldBe(PhotoshopStartingState.Unknown);
    }

    [Fact]
    public void Partial_editor_chrome_is_not_an_empty_editor()
    {
        Classify(Observe(classes: ["OWL.MenuBar"])).State.ShouldBe(PhotoshopStartingState.Unknown);
    }

    /// <summary>The no-document title is required as well as the markers.</summary>
    [Fact]
    public void An_unexpected_title_on_the_start_screen_is_unknown()
    {
        Classify(Observe(title: "Adobe Photoshop 2026", classes: StartScreenClasses))
            .State.ShouldBe(PhotoshopStartingState.Unknown);
    }

    // -----------------------------------------------------------------------------------
    // Documents
    // -----------------------------------------------------------------------------------

    [Fact]
    public void A_loaded_document_with_a_probe_confirmed_path_is_the_expected_document()
    {
        PhotoshopStateSnapshot snapshot = Classify(Observe(
            title: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName),
            classes: DocumentClasses,
            expectedFile: PhotoshopFakes.ExpectedFileName,
            observedPath: PhotoshopFakes.ExpectedPath));

        snapshot.State.ShouldBe(PhotoshopStartingState.KnownEditorWithExpectedDocument);
        snapshot.IsSafeStartingState.ShouldBeTrue();
    }

    /// <summary>
    /// The single most important rule in this file: a matching title alone never confirms.
    /// </summary>
    /// <remarks>
    /// Photoshop's title carries only a basename, so a title match says "a file of this name is
    /// loaded" and nothing about which folder it came from. Without the identity probe's path the
    /// classifier must stop short of the confirmed state — and it must land somewhere honest
    /// rather than on Unknown, because a document really is loaded (§12).
    /// </remarks>
    [Fact]
    public void A_matching_title_without_a_probe_path_is_not_the_expected_document()
    {
        PhotoshopStateSnapshot snapshot = Classify(Observe(
            title: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName),
            classes: DocumentClasses,
            expectedFile: PhotoshopFakes.ExpectedFileName));

        snapshot.State.ShouldBe(PhotoshopStartingState.KnownEditorWithOtherDocument);
    }

    [Fact]
    public void A_different_document_is_recognised_but_never_the_expected_one()
    {
        PhotoshopStateSnapshot snapshot = Classify(Observe(
            title: PhotoshopFakes.TitleFor("SOMEONE-ELSES-WORK.psd"),
            classes: DocumentClasses,
            expectedFile: PhotoshopFakes.ExpectedFileName,
            observedPath: @"C:\Users\admin\Desktop\SOMEONE-ELSES-WORK.psd"));

        snapshot.State.ShouldBe(PhotoshopStartingState.KnownEditorWithOtherDocument);

        // Safe to open from — an operator's own document is an ordinary condition, not a fault.
        snapshot.IsSafeStartingState.ShouldBeTrue();
    }

    [Fact]
    public void A_loaded_document_with_no_expected_file_named_is_someone_elses()
    {
        Classify(Observe(title: PhotoshopFakes.TitleFor("X.png"), classes: DocumentClasses))
            .State.ShouldBe(PhotoshopStartingState.KnownEditorWithOtherDocument);
    }

    // -----------------------------------------------------------------------------------
    // Modals and ambiguity
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A modal is decided before any content marker, because content stays visible beneath one.
    /// </summary>
    [Fact]
    public void An_owned_dialog_outranks_every_content_marker()
    {
        PhotoshopStateSnapshot snapshot = Classify(Observe(
            title: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName),
            classes: DocumentClasses,
            dialogs: ["存储为"],
            expectedFile: PhotoshopFakes.ExpectedFileName,
            observedPath: PhotoshopFakes.ExpectedPath));

        snapshot.State.ShouldBe(PhotoshopStartingState.KnownModal);
        snapshot.IsSafeStartingState.ShouldBeFalse();
    }

    [Fact]
    public void A_disabled_main_window_is_a_modal_even_with_no_dialog_title_readable()
    {
        Classify(Observe(classes: StartScreenClasses, enabled: false))
            .State.ShouldBe(PhotoshopStartingState.KnownModal);
    }

    /// <summary>
    /// Photoshop's own untitled floating chrome is not a blocking dialog.
    /// </summary>
    /// <remarks>
    /// A live regression. Photoshop keeps <c>OWL.ShadowView</c> and the <c>OWL.Dock</c> it owns
    /// as owned top-level windows, and both become visible when the frame is restored from
    /// minimised. Counting every owned visible window as a modal reported a blocking dialog over
    /// an ordinary editor that had just been un-minimised — a refusal about PrintFlow's own
    /// reading rather than about Photoshop.
    /// </remarks>
    [Fact]
    public void Untitled_owned_chrome_windows_are_not_a_blocking_modal()
    {
        PhotoshopStateSnapshot snapshot = Classify(Observe(
            title: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName),
            classes: DocumentClasses,
            dialogs: ["", "   "],
            expectedFile: PhotoshopFakes.ExpectedFileName,
            observedPath: PhotoshopFakes.ExpectedPath));

        snapshot.State.ShouldBe(PhotoshopStartingState.KnownEditorWithExpectedDocument);
    }

    /// <summary>One titled dialog among untitled chrome is still a modal.</summary>
    [Fact]
    public void A_titled_dialog_among_untitled_chrome_is_still_a_modal()
    {
        Classify(Observe(classes: StartScreenClasses, dialogs: ["", "颜色设置", ""]))
            .State.ShouldBe(PhotoshopStartingState.KnownModal);
    }

    /// <summary>Both markers at once is a screen nobody has been shown, so it stops.</summary>
    [Fact]
    public void The_welcome_and_document_markers_together_are_unknown()
    {
        Classify(Observe(classes: [.. DocumentClasses, PhotoshopFakes.StartScreenClass]))
            .State.ShouldBe(PhotoshopStartingState.Unknown);
    }

    // -----------------------------------------------------------------------------------
    // Fail-closed on absent evidence
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A preset that vouches for no screen discriminators recognises nothing at all.
    /// </summary>
    /// <remarks>
    /// "PrintFlow has not been shown this" and "PrintFlow refuses to act on this" have to be the
    /// same condition, or a missing evidence file becomes a silent widening of what the adapter
    /// will drive.
    /// </remarks>
    [Fact]
    public void Without_signed_window_state_evidence_every_screen_is_unknown()
    {
        PhotoshopBaseline baseline = PhotoshopFakes.Baseline(includeStates: false);

        Classify(Observe(classes: StartScreenClasses), baseline)
            .State.ShouldBe(PhotoshopStartingState.Unknown);
        Classify(Observe(classes: DocumentClasses), baseline)
            .State.ShouldBe(PhotoshopStartingState.Unknown);
    }

    /// <summary>
    /// Without signed identity evidence, a loaded document can never be the expected one.
    /// </summary>
    [Fact]
    public void Without_signed_identity_evidence_the_expected_document_state_is_unreachable()
    {
        PhotoshopStateSnapshot snapshot = Classify(
            Observe(
                title: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName),
                classes: DocumentClasses,
                expectedFile: PhotoshopFakes.ExpectedFileName,
                observedPath: PhotoshopFakes.ExpectedPath),
            PhotoshopFakes.Baseline(includeIdentity: false));

        snapshot.State.ShouldBe(PhotoshopStartingState.KnownEditorWithOtherDocument);
    }

    /// <summary>
    /// Busy is declared but unreachable in Part A, and that is asserted rather than assumed.
    /// </summary>
    /// <remarks>
    /// The value exists because §7 names it. No signed busy signature was captured, so nothing
    /// may classify as Busy — a computing Photoshop has to fall to Unknown and stop. This test
    /// is what stops someone later wiring the value up without the evidence to back it.
    /// </remarks>
    [Fact]
    public void No_observation_classifies_as_Busy_in_Part_A()
    {
        PhotoshopObservation[] everyShape =
        [
            Observe(classes: StartScreenClasses),
            Observe(classes: DocumentClasses),
            Observe(classes: PhotoshopFakes.EditorChromeClasses),
            Observe(classes: [], dialogs: ["something"]),
            Observe(classes: [], enabled: false),
            Observe(classes: ["OWL.Unknown"]),
        ];

        foreach (PhotoshopObservation observation in everyShape)
        {
            Classify(observation).State.ShouldNotBe(PhotoshopStartingState.Busy);
        }
    }
}
