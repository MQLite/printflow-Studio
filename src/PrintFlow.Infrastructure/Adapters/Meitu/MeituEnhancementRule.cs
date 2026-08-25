namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// How far an Enhancement invocation has got, decided from signed evidence alone
/// (Epic 11300 Part B2A §13, §14, §17).
/// </summary>
public enum MeituEnhancementPhase
{
    /// <summary>
    /// Neither the signed Busy markers nor the signed completion markers are showing.
    /// </summary>
    /// <remarks>
    /// Not a failure by itself — it is the ordinary reading in the moments after the control is
    /// invoked and before Meitu has put anything on screen. It becomes a failure only when the
    /// bounded timeout expires while the observation is still here.
    /// </remarks>
    Unobserved,

    /// <summary>The signed Busy markers are showing. No input may be produced.</summary>
    Busy,

    /// <summary>The signed completion markers are showing. Enhancement has finished.</summary>
    Complete,
}

/// <summary>
/// Decides whether an observation shows Meitu computing or Meitu finished, using only the
/// signed enhancement evidence (Epic 11300 Part B2A §13, §14).
/// </summary>
/// <remarks>
/// Pure and static, for the same reason as <see cref="MeituStateClassifier"/>: the rule that
/// matters most here is a refusal — "Busy stopped showing and nothing positive replaced it is
/// <b>not</b> completion" — and a refusal is only worth trusting when a test can construct the
/// situation that must produce it.
///
/// Busy is evaluated before completion, and completion may additionally require Busy to be
/// gone. The ordering is not an optimisation: while Meitu is computing, an incidental match on
/// a completion marker must not be allowed to end the wait and let the next step send input
/// into a running operation (§13).
/// </remarks>
public static class MeituEnhancementRule
{
    /// <summary>Classifies one observation against the signed enhancement evidence.</summary>
    public static MeituEnhancementPhase Classify(
        MeituEnhancementSignature signature, MeituObservation observation)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(observation);

        bool busy = IsBusy(signature, observation);
        if (busy)
        {
            return MeituEnhancementPhase.Busy;
        }

        return IsComplete(signature, observation, busy)
            ? MeituEnhancementPhase.Complete
            : MeituEnhancementPhase.Unobserved;
    }

    /// <summary>Whether the signed positive Busy markers are showing.</summary>
    public static bool IsBusy(MeituEnhancementSignature signature, MeituObservation observation)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(observation);

        return Matches(
            signature.Busy.RequiredMarkers, signature.Busy.MinimumRequiredMarkers, observation);
    }

    /// <summary>
    /// Whether the signed positive completion markers are showing.
    /// </summary>
    /// <param name="busy">Whether <see cref="IsBusy"/> holds for the same observation.</param>
    /// <remarks>
    /// Takes the Busy verdict as an argument rather than recomputing it so that the caller
    /// cannot ask "is this complete?" without having established "is this still running?".
    /// </remarks>
    public static bool IsComplete(
        MeituEnhancementSignature signature, MeituObservation observation, bool busy)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(observation);

        if (busy && signature.Completion.RequiresBusyAbsent)
        {
            return false;
        }

        return Matches(
            signature.Completion.RequiredMarkers,
            signature.Completion.MinimumRequiredMarkers,
            observation);
    }

    /// <summary>
    /// Whether enough of <paramref name="markers"/> appear in the observation.
    /// </summary>
    /// <remarks>
    /// An empty marker list, or a threshold of zero, matches <i>nothing</i> rather than
    /// everything. That inversion of the usual "all of an empty set" convention is the point:
    /// evidence that lists no positive markers describes no screen, and treating it as a match
    /// would turn missing evidence into an unconditional Busy or an unconditional completion —
    /// the two most dangerous verdicts this rule can reach.
    /// </remarks>
    private static bool Matches(
        System.Collections.Immutable.ImmutableArray<string> markers,
        int minimum,
        MeituObservation observation)
    {
        if (markers.IsDefaultOrEmpty || minimum <= 0 || minimum > markers.Length)
        {
            return false;
        }

        int seen = 0;
        foreach (string marker in markers)
        {
            if (Shows(observation, marker))
            {
                seen++;
            }
        }

        return seen >= minimum;
    }

    private static bool Shows(MeituObservation observation, string marker)
    {
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
