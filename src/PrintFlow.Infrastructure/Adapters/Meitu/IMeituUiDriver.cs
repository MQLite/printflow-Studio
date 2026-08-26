using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// The controls this slice is allowed to look for, named rather than described
/// (Epic 11300 Part A §11).
/// </summary>
/// <remarks>
/// A closed set, like <see cref="KnownShortcut"/>: the adapter can ask for the photo-editor
/// entry or a part of the file dialog, and nothing else. There is no
/// <c>FindElement(string name)</c> that a future caller could hand an arbitrary label to.
/// </remarks>
public enum KnownMeituElement
{
    /// <summary>The clean start page's photo-editor entry, selected from the signed markers.</summary>
    WelcomeOpenEntry,

    /// <summary>The empty editor's own open control, which is what actually raises the picker.</summary>
    EditorOpenControl,

    /// <summary>The file-name field of the Windows common file dialog Meitu opened.</summary>
    FileDialogFileName,

    /// <summary>The Open button of that dialog.</summary>
    FileDialogOpenButton,

    /// <summary>The loaded editor's structurally derived Save control.</summary>
    EditorSaveControl,

    /// <summary>The loaded editor's structurally derived 关闭图片 control.</summary>
    EditorCloseDocumentControl,

    /// <summary>The loaded editor's structurally derived Enhancement control.</summary>
    EditorEnhancementAction,

    /// <summary>The loaded editor's signed 抠图 page entry.</summary>
    EditorBackgroundRemovalAction,

    /// <summary>The signed 调整 page used to re-establish the ordinary editor after cutout.</summary>
    EditorBackgroundRemovalReturn,

    /// <summary>The file-name field of the owned Save surface, which carries the output base name.</summary>
    ExportFileNameField,

    /// <summary>The format selector of that surface.</summary>
    ExportFormatField,

    /// <summary>The control on that surface that opens the destination dialog.</summary>
    ExportSaveAsControl,

    /// <summary>The file-name field of the destination dialog, which takes a full path.</summary>
    ExportDestinationFileName,

    /// <summary>The destination dialog's confirm control — the only thing that writes a file.</summary>
    ExportDestinationConfirmButton,

    /// <summary>The destination dialog's cancel control, which backs out having written nothing.</summary>
    ExportDestinationCancelButton,

    /// <summary>The close control of Meitu's post-save confirmation surface.</summary>
    ExportResultCloseControl,
}

/// <summary>
/// The single boundary through which every physical interaction with Meitu passes
/// (Epic 11300 Part A §11).
/// </summary>
/// <remarks>
/// Everything above this interface — the production adapter, and later the enhancement and
/// background-removal flows — speaks in terms of a verified <see cref="MeituTarget"/>, a named
/// element and a named shortcut. No caller can express a screen coordinate, a raw key code, or
/// a window it has not had verified, because those types do not appear in this signature.
///
/// The implementation's obligation, which its callers therefore do not have to remember: every
/// method that produces input re-establishes that the target still belongs to the expected
/// process and still holds the foreground <b>before</b> the input is produced, and returns
/// <see cref="FailureCode.MeituTargetLost"/> with nothing sent otherwise (§3, §10, §19).
/// </remarks>
public interface IMeituUiDriver
{
    /// <summary>
    /// Reads the current screen and classifies it. Sends nothing and changes nothing.
    /// </summary>
    /// <param name="target">The verified process and window.</param>
    /// <param name="expectedWorkingCopyFileName">
    /// The name of the working copy this attempt handed over, so
    /// <see cref="MeituStartingState.KnownEditorWithExpectedWorkingCopy"/> can be distinguished
    /// from some other document being open. <c>null</c> before anything has been handed over.
    /// </param>
    Task<OperationResult<MeituStateSnapshot>> InspectStateAsync(
        MeituTarget target, string? expectedWorkingCopyFileName, CancellationToken cancellationToken);

