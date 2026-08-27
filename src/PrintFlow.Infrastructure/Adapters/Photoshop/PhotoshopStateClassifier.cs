using System.Collections.Immutable;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>
/// Decides which Photoshop screen an observation describes (Epic 11400 Part A §7).
/// </summary>
/// <remarks>
/// A pure function of the observation and the signed baseline: no window handle, no process, no
/// desktop. That is what lets every rule below be tested exhaustively without Photoshop running.
///
/// The ordering is part of the rule, not an implementation detail. A blocking modal is decided
/// first, because every content marker beneath it stays exactly as recognisable while a dialog
/// sits on top — a classifier that checked content first would report a safe, input-eligible
/// state during a modal that PrintFlow must not touch. Only then is the document question asked,
/// and only then the start/empty distinction.
/// </remarks>
public static class PhotoshopStateClassifier
{
    /// <summary>Classifies <paramref name="observation"/> against the signed baseline.</summary>
    public static PhotoshopStateSnapshot Classify(
        PhotoshopBaseline baseline, PhotoshopObservation observation)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(observation);

        // A titled dialog owned by Photoshop, or a main window something has disabled. Both mean
        // the same thing for automation: whatever is underneath cannot be acted on, and
        // PrintFlow does not dismiss what it did not raise (§7).
        //
        // Only *titled* owned windows count, and that qualification is a live finding rather
        // than a loosening. Photoshop keeps floating panel chrome — OWL.ShadowView and the
        // OWL.Dock it owns — as owned top-level windows that become visible whenever the frame
        // is restored from minimised, and they carry no title at all. Treating those as modals
        // made a perfectly ordinary restored editor unreachable. Both signed dialogs, 打开 and
        // 另存为, carry their exact titles and additionally disable the main window, so the rule
        // still catches everything it was written to catch.
        if (!observation.OwnedDialogTitles.IsDefaultOrEmpty &&
            observation.OwnedDialogTitles.Any(title => !string.IsNullOrWhiteSpace(title)))
        {
            return Snapshot(PhotoshopStartingState.KnownModal, [], observation);
        }

        if (!observation.MainWindowEnabled)
        {
            return Snapshot(PhotoshopStartingState.KnownModal, [], observation);
        }

        // No signed screen discriminators means no screen is positively recognisable. Refusing
        // here rather than guessing is the whole of the fail-closed rule: absent evidence and a
        // refusal are the same condition.
        if (baseline.WindowStates is not { } states)
        {
            return Snapshot(PhotoshopStartingState.Unknown, [], observation);
        }

        ImmutableArray<string> classes = observation.VisibleChildClasses.IsDefault
            ? []
            : observation.VisibleChildClasses;

        bool welcome = classes.Contains(states.StartScreenMarkerClass, StringComparer.Ordinal);
        bool document = classes.Contains(states.DocumentMarkerClass, StringComparer.Ordinal);

        // Both markers at once is not a screen PrintFlow has ever been shown, and inventing a
        // precedence between them would be exactly the kind of guess §7 rules out.
        if (welcome && document)
        {
            return Snapshot(PhotoshopStartingState.Unknown, [], observation);
        }

        if (document)
        {
            return ClassifyLoadedDocument(baseline, observation, states);
        }

        if (welcome)
        {
            return string.Equals(
                    observation.WindowTitle, baseline.NoDocumentWindowTitle, StringComparison.Ordinal)
                ? Snapshot(
                    PhotoshopStartingState.KnownStartScreen, [states.StartScreenMarkerClass], observation)
                : Snapshot(PhotoshopStartingState.Unknown, [], observation);
        }

        // Neither marker. An empty editor is recognised only when the frame is positively there
        // and the title is the signed no-document one — not merely because nothing else matched.
        bool chrome = !states.EditorChromeClasses.IsDefaultOrEmpty &&
                      states.EditorChromeClasses.All(name => classes.Contains(name, StringComparer.Ordinal));

        return chrome && string.Equals(
                observation.WindowTitle, baseline.NoDocumentWindowTitle, StringComparison.Ordinal)
            ? Snapshot(PhotoshopStartingState.KnownEditorNoDocument, states.EditorChromeClasses, observation)
            : Snapshot(PhotoshopStartingState.Unknown, [], observation);
    }

    /// <summary>
    /// Decides which document a loaded editor is holding.
    /// </summary>
    /// <remarks>
    /// The expected state is reachable only when all of it lines up: the caller named a file,
    /// the signed identity route exists, the title names that file, and a probe-supplied
    /// absolute path resolves to it. Any one of those missing yields
    /// <see cref="PhotoshopStartingState.KnownEditorWithOtherDocument"/> — a recognised state
    /// that is safe to open from and can never be mistaken for confirmation.
    /// </remarks>
    private static PhotoshopStateSnapshot ClassifyLoadedDocument(
        PhotoshopBaseline baseline,
        PhotoshopObservation observation,
        PhotoshopWindowStateSignature states)
    {
        ImmutableArray<string> matched = [states.DocumentMarkerClass];

        if (baseline.DocumentIdentity is not { } identity ||
            observation.ExpectedDocumentFileName is not { Length: > 0 } expectedName)
        {
            return Snapshot(PhotoshopStartingState.KnownEditorWithOtherDocument, matched, observation);
        }

        if (!PhotoshopDocumentIdentityRule.TitleNamesExpectedDocument(
                identity, observation.WindowTitle, expectedName))
        {
            return Snapshot(PhotoshopStartingState.KnownEditorWithOtherDocument, matched, observation);
        }

        // The title agreeing is not enough, and this is the line that keeps it that way. Without
        // a path from the identity probe the strongest available claim is "a file with the right
        // name is loaded", which is not the claim this state makes (§12).
        return observation.ObservedDocumentFullPath is { Length: > 0 }
            ? Snapshot(PhotoshopStartingState.KnownEditorWithExpectedDocument, matched, observation)
            : Snapshot(PhotoshopStartingState.KnownEditorWithOtherDocument, matched, observation);
    }

    private static PhotoshopStateSnapshot Snapshot(
        PhotoshopStartingState state,
        ImmutableArray<string> matchedMarkers,
        PhotoshopObservation observation) =>
        new(state, matchedMarkers, observation);
}
