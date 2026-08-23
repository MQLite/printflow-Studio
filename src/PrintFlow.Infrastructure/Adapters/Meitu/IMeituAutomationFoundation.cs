using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>Meitu is running, identified, and sitting on a state PrintFlow recognises as safe.</summary>
/// <param name="Target">The verified process and window.</param>
/// <param name="State">The recognised safe state, with the markers it was recognised by.</param>
/// <param name="WasLaunched">
/// Whether PrintFlow started this instance. Recorded because §28 asks the smoke to report
/// whether Meitu was left open, and because reusing an operator's instance and starting a fresh
/// one are materially different situations to read about afterwards.
/// </param>
public sealed record MeituReadiness(MeituTarget Target, MeituStateSnapshot State, bool WasLaunched);

/// <summary>The working copy is open in Meitu, confirmed by name.</summary>
/// <param name="Target">The verified target, refreshed after the file was opened.</param>
/// <param name="State">
/// The confirming state — always
/// <see cref="MeituStartingState.KnownEditorWithExpectedWorkingCopy"/>, because a state that
/// merely stopped looking like the welcome page would not prove the right file is loaded.
/// </param>
public sealed record MeituOpenedWorkingCopy(MeituTarget Target, MeituStateSnapshot State);

/// <summary>
/// The Epic 11300 Part A foundation: get to a verified Meitu in a known safe state, and hand it
/// a PrintFlow-created working copy. Nothing beyond that.
/// </summary>
/// <remarks>
/// Kept separate from <c>IMeituProcessor</c> on purpose. <c>IMeituProcessor</c> is the workflow
/// seam, and a workflow step that calls it expects a validated output file and a Revision; this
/// foundation produces neither and must never be mistaken for something that does (§24). It is
/// Infrastructure-only, exercised by the controlled smoke and by tests, and is not reachable
/// from <c>SessionService</c>.
/// </remarks>
public interface IMeituAutomationFoundation
{
    /// <summary>
    /// Identifies or launches the accepted Meitu and confirms it is on a recognised safe state.
    /// </summary>
    /// <remarks>
    /// Fails closed on everything ambiguous: no accepted binary, a binary whose hash has moved,
    /// more than one candidate process, no identifiable window, a blocking dialog, or a screen
    /// that is not positively recognised. Nothing is closed, dismissed or dragged into a
    /// different state to make the answer come out safe (§13, §15).
    /// </remarks>
    Task<OperationResult<MeituReadiness>> EnsureReadyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Opens a PrintFlow-created working copy in the verified Meitu and confirms it is loaded.
    /// </summary>
    /// <param name="workingCopy">
    /// Must be a <see cref="WorkspaceArea.Working"/> reference. Anything else is refused before
    /// a path is even resolved: the customer's original, the immutable source snapshot and an
    /// approved output are never handed to an external application (§16).
    /// </param>
    /// <param name="cancellationToken">Cancellation stops the sequence without further interaction.</param>
    Task<OperationResult<MeituOpenedWorkingCopy>> OpenWorkingCopyAsync(
        WorkspaceFileRef workingCopy, CancellationToken cancellationToken);
}
