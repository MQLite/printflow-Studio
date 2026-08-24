using System.Collections.Immutable;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// Decides which Meitu screen an observation shows, using only the signed baseline
/// (Epic 11300 Part A §12, §13).
/// </summary>
/// <remarks>
/// Pure and static: no window handle, no OS call, no clock. Every recognition rule in this
/// slice is therefore testable by constructing a <see cref="MeituObservation"/>, which is why
/// the interesting safety cases — a blocking dialog, an unrecognised screen, a look-alike title
/// — have unit tests rather than a manual check.
///
/// The classification order is the safety order. Blocking states are decided before content
/// states, so a modal sitting over a recognisable welcome page is reported as a modal rather
/// than waved through. Everything not positively matched falls to
/// <see cref="MeituStartingState.Unknown"/>, which stops.
/// </remarks>
public static class MeituStateClassifier
{
    /// <summary>
    /// How many distinct signed welcome markers must be visible before the start page is
    /// accepted.
    /// </summary>
    /// <remarks>
    /// Epic 11000's own recognition rule reads: "Use multiple stable structural markers plus
    /// exact process/version and title. Never identify this page by colour or advertisement
    /// content alone." Four is the operational reading of "multiple" — high enough that a
    /// single coincidental label cannot carry the decision, low enough to survive one entry
    /// being renamed or scrolled out of the automation tree in a future build.
    /// </remarks>
    public const int MinimumWelcomeMarkers = 4;

    /// <summary>Classifies <paramref name="observation"/> against <paramref name="baseline"/>.</summary>
    public static MeituStateSnapshot Classify(MeituBaseline baseline, MeituObservation observation)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(observation);

        ImmutableArray<string> matched = MatchedMarkers(baseline, observation);

        // 1. Blocking first. An owned pop-up, or a main window the OS reports as disabled,
        //    means something is in front of whatever else is on screen — and PrintFlow never
        //    guesses how to dismiss it (MVP design §11.4).
        if (!observation.MainWindowEnabled || !observation.OwnedDialogTitles.IsDefaultOrEmpty)
        {
            return new MeituStateSnapshot(MeituStartingState.KnownModal, matched, observation);
        }

        // 2. A title PrintFlow has never seen on the accepted runtime is not a screen it can
        //    reason about, whatever else is visible.
        if (!IsAcceptedTitle(baseline, observation.WindowTitle))
        {
            return new MeituStateSnapshot(MeituStartingState.Unknown, matched, observation);
        }

        // 3. The working copy PrintFlow handed over, identified by the name PrintFlow chose and
        //    only on the screen the signed editor evidence describes. Three things must hold
        //    together: the editor's exact title, enough of its positive markers, and the
        //    expected name where the evidence says a document name appears. "Some document is
        //    open" is never sufficient, and a name PrintFlow did not choose proves nothing (§14).
        if (observation.ExpectedWorkingCopyFileName is { Length: > 0 } expected &&
            baseline.EditorWithWorkingCopy is { } editor &&
            MatchesEditor(editor, observation) &&
            ShowsFile(observation, expected, editor.FileNameLocation))
        {
            return new MeituStateSnapshot(
                MeituStartingState.KnownEditorWithExpectedWorkingCopy, matched, observation);
        }

        // 4. The signed clean start page: its exact recorded title, plus multiple markers.
        //    The title must match exactly here, not by prefix as in step 2. Meitu's editor is
        //    titled "美图秀秀-图片编辑" — the start page's title with a feature suffix — and the
        //    editor still shows much of the same navigation, so a prefix match plus markers
        //    could read an open document as a clean start page. Exact equality against the
        //    signed clean-start title is what makes that impossible.
        if (matched.Length >= MinimumWelcomeMarkers &&
            string.Equals(observation.WindowTitle, baseline.WelcomeWindowTitle, StringComparison.Ordinal))
        {
            return new MeituStateSnapshot(MeituStartingState.KnownWelcome, matched, observation);
        }

        // 5. The editor with no document loaded — considered only when nothing has been handed
        //    over. The restriction is what stops the dangerous reading of §14: if PrintFlow
        //    expected A.png and the editor is showing B.png, step 3 has already declined, and
        //    without this guard an empty-editor signature loose enough to match would turn that
        //    into a *safe* starting state. Expecting a file and not seeing it is never safe.
        if (observation.ExpectedWorkingCopyFileName is not { Length: > 0 } &&
            baseline.EditorEmpty is { } empty &&
            MatchesEditor(empty, observation))
        {
            return new MeituStateSnapshot(MeituStartingState.KnownEditorEmpty, matched, observation);
        }