    /// <summary>
    /// Brings the target window to the foreground and confirms it actually got there.
    /// </summary>
    /// <remarks>
    /// Returns a refreshed target rather than the one passed in: activation can restore a
    /// minimised window, which changes its bounds and its state flags, and a later decision must
    /// rest on what is true now.
    /// </remarks>
    Task<OperationResult<MeituTarget>> ActivateAsync(MeituTarget target, CancellationToken cancellationToken);

    /// <summary>Locates a named control beneath the verified window.</summary>
    OperationResult<UiElementRef> FindKnownElement(MeituTarget target, KnownMeituElement element);

    /// <summary>Invokes a named control, after re-verifying the target.</summary>
    Task<OperationResult<Unit>> InvokeKnownElementAsync(
        MeituTarget target, KnownMeituElement element, CancellationToken cancellationToken);

    /// <summary>Sends a named shortcut, after re-verifying that the target holds the foreground.</summary>
    Task<OperationResult<Unit>> SendVerifiedShortcutAsync(
        MeituTarget target, KnownShortcut shortcut, CancellationToken cancellationToken);

    /// <summary>
    /// Hands <paramref name="workingCopyAbsolutePath"/> to Meitu through positively identified
    /// controls, and returns the window the file was opened into.
    /// </summary>
    /// <remarks>
    /// The path is never typed into whatever dialog happens to hold focus. The picker must be
    /// owned by the verified Meitu process and carry the signed window class before its
    /// file-name field is written, and the field is set through the value pattern rather than
    /// keystrokes (§17).
    ///
    /// The returned target is not always the one passed in, and callers must confirm against
    /// the one they get back. Meitu's editor is a <i>separate top-level window</i> from its
    /// start page (Part B1 §7), so a sequence that begins on the start page ends on a window
    /// that did not exist when it started — and confirming against the original would be
    /// looking at the wrong screen.
    /// </remarks>
    Task<OperationResult<MeituTarget>> OpenWorkingCopyAsync(
        MeituTarget target, string workingCopyAbsolutePath, CancellationToken cancellationToken);

    /// <summary>
    /// Watches, read-only, what Meitu does with the document it has just been handed
    /// (Epic 11300 Part B2B §20, §21, §22).
    /// </summary>
    /// <param name="target">The verified editor the file was opened into.</param>
    /// <param name="expectedWorkingCopyFileName">The Working copy that was handed over.</param>
    /// <param name="cancellationToken">Cancellation stops the observation; nothing is ever sent.</param>
    /// <remarks>
    /// This exists because Meitu can enhance a document PrintFlow never asked it to. It keeps the
    /// selected module across document loads, so opening a file while <c>AI变清晰</c> is still
    /// selected starts an enhancement with no PrintFlow input at all — observed live in both
    /// Part B2A and Part B2B.
    ///
    /// The consequence that makes this a separate step rather than a check inside the enhancement
    /// route is §22's correlation requirement. The module's parameter panel — which <i>is</i> the
    /// signed completion signature — survives the document it was used on: it was observed still
    /// matching on an <b>empty editor</b>, with no document loaded at all. So "the completion
    /// markers are showing" says nothing whatever about the document now in front of PrintFlow,
    /// and the only evidence that ties an enhancement to <i>this</i> load is having positively
    /// seen Busy between the open and now. That observation cannot be reconstructed afterwards,
    /// which is why it is taken here and carried forward rather than inferred later.
    ///
    /// Read-only throughout: no control is invoked, no value is written, and a run that observes
    /// nothing is an ordinary outcome rather than a failure.
    /// </remarks>
    Task<OperationResult<MeituLoadObservation>> ObserveLoadedDocumentAsync(
        MeituTarget target,
        string expectedWorkingCopyFileName,
        IAutomationStopSignal stop,
        CancellationToken cancellationToken);

