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

        // 3. The working copy PrintFlow handed over, identified by the name PrintFlow chose.
        //    A name it did not choose proves nothing and is not accepted here.
        if (observation.ExpectedWorkingCopyFileName is { Length: > 0 } expected &&
            ShowsFile(observation, expected))
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

        // 5. Anything else — including an editor that merely looks empty, and a processing
        //    overlay whose signature Part A has no signed evidence for.
        return new MeituStateSnapshot(MeituStartingState.Unknown, matched, observation);
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

    private static bool ShowsFile(MeituObservation observation, string fileName)
    {
        if (observation.WindowTitle.Contains(fileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
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
}