        // 6. Anything else — including a screen whose signature the verified chain does not
        //    carry, and a processing overlay Part B1 has no signed evidence for.
        return new MeituStateSnapshot(MeituStartingState.Unknown, matched, observation);
    }

    /// <summary>
    /// Whether an observation matches a signed editor signature: exact title, enough positive
    /// markers.
    /// </summary>
    /// <remarks>
    /// Exact title equality rather than the prefix rule of step 2. The distinction is the
    /// Part A defect in reverse: Meitu titles its editor with the start page's title plus a
    /// feature suffix, so a prefix test cannot separate the two screens in either direction.
    ///
    /// The markers are required, not merely counted, and they are <i>positive</i> ones. A
    /// signature phrased as "the expected file name is absent" would be matched just as well by
    /// a text read that returned nothing at all, which is the failure mode §15 rules out.
    /// </remarks>
    private static bool MatchesEditor(MeituEditorSignature signature, MeituObservation observation)
    {
        if (!string.Equals(observation.WindowTitle, signature.WindowTitle, StringComparison.Ordinal))
        {
            return false;
        }

        if (signature.RequiredMarkers.IsDefaultOrEmpty || signature.MinimumRequiredMarkers <= 0)
        {
            // A signature with no positive markers would accept the screen on its title alone,
            // and a title is the one thing on a window that any application can claim.
            return false;
        }

        int seen = 0;
        foreach (string marker in signature.RequiredMarkers)
        {
            if (Shows(observation, marker))
            {
                seen++;
            }
        }

        return seen >= signature.MinimumRequiredMarkers;
    }

    private static ImmutableArray<string> MatchedMarkers(MeituBaseline baseline, MeituObservation observation)
    {
        if (baseline.WelcomeMarkers.IsDefaultOrEmpty || observation.VisibleTexts.IsDefaultOrEmpty)
        {
            return [];
        }

        ImmutableArray<string>.Builder matched = ImmutableArray.CreateBuilder<string>();
        foreach (string marker in baseline.WelcomeMarkers)
        {
            foreach (string text in observation.VisibleTexts)
            {
                if (text.Contains(marker, StringComparison.Ordinal))
                {
                    matched.Add(marker);
                    break;
                }
            }
        }

        return matched.ToImmutable();
    }

    private static bool IsAcceptedTitle(MeituBaseline baseline, string title)
    {
        if (baseline.AcceptedWindowTitles.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (string accepted in baseline.AcceptedWindowTitles)
        {
            // Prefix rather than equality: Epic 11000 observed both "美图秀秀" and
            // "美图秀秀-图片编辑", and a feature suffix on an accepted base title is still the
            // accepted application. The base title itself must still match exactly.
            if (title.StartsWith(accepted, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the expected document name appears where the signed evidence says it appears.
    /// </summary>
    /// <remarks>
    /// The location is part of the signature rather than "anywhere in the UI" because file
    /// names turn up in places that prove nothing about what is loaded — a recent-files list on
    /// the start page being the obvious one. Restricting the search to the observed location
    /// keeps the confirmation a statement about the open document (§13).
    /// </remarks>
    private static bool ShowsFile(
        MeituObservation observation, string fileName, MeituFileNameLocation location)
    {
        bool inTitle = location is MeituFileNameLocation.WindowTitle
                or MeituFileNameLocation.TitleOrVisibleText &&
            observation.WindowTitle.Contains(fileName, StringComparison.OrdinalIgnoreCase);

        if (inTitle)
        {
            return true;
        }

        if (location is not (MeituFileNameLocation.VisibleText or MeituFileNameLocation.TitleOrVisibleText))
        {
            return false;
        }

        foreach (string text in observation.VisibleTexts)
        {
            if (text.Contains(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a marker appears in the window title or any automation name read.</summary>
    private static bool Shows(MeituObservation observation, string marker)
    {
        if (observation.WindowTitle.Contains(marker, StringComparison.Ordinal))
        {
            return true;
        }

        if (observation.VisibleTexts.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (string text in observation.VisibleTexts)
        {
            if (text.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