    /// <summary>
    /// Opens the signed Save surface, reads its document-derived default name, cancels without
    /// saving, and confirms it exactly identifies <paramref name="expectedWorkingCopyFileName"/>.
    /// </summary>
    Task<OperationResult<MeituStateSnapshot>> ConfirmWorkingCopyIdentityAsync(
        MeituTarget target,
        string expectedWorkingCopyFileName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs the whole guarded Enhancement sequence: confirm identity, re-acquire the editor,
    /// resolve the signed action, invoke it once, observe Busy, detect completion positively,
    /// and confirm identity again (Epic 11300 Part B2A §5, §6, §10–§15).
    /// </summary>
    /// <param name="target">The verified process and window holding the working copy.</param>
    /// <param name="expectedWorkingCopyFileName">
    /// The Working copy PrintFlow handed over. Enhancement runs only when the document the
    /// editor is holding is exactly this one.
    /// </param>
    /// <param name="cancellationToken">Cancellation stops the sequence and produces no further input.</param>
    /// <remarks>
    /// Deliberately one method rather than an invoke primitive plus a caller-assembled sequence.
    /// The identity probe is not advice about how to use the invoke — it is the precondition
    /// that makes the invoke safe, and §22 requires that a failed probe produce <i>zero</i>
    /// Enhancement invocations. Exposing an unguarded invoke and documenting the required order
    /// would leave that guarantee to whoever writes the next caller.
    ///
    /// Produces no output file, saves nothing, exports nothing and creates no Revision. A
    /// successful result means the state transitions were observed, and nothing about the
    /// resulting image (§16, §20).
    /// </remarks>
    /// <param name="stop">
    /// The operator's Stop channel, and the phases this run reports back through it
    /// (Epic 11300 Part D2A §4, §9). Required rather than optional: a caller who could omit it
    /// would produce a run that cannot be stopped, and that is not a default anyone should be
    /// able to reach by accident. Pass <c>InertAutomationStopSignal.Instance</c> to say
    /// explicitly that nothing can stop this call.
    /// </param>
    Task<OperationResult<MeituEnhancementOutcome>> RunEnhancementAsync(
        MeituTarget target,
        string expectedWorkingCopyFileName,
        IAutomationStopSignal stop,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs the C1 Background Removal action through Busy and positive completion, then returns
    /// to the ordinary editor and reconfirms exact document identity. Produces no file.
    /// </summary>
    /// <param name="stop">The operator's Stop channel; see <see cref="RunEnhancementAsync"/>.</param>
    Task<OperationResult<MeituBackgroundRemovalOutcome>> RunBackgroundRemovalAsync(
        MeituTarget target,
        string expectedWorkingCopyFileName,
        BackgroundRemovalDecision modeDecision,
        IAutomationStopSignal stop,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the loaded editor to its signed empty state through the signed close control.
    /// </summary>
    /// <remarks>
    /// Exists so the synthetic workspace can be deleted only once Meitu has positively let go
    /// of the file, and so a document left loaded by an earlier run can be cleared before a new
    /// Working copy is opened (§2, §28). It closes a document; it never saves one, and it never
    /// answers a prompt — an owned modal appearing in response stops the sequence (§19).
    /// </remarks>
    Task<OperationResult<MeituTarget>> CloseDocumentAsync(
        MeituTarget target, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the finished result to <paramref name="destinationAbsolutePath"/> through the
    /// signed export route (Epic 11300 Part B2B §5–§12).
    /// </summary>
    /// <param name="target">The verified editor holding the enhanced document.</param>
    /// <param name="expectedWorkingCopyFileName">
    /// The Working copy the editor must still be showing. Re-checked immediately before the
    /// Save surface is raised.
    /// </param>
    /// <param name="observedDocumentIdentity">
    /// The value the signed Save probe read moments ago, so the editor can be re-classified
    /// without a further Save invocation.
    /// </param>
    /// <param name="destinationAbsolutePath">
    /// Where the result must land. Resolved by the adapter from a managed
    /// <c>WorkspaceFileRef</c> for the producing attempt; no caller above Infrastructure can
    /// state a path, because no signature above Infrastructure carries one (§7).
    /// </param>
    /// <param name="cancellationToken">Cancellation stops before any further input is produced.</param>
    /// <remarks>
    /// Returns what was <i>observed</i>, never a verdict. A successful result means the signed
    /// controls were set, read back exactly, and the confirm control was invoked once — and
    /// nothing at all about whether a file exists. §13 is explicit that a dialog closing is not
    /// output success, and this method has no way to make that claim: it never looks at the
    /// filesystem, so the caller has to.
    ///
    /// Nothing is retried. An export whose destination dialog will not close is backed out
    /// through the signed cancel control rather than invoked a second time, because a confirm
    /// that was already accepted and a confirm that was ignored look identical from here.
    /// </remarks>
    /// <param name="stop">
    /// The operator's Stop channel; see <see cref="RunEnhancementAsync"/>. A stop that arrives
    /// while the destination dialog is open backs out through the signed cancel control that is
    /// already part of this validated route, rather than leaving a modal blocking the editor
    /// (Part D2A §14). A stop that arrives after the confirm has been invoked changes nothing
    /// here: PrintFlow cannot cancel a filesystem write and does not pretend to (§15).
    /// </param>
    Task<OperationResult<MeituExportEvidence>> ExportResultAsync(
        MeituTarget target,
        string expectedWorkingCopyFileName,
        string observedDocumentIdentity,
        string destinationAbsolutePath,
        IAutomationStopSignal stop,
        CancellationToken cancellationToken);

    /// <summary>
    /// Dismisses Meitu's post-save confirmation surface, if it is up, through its signed close
    /// control (Epic 11300 Part B2B §24).
    /// </summary>
    /// <remarks>
    /// Separate from the export so that a failure to tidy the screen can never be confused with
    /// a failure to produce the file (§26). It is also the only Meitu-owned surface PrintFlow
    /// dismisses anywhere, and it earns that exemption by being positively identified from
    /// signed markers <i>and</i> a signed close control: the modified-document prompt that §25
    /// discusses carries neither, so nothing here can reach it.
    ///
    /// No surface present is a success, not a failure. The surface is a Meitu setting the
    /// operator can switch off, so requiring it would make cleanup depend on a preference.
    /// </remarks>
    Task<OperationResult<bool>> DismissExportResultSurfaceAsync(
        MeituTarget target, CancellationToken cancellationToken);

    /// <summary>
    /// Invokes the exact signed control that abandons the operation Meitu is currently running,
    /// once, and then observes what Meitu actually does (Epic 11300 Part D2A §9, §11).
    /// </summary>
    /// <param name="target">The verified process and window running the operation.</param>
    /// <param name="operation">
    /// Which operation the caller believes is running. Checked rather than trusted: the signed
    /// evidence must cover it <b>and</b> its own Busy signature must match the screen right now,
    /// because Meitu raises one shared progress mask for both operations and the control alone
    /// therefore says nothing about which one it would cancel (§7).
    /// </param>
    /// <param name="expectedWorkingCopyFileName">The Working copy this attempt handed over.</param>
    /// <param name="cancellationToken">
    /// Cancellation stops the <i>observation</i> after the invocation. It never causes a second
    /// invocation and never suppresses the first.
    /// </param>
    /// <remarks>
    /// This is the only method in this interface whose whole purpose is to <i>stop</i> work, and
    /// it is deliberately not expressible as anything more general. There is no
    /// <c>InvokeCancelByName</c>, no <c>FindCancelElement</c> and no way for a caller to name
    /// the control — the parameters carry an operation and a file name, so a future caller has
    /// no vocabulary in which to ask for an arbitrary element to be clicked (§8).
    /// <para>
    /// It invokes <b>at most once</b>. §9 forbids automatic retry, and the reason is the same
    /// one that governs the export confirm: a cancel that was accepted and a cancel that was
    /// ignored look identical from here, so a second invocation would be a coin flip on an
    /// irreversible-in-effect action.
    /// </para>
    /// <para>
    /// It returns what it <i>observed</i> rather than a verdict, which is why
    /// <see cref="MeituCancelOutcome"/> has no <c>Succeeded</c> member. §11 requires the
    /// post-cancel screen to be determined live rather than assumed, and this slice's evidence
    /// explicitly did not establish it — so the record carries the state Meitu was actually left
    /// in, including when that state is <c>Unknown</c>.
    /// </para>
    /// <para>
    /// It never terminates anything. No process API is reachable from here, and a cancel that
    /// cannot be resolved is a refusal with nothing sent, never an escalation (§3, §10).
    /// </para>
    /// </remarks>
    Task<OperationResult<MeituCancelOutcome>> CancelRunningOperationAsync(
        MeituTarget target,
        MeituOperation operation,
        string? expectedWorkingCopyFileName,
        CancellationToken cancellationToken);

    /// <summary>Captures the target window as local failure evidence.</summary>
    OperationResult<EvidenceRef> CaptureEvidence(MeituTarget target, string reason);
}

/// <summary>
/// What one guarded cancel invocation positively observed (Epic 11300 Part D2A §11, §12).
/// </summary>
/// <param name="Operation">The operation whose Busy was correlated before the invocation.</param>
/// <param name="BusyBeforeCancel">The observation in which that operation was positively Busy.</param>
/// <param name="LeftBusy">
/// Whether Meitu positively stopped matching the operation's Busy signature after the single
/// invocation. False means the cancel was sent and the operation was still running when
/// observation stopped — which is reported, not retried.
/// </param>
/// <param name="StateAfterCancel">
/// What was actually read after the invocation. Reported rather than predicted: §11 requires
/// the post-cancel screen to be observed rather than assumed.
/// </param>
/// <remarks>
/// <see cref="StateAfterCancel"/> needs one caveat to be read correctly, and it is a caveat
/// about <i>scope</i> rather than about Meitu. The read behind it is the fast signed-marker one
/// — it asks only whether this operation's own Busy and completion markers are showing, because
/// that is the question a cancel has to answer inside a Busy window that can last under two
/// seconds. It is <b>not</b> the full <see cref="MeituStateClassifier"/> pass, so
/// <c>MeituStartingState.Unknown</c> here means "not this operation's Busy", never "Meitu is on
/// a screen PrintFlow cannot recognise". <see cref="MeituStateSnapshot.Observation"/> carries the
/// markers that were actually seen.
/// <para>
/// The live post-cancel screens are recorded in the signed Busy-cancel evidence rather than
/// asserted here: a cancelled 变清晰 returns to the ordinary editor tool list with the document
/// still loaded, and a cancelled 抠图 stays inside the 抠图 page with its own controls showing.
/// Neither is <c>KnownEditorWithExpectedWorkingCopy</c>, which is exactly why §11 forbids
/// assuming it — the next attempt re-enters through the ordinary readiness path and proves the
/// state for itself.
/// </para>
/// </remarks>
/// <remarks>
/// There is deliberately no <c>Succeeded</c>, no <c>DocumentIntact</c> and no
/// <c>ReadyForRetry</c> member anywhere on this record. A cancel that worked perfectly is still
/// an attempt that produced nothing (§12), and the one inference this type must not make
/// available is that leaving Busy means the editor is back in a safe, input-eligible state.
/// The next attempt re-enters through the ordinary readiness path and proves that for itself.
/// </remarks>
public sealed record MeituCancelOutcome(
    MeituOperation Operation,
    MeituStateSnapshot BusyBeforeCancel,
    bool LeftBusy,
    MeituStateSnapshot StateAfterCancel);

/// <summary>
/// What Meitu did with a document on its own initiative between the open and the first thing
/// PrintFlow asked of it (Epic 11300 Part B2B §20, §21, §22).
/// </summary>
/// <param name="PhaseAtOpen">The signed enhancement phase read immediately after the file was handed over.</param>
/// <param name="AutoStartedEnhancement">
/// Whether Busy was positively observed during <i>this</i> load. The single fact that licenses
/// treating an enhancement nobody asked for as this attempt's enhancement.
/// </param>
/// <param name="Busy">The observation Busy was seen in, or <c>null</c>.</param>
/// <param name="Completion">The observation completion was seen in, or <c>null</c>.</param>
/// <remarks>
/// <see cref="AutoStartedEnhancement"/> is deliberately not derivable from
/// <see cref="Completion"/>. A completion match with no Busy behind it is the stale-module
/// reading §21 warns about — the panel from the previous document, which was observed still
/// matching on an editor holding nothing — and it must not become an enhancement claim.
/// </remarks>
public sealed record MeituLoadObservation(
    MeituEnhancementPhase PhaseAtOpen,
    bool AutoStartedEnhancement,
    MeituStateSnapshot? Busy,
    MeituStateSnapshot? Completion);

/// <summary>
/// What one guarded export run positively set and invoked (Epic 11300 Part B2B §8–§13).
/// </summary>
/// <param name="RequestedBaseName">The output base name written to the Save surface and read back exactly.</param>
/// <param name="ConfirmedFormatValue">The format value read back from the signed selector before anything was invoked.</param>
/// <param name="DestinationDialogTitle">The title of the destination dialog that was actually driven.</param>
/// <param name="RequestedAbsolutePath">The full path written into that dialog and read back exactly.</param>
/// <remarks>
/// Every member is something that was read back off the screen, and there is deliberately no
/// <c>Succeeded</c>, <c>OutputExists</c>, <c>ByteLength</c> or <c>Sha256</c> anywhere on it. The
/// record says what PrintFlow asked for; whether it got it is a question only the filesystem
/// answers, and keeping the two in different types is what stops the first from being read as
/// the second (§13).
/// </remarks>
public sealed record MeituExportEvidence(
    string RequestedBaseName,
    string ConfirmedFormatValue,
    string DestinationDialogTitle,
    string RequestedAbsolutePath);

/// <summary>
/// What one guarded Enhancement run positively observed (Epic 11300 Part B2A §16).
/// </summary>
/// <param name="Target">The verified target, refreshed after the run.</param>
/// <param name="ObservedDocumentIdentity">
/// The exact document-derived value the signed Save surface exposed, read twice and equal both
/// times.
/// </param>
/// <param name="IdentityBeforeEnhancement">The confirming state read immediately before the action.</param>
/// <param name="Busy">The observation in which the signed Busy markers were positively seen.</param>
/// <param name="Completion">The observation in which the signed completion markers were positively seen.</param>
/// <param name="IdentityAfterEnhancement">The confirming state read after completion.</param>
/// <remarks>
/// Every member is an observation rather than a verdict, and there is deliberately no
/// <c>OutputPath</c>, <c>Succeeded</c> or <c>Revision</c> anywhere on it. The record is the
/// exact scope §16 permits this slice to claim: the transitions happened, to this document, in
/// this order. Whether the enhanced image is any good — indeed whether it can be got out of
/// Meitu at all — is Part B2B, and no field here can be mistaken for an answer to it.
/// </remarks>
public sealed record MeituEnhancementOutcome(
    MeituTarget Target,
    string ObservedDocumentIdentity,
    MeituStateSnapshot IdentityBeforeEnhancement,
    MeituStateSnapshot Busy,
    MeituStateSnapshot Completion,
    MeituStateSnapshot IdentityAfterEnhancement);

/// <summary>What one guarded C1 Background Removal run positively observed.</summary>
public sealed record MeituBackgroundRemovalOutcome(
    MeituTarget Target,
    string ObservedDocumentIdentity,
    BackgroundRemovalDecision ModeDecision,
    string ObservedAutomaticModeName,
    MeituStateSnapshot IdentityBeforeAction,
    MeituStateSnapshot Busy,
    MeituStateSnapshot Completion,
    MeituStateSnapshot IdentityAfterCompletion);
