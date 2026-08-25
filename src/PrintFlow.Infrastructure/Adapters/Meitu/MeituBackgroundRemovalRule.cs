namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>Positive phases of Meitu's Background Removal route.</summary>
public enum MeituBackgroundRemovalPhase
{
    Unobserved,
    Busy,
    Complete,
}

/// <summary>Classifies only operation-specific Background Removal evidence.</summary>
public static class MeituBackgroundRemovalRule
{
    public static MeituBackgroundRemovalPhase Classify(
        MeituBackgroundRemovalSignature signature,
        MeituObservation observation)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(observation);

        bool busy = Matches(signature.Busy.RequiredMarkers, signature.Busy.MinimumRequiredMarkers, observation);
        if (busy)
        {
            return MeituBackgroundRemovalPhase.Busy;
        }

        bool complete = (!signature.Completion.RequiresBusyAbsent || !busy) &&
            Matches(
                signature.Completion.RequiredMarkers,
                signature.Completion.MinimumRequiredMarkers,
                observation);
        return complete ? MeituBackgroundRemovalPhase.Complete : MeituBackgroundRemovalPhase.Unobserved;
    }

    private static bool Matches(
        System.Collections.Immutable.ImmutableArray<string> markers,
        int minimum,
        MeituObservation observation)
    {
        if (markers.IsDefaultOrEmpty || minimum <= 0 || minimum > markers.Length ||
            observation.VisibleTexts.IsDefaultOrEmpty)
        {
            return false;
        }

        int seen = markers.Count(marker => observation.VisibleTexts.Any(
            text => text.Contains(marker, StringComparison.Ordinal)));
        return seen >= minimum;
    }
}
