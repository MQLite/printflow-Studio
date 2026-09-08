using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Ports;

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
/// <param name="Load">
/// What Meitu did with the document on its own initiative between being given it and being
/// asked anything (Epic 11300 Part B2B §20, §21, §22). Carried on the open rather than
/// re-derived later because it cannot be re-derived later: the only evidence that ties an
/// enhancement to <i>this</i> load is having watched Busy happen, and that moment is gone by
/// the time anyone asks.
/// </param>
public sealed record MeituOpenedWorkingCopy(
    MeituTarget Target, MeituStateSnapshot State, MeituLoadObservation Load);

/// <summary>
/// A validated Enhancement result on a PrintFlow-controlled path (Epic 11300 Part B2B §16, §27).
/// </summary>
/// <param name="File">The managed reference the result was written to.</param>
/// <param name="Facts">Its facts, read once by the existing inspection pipeline.</param>
/// <param name="Evidence">What the export route set and invoked to produce it.</param>
/// <param name="ObservationsToSettle">How many observations the file took to stop changing.</param>
/// <remarks>
/// This is the first type in the Meitu adapter that may be read as a success, and it is a
/// separate type from <see cref="MeituEnhancementOutcome"/> for exactly that reason. That record
/// says some state transitions were observed; this one says a file exists, settled, was read
/// end to end, and is a PNG no smaller than the working copy it came from. Nothing constructs it
/// without all of that having happened.
/// </remarks>
public sealed record MeituExportedOutput(
    WorkspaceFileRef File,
    FileFacts Facts,
    MeituExportEvidence Evidence,
    int ObservationsToSettle,
    MeituTransparencyFacts? Transparency);

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

    /// <summary>Re-observes a previously verified process without launching or changing it.</summary>
    Task<OperationResult<MeituReadiness>> ReinspectAsync(
        MeituReadiness previous, CancellationToken cancellationToken);

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
        WorkspaceFileRef workingCopy, IAutomationStopSignal stop, CancellationToken cancellationToken);
    /// <summary>
    /// Runs the signed Enhancement action over a working copy already open in the verified
    /// Meitu, and observes it through to positively evidenced completion.
    /// </summary>
    /// <param name="opened">The result of <see cref="OpenWorkingCopyAsync"/>.</param>
    /// <param name="workingCopy">
    /// The same <see cref="WorkspaceArea.Working"/> reference that was opened. Re-checked here
    /// rather than trusted: this method exists to invoke an irreversible action, and the area
    /// boundary is worth restating at every point that could reach one (§16).
    /// </param>
    /// <param name="cancellationToken">Cancellation stops observation and produces no further input.</param>
    /// <remarks>
    /// Success means the enhancement happened to this document and was watched from start to
    /// finish: identity confirmed, the work positively seen running and positively seen
    /// finishing, and identity confirmed again. It does <b>not</b> mean an output file exists —
    /// this method writes none — and no Revision may be created from it. Getting the result out
    /// of Meitu is <see cref="ExportEnhancedResultAsync"/>.
    ///
    /// There are two ways to get there and the difference is invisible in the result, which is
    /// deliberate. Ordinarily PrintFlow invokes the signed control once. But Meitu retains its
    /// selected module across document loads and starts enhancing the moment a file appears, so
    /// <paramref name="opened"/> may already carry a completed run — and when it does, this
    /// method invokes nothing at all rather than starting a second enhancement over the first.
    /// The guarantee that makes both paths the same claim is that neither accepts an enhancement
    /// it did not watch happen (§20, §21, §22).
    /// </remarks>
    Task<OperationResult<MeituEnhancementOutcome>> EnhanceAsync(
        MeituOpenedWorkingCopy opened, WorkspaceFileRef workingCopy, IAutomationStopSignal stop,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs only the C1 Background Removal UI action/completion slice. The required mode
    /// decision is explicit, and success still produces no export, AdapterOutput or Revision.
    /// </summary>
    Task<OperationResult<MeituBackgroundRemovalOutcome>> RemoveBackgroundAsync(
        MeituOpenedWorkingCopy opened,
        WorkspaceFileRef workingCopy,
        BackgroundRemovalDecision modeDecision,
        IAutomationStopSignal stop,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes the enhanced result to a PrintFlow-controlled path and validates what landed there
    /// (Epic 11300 Part B2B §7, §14, §16, §17, §19).
    /// </summary>
    /// <param name="enhancement">A completed, positively observed Enhancement on this document.</param>
    /// <param name="workingCopy">The <see cref="WorkspaceArea.Working"/> input, re-checked here.</param>
    /// <param name="workingCopyFactsBefore">
    /// The input's facts as read before Meitu was given it, so §19's byte-for-byte comparison has
    /// something to compare against. Taken before rather than derived after for the obvious
    /// reason: a hash read afterwards would agree with itself whatever happened.
    /// </param>
    /// <param name="output">
    /// Where the result must land — a managed reference belonging to the producing attempt. This
    /// is the whole of §7's boundary: the parameter is a <see cref="WorkspaceFileRef"/> and there
    /// is no overload taking a path, so no caller can name a destination the workspace does not
    /// own.
    /// </param>
    /// <param name="cancellationToken">Cancellation before the confirm control produces no file.</param>
    /// <remarks>
    /// Success is a much stronger claim than anywhere else in this adapter, and every part of it
    /// is checked rather than inferred: the destination did not already exist, the signed
    /// controls read back exactly what PrintFlow wrote, a file appeared at the controlled path,
    /// stopped changing, could be read to the end, inspected as the required format at no less
    /// than the working copy's dimensions, and the working copy itself is byte-for-byte what it
    /// was. A dialog closing is not part of it (§13).
    /// </remarks>
    Task<OperationResult<MeituExportedOutput>> ExportEnhancedResultAsync(
        MeituEnhancementOutcome enhancement,
        WorkspaceFileRef workingCopy,
        FileFacts workingCopyFactsBefore,
        WorkspaceFileRef output,
        IAutomationStopSignal stop,
        CancellationToken cancellationToken);

    /// <summary>
    /// Exports a completed, current-load-correlated Background Removal result through the same
    /// signed Save/另存为 route, then requires a same-size readable PNG with both transparent
    /// pixels and visible foreground while proving the Working copy is unchanged.
    /// </summary>
    Task<OperationResult<MeituExportedOutput>> ExportBackgroundRemovalResultAsync(
        MeituBackgroundRemovalOutcome backgroundRemoval,
        WorkspaceFileRef workingCopy,
        FileFacts workingCopyFactsBefore,
        WorkspaceFileRef output,
        IAutomationStopSignal stop,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads a managed file's facts through the existing inspection pipeline.
    /// </summary>
    /// <remarks>
    /// Exposed so a caller can take the "before" reading <see cref="ExportEnhancedResultAsync"/>
    /// needs without acquiring an <c>IFileInspector</c> of its own and, more importantly, without
    /// being tempted to hash the file itself. One inspection implementation, reached one way.
    /// </remarks>
    Task<OperationResult<FileFacts>> InspectManagedFileAsync(
        WorkspaceFileRef file, CancellationToken cancellationToken);

    /// <summary>
    /// Dismisses Meitu's post-save confirmation surface through its signed close control
    /// (Epic 11300 Part B2B §24).
    /// </summary>
    /// <remarks>
    /// Exposed because <see cref="CloseDocumentAsync"/> cannot do its job while that surface is
    /// up — it disables the editor, so the close control's own guard sees a blocking modal and
    /// stops. That is the correct behaviour and the reason this is a separate step rather than
    /// something the close route does on its own initiative: it is the only Meitu-owned surface
    /// PrintFlow dismisses anywhere, and a caller has to ask for it.
    ///
    /// Returns whether anything was there. Nothing to dismiss is a success — the surface is a
    /// Meitu setting the operator can switch off.
    /// </remarks>
    Task<OperationResult<bool>> DismissExportResultSurfaceAsync(
        MeituTarget target, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a loaded editor to its signed empty state, so a working file Meitu is holding can
    /// safely be deleted.
    /// </summary>
    /// <remarks>
    /// Never called as part of <see cref="EnhanceAsync"/> or <see cref="OpenWorkingCopyAsync"/>,
    /// and deliberately so. Closing a document is a decision about someone's work: PrintFlow
    /// exposes the capability for a supervised caller that knows the loaded document is its own
    /// synthetic file, and takes it on no initiative of its own (§2, §19, §28).
    /// </remarks>
    Task<OperationResult<MeituTarget>> CloseDocumentAsync(
        MeituTarget target, CancellationToken cancellationToken);

}
