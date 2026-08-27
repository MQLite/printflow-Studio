using System.Collections.Immutable;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>
/// The Photoshop screens this slice can tell apart (Epic 11400 Part A §7).
/// </summary>
/// <remarks>
/// Deliberately small. Every screen Photoshop can show that is not one of the positively
/// recognised values collapses into <see cref="Unknown"/>, and <see cref="Unknown"/> stops.
/// Adding a value means adding signed workstation evidence for it first.
/// </remarks>
public enum PhotoshopStartingState
{
    /// <summary>No process launched from the accepted executable is running.</summary>
    NotRunning,

    /// <summary>The start/home screen, matched by the signed welcome-view marker class.</summary>
    KnownStartScreen,

    /// <summary>
    /// The editor frame with no document loaded.
    /// </summary>
    /// <remarks>
    /// Positively recognised rather than inferred from an absence: the editor chrome classes are
    /// present, the welcome marker is not, and no document marker exists. All three are checked,
    /// because "the window did not look like anything else" is not evidence of an empty editor.
    /// </remarks>
    KnownEditorNoDocument,

    /// <summary>The editor showing the exact managed file PrintFlow handed over, by identity.</summary>
    /// <remarks>
    /// Reachable only when the caller supplied an expected file <i>and</i> the observation
    /// carries a document path read through the signed identity probe. A title that merely
    /// mentions the right basename never reaches this value on its own.
    /// </remarks>
    KnownEditorWithExpectedDocument,

    /// <summary>
    /// The editor showing some document that is not the expected one.
    /// </summary>
    /// <remarks>
    /// A recognised state rather than <see cref="Unknown"/>, and that distinction is the point.
    /// Photoshop is shared with the operator, so a document PrintFlow did not open is an
    /// ordinary condition and not a fault — it is safe to open another file from here, and it is
    /// never safe to treat it as the expected document or to close it (§21).
    /// </remarks>
    KnownEditorWithOtherDocument,

    /// <summary>Photoshop is computing.</summary>
    /// <remarks>
    /// Declared because §7 names it, but <b>never returned by the Part A classifier</b>: no
    /// signed busy signature exists, because Part A performs no operation long enough to have
    /// produced one to observe. Until a busy marker is captured, a computing Photoshop
    /// classifies as <see cref="Unknown"/> and stops — the conservative direction.
    /// </remarks>
    Busy,

    /// <summary>A dialog owned by Photoshop is blocking the main window.</summary>
    KnownModal,

    /// <summary>Not positively recognised. Automation stops; nothing is closed or dismissed.</summary>
    Unknown,
}

/// <summary>
/// What PrintFlow observed about the Photoshop window at one instant, before any
/// classification.
/// </summary>
/// <param name="WindowTitle">The verified window's title.</param>
/// <param name="VisibleChildClasses">Distinct classes of the verified window's visible children.</param>
/// <param name="OwnedDialogTitles">Titles of owned pop-ups sitting above the main window.</param>
/// <param name="MainWindowEnabled">False when a modal has disabled the main window.</param>
/// <param name="ExpectedDocumentFileName">
/// The file name of the managed file this attempt handed over, or <c>null</c> when nothing has
/// been handed over yet.
/// </param>
/// <param name="ObservedDocumentFullPath">
/// The absolute path of the loaded document, read from the signed Save As identity probe and
/// then cancelled, or <c>null</c> for an ordinary read-only observation. Only the guarded
/// identity probe may supply a value; the window title never populates it.
/// </param>
/// <remarks>
/// Separating observation from classification is what makes the recognition rules testable
/// without a desktop: <see cref="PhotoshopStateClassifier"/> is a pure function of this record
/// and the signed baseline, so every rule has a unit test that needs no Photoshop, no window and
/// no screen.
/// </remarks>
public sealed record PhotoshopObservation(
    string WindowTitle,
    ImmutableArray<string> VisibleChildClasses,
    ImmutableArray<string> OwnedDialogTitles,
    bool MainWindowEnabled,
    string? ExpectedDocumentFileName,
    string? ObservedDocumentFullPath = null);

/// <summary>The classified state plus the evidence the classification rested on.</summary>
/// <param name="State">What PrintFlow decided the screen is.</param>
/// <param name="MatchedMarkers">Which signed marker classes were seen, for the log.</param>
/// <param name="Observation">The raw observation, retained so a failure can explain itself.</param>
public sealed record PhotoshopStateSnapshot(
    PhotoshopStartingState State,
    ImmutableArray<string> MatchedMarkers,
    PhotoshopObservation Observation)
{
    /// <summary>
    /// Whether an open may proceed from this state (Epic 11400 Part A §7).
    /// </summary>
    /// <remarks>
    /// An allow-list, never a deny-list: a state not named here is unsafe, so a value added to
    /// <see cref="PhotoshopStartingState"/> later is refused by default rather than silently
    /// permitted.
    /// <para>
    /// <see cref="PhotoshopStartingState.KnownEditorWithOtherDocument"/> is on the list on
    /// purpose. Photoshop routinely holds the operator's own work, and refusing to open a
    /// managed file merely because someone else's document is loaded would be a refusal about
    /// PrintFlow's tidiness rather than about safety. What that state must never do is satisfy
    /// an identity check, and it cannot: identity is decided separately, against the absolute
    /// path.
    /// </para>
    /// </remarks>
    public bool IsSafeStartingState => State
        is PhotoshopStartingState.KnownStartScreen
        or PhotoshopStartingState.KnownEditorNoDocument
        or PhotoshopStartingState.KnownEditorWithExpectedDocument
        or PhotoshopStartingState.KnownEditorWithOtherDocument;
}

/// <summary>
/// A Photoshop process and the one top-level window PrintFlow has attributed to it.
/// </summary>
/// <remarks>
/// Passed around instead of a bare handle so that "which process does this window belong to?" is
/// always answerable at the point of use, which is what the re-verification before every
/// interaction depends on.
/// </remarks>
public sealed record PhotoshopTarget(ExternalProcessRef Process, ExternalWindowRef Window);

/// <summary>
/// The absolute path of the document Photoshop is holding, established through the signed
/// read-only identity probe (Epic 11400 Part A §11).
/// </summary>
/// <param name="ObservedFileName">The basename the identity surface reported.</param>
/// <param name="ObservedDirectory">The containing folder the identity surface reported.</param>
/// <param name="ObservedFullPath">The two combined — the value identity is decided on.</param>
/// <param name="WindowTitle">The window title at the moment of the probe, recorded as corroboration.</param>
/// <remarks>
/// Carries no output, revision or success surface, and must not grow one. This record says
/// "Photoshop is holding exactly this file"; it says nothing whatever about anything having been
/// done to it (§18).
/// </remarks>
public sealed record PhotoshopDocumentIdentity(
    string ObservedFileName,
    string ObservedDirectory,
    string ObservedFullPath,
    string WindowTitle);
