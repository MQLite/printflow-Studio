using System.Collections.Immutable;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// The Meitu screens this slice can tell apart (Epic 11300 Part A §12).
/// </summary>
/// <remarks>
/// Deliberately small. Every screen Meitu can show that is not one of the positively
/// recognised values collapses into <see cref="Unknown"/>, and <see cref="Unknown"/> stops.
/// Adding a value means adding signed workstation evidence for it first — the enum is not a
/// wish list of screens PrintFlow would like to handle.
/// </remarks>
public enum MeituStartingState
{
    /// <summary>No process launched from the accepted executable is running.</summary>
    NotRunning,

    /// <summary>The clean start page, matched against the signed clean-start markers.</summary>
    KnownWelcome,

    /// <summary>
    /// The editor with no document loaded.
    /// </summary>
    /// <remarks>
    /// Defined because §12 names it and because the safe-state rule (§13) will admit it, but
    /// <b>never returned by the Part A classifier</b>: Epic 11000 captured a signed signature
    /// for the welcome page only, and "the editor happens to look empty" is an absence of
    /// evidence rather than evidence of absence. Until an empty-editor signature is captured,
    /// that screen classifies as <see cref="Unknown"/> and stops. Reaching it is Part B work.
    /// </remarks>
    KnownEditorEmpty,

    /// <summary>The editor showing the working copy PrintFlow itself handed over, by name.</summary>
    KnownEditorWithExpectedWorkingCopy,

    /// <summary>Meitu is computing. Not a safe starting state; automation waits or stops.</summary>
    /// <remarks>
    /// Like <see cref="KnownEditorEmpty"/>, this has no signed signature in the Part A chain:
    /// the processing-overlay captures live in the Epic 11000 workflow evidence files, which the
    /// preset does not vouch for. A processing overlay therefore classifies as
    /// <see cref="Unknown"/> today, which stops — the conservative direction.
    /// </remarks>
    Busy,

    /// <summary>A dialog owned by Meitu is blocking the main window.</summary>
    KnownModal,

    /// <summary>Not positively recognised. Automation stops; nothing is closed or dismissed.</summary>
    Unknown,
}

/// <summary>
/// What PrintFlow observed about the Meitu window at one instant, before any classification.
/// </summary>
/// <param name="WindowTitle">The verified window's title.</param>
/// <param name="VisibleTexts">Automation names read beneath the verified window.</param>
/// <param name="OwnedDialogTitles">Titles of owned pop-ups sitting above the main window.</param>
/// <param name="MainWindowEnabled">False when a modal has disabled the main window.</param>
/// <param name="ExpectedWorkingCopyFileName">
/// The file name of the working copy this attempt handed over, or <c>null</c> when nothing has
/// been handed over yet.
/// </param>
/// <remarks>
/// Separating observation from classification is what makes the recognition rules testable
/// without a desktop: <see cref="MeituStateClassifier"/> is a pure function of this record and
/// the signed baseline, so every rule in §12 and §13 has a unit test that needs no Meitu, no
/// window and no screen.
/// </remarks>
public sealed record MeituObservation(
    string WindowTitle,
    ImmutableArray<string> VisibleTexts,
    ImmutableArray<string> OwnedDialogTitles,
    bool MainWindowEnabled,
    string? ExpectedWorkingCopyFileName);

/// <summary>The classified state plus the evidence the classification rested on.</summary>
/// <param name="State">What PrintFlow decided the screen is.</param>
/// <param name="MatchedMarkers">Which signed welcome markers were seen, for the log.</param>
/// <param name="Observation">The raw observation, retained so a failure can explain itself.</param>
public sealed record MeituStateSnapshot(
    MeituStartingState State,
    ImmutableArray<string> MatchedMarkers,
    MeituObservation Observation)
{
    /// <summary>
    /// Whether processing may proceed from this state (Epic 11300 Part A §13).
    /// </summary>
    /// <remarks>
    /// An allow-list, never a deny-list: a state that is not named here is unsafe, so a value
    /// added to <see cref="MeituStartingState"/> later is refused by default rather than
    /// silently permitted.
    /// </remarks>
    public bool IsSafeStartingState => State
        is MeituStartingState.KnownWelcome
        or MeituStartingState.KnownEditorEmpty
        or MeituStartingState.KnownEditorWithExpectedWorkingCopy;
}

/// <summary>
/// A Meitu process and the one top-level window PrintFlow has attributed to it.
/// </summary>
/// <remarks>
/// The adapter passes this around instead of a bare handle so that "which process does this
/// window belong to?" is always answerable at the point of use, which is what the
/// re-verification before every interaction depends on (§10).
/// </remarks>
public sealed record MeituTarget(ExternalProcessRef Process, ExternalWindowRef Window);
