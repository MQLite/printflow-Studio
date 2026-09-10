using System.Collections.Immutable;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// The accepted identity and recognition signals for Meitu on this workstation, as read from
/// the signed Epic 11000 preset chain (Epic 11300 Part A §7).
/// </summary>
/// <param name="ExecutablePath">The one executable PrintFlow may launch or match against.</param>
/// <param name="ExecutableSha256">The accepted binary digest for that executable.</param>
/// <param name="AcceptedVersion">The accepted Meitu runtime version, recorded as evidence.</param>
/// <param name="UiLanguage">The Meitu UI language the recognition signals were captured in.</param>
/// <param name="AcceptedWindowTitles">
/// Titles observed on the accepted runtime. Corroborating evidence only — process ownership,
/// not a title, is what identifies a window (§9).
/// </param>
/// <param name="WelcomeWindowTitle">
/// The exact window title Epic 11000 observed on the clean start page.
/// </param>
/// <param name="WelcomeMarkers">
/// Stable structural labels of the clean start page, extracted from the signed clean-start
/// evidence file. Deliberately excludes theme colour and the rotating advertisement, which
/// Epic 11000 recorded as unsafe recognition inputs.
/// </param>
/// <param name="StartPageCard">
/// The structural shape of a start-page card and the label that titles it, or <c>null</c> when
/// the verified chain vouches for no such evidence — in which case PrintFlow will not invoke a
/// start-page entry at all.
/// </param>
/// <param name="FileDialog">
/// The signature of the picker the start-page card opens, or <c>null</c> when none has been
/// observed and signed. Null means the open path stops rather than guessing at a dialog shape.
/// </param>
/// <param name="DocumentIdentity">
/// The signed Save-dialog identity probe, or <c>null</c>. Null leaves
/// <see cref="MeituStartingState.KnownEditorWithExpectedWorkingCopy"/> unreachable.
/// </param>
/// <param name="EditorEmpty">
/// The signature of the editor with no document loaded, or <c>null</c>. Null leaves
/// <see cref="MeituStartingState.KnownEditorEmpty"/> unreachable — which is the Part A
/// position, kept until positive markers for that screen are actually observed (§10, §15).
/// </param>
/// <param name="CloseDocument">
/// The signed route that returns the loaded editor to its empty state, or <c>null</c>. Null
/// means PrintFlow will not attempt to close a document at all — it will report that the
/// editor is holding one and leave it to the operator.
/// </param>
/// <param name="Enhancement">
/// The signed Enhancement action, Busy signature and completion signature, or <c>null</c>.
/// Null leaves <see cref="MeituStartingState.Busy"/> unreachable and every Enhancement route
/// refused, on the same fail-closed principle as the members above.
/// </param>
/// <param name="Export">
/// The signed route that gets a finished result out of Meitu, or <c>null</c>. Null leaves every
/// export refused, and an Enhancement PrintFlow cannot export produces no output file — which
/// in turn leaves <c>ProcessAsync</c> unable to return success and no Revision creatable.
/// </param>
/// <remarks>
/// This record is the adapter's <i>only</i> source of Meitu facts. Nothing in the adapter
/// hard-codes a path, a version or a label, so a Meitu upgrade is a preset revalidation rather
/// than a code change (§7).
///
/// The four evidence-backed members added in Part B1 are nullable on purpose, and the
/// nullability is the safety mechanism rather than a convenience: a state whose signature the
/// signed chain does not carry stays unreachable, so "PrintFlow has not been shown this screen"
/// and "PrintFlow refuses to act on this screen" are the same condition (§10).
/// </remarks>
public sealed record MeituBaseline(
    string ExecutablePath,
    Sha256 ExecutableSha256,
    string AcceptedVersion,
    string UiLanguage,
    ImmutableArray<string> AcceptedWindowTitles,
    string WelcomeWindowTitle,
    ImmutableArray<string> WelcomeMarkers,
    MeituCardShape? StartPageCard = null,
    MeituFileDialogSignature? FileDialog = null,
    MeituDocumentIdentitySignature? DocumentIdentity = null,
    MeituEditorSignature? EditorEmpty = null,
    MeituCloseDocumentSignature? CloseDocument = null,
    MeituEnhancementSignature? Enhancement = null,
    MeituExportSignature? Export = null,
    MeituBackgroundRemovalSignature? BackgroundRemoval = null,
    MeituBusyCancelSignature? BusyCancel = null)
{
    /// <summary>The loaded-editor portion of the signed identity evidence.</summary>
    public MeituEditorSignature? EditorWithWorkingCopy => DocumentIdentity?.Editor;
}

/// <summary>
/// The signed control that abandons an operation Meitu is currently running
/// (Epic 11300 Part D2A §6, §7, §8).
/// </summary>
/// <param name="Control">
/// The full signature of the actionable element itself. Unlike the enhancement and
/// close-document routes, the automation name here belongs to the button rather than to a
/// label above it, so there is no marker-to-owner walk and no ancestor depth: owner depth is
/// zero, and that is signed evidence rather than an assumption.
/// </param>
/// <param name="RequiredAncestorClassNames">
/// The control-view ancestor classes the resolved element must sit beneath, innermost first —
/// observed as <c>LoadingMaskWidget</c> → <c>SpecialMaskWidget</c> → <c>MaskDialog</c>. The
/// automation id already encodes that path, so this is a second, independent statement of the
/// same fact: it is what stops a coincidentally-named id from passing as the progress mask's
/// own cancel.
/// </param>
/// <param name="ConfirmedOperations">
/// The operations this cancel has been positively observed to abandon, by the stable English
/// <c>MeituOperation</c> name. An operation absent from this set is refused, whatever the
/// control walk finds.
/// </param>
/// <remarks>
/// One signature, not one per operation, and the reason is a live observation rather than a
/// simplification. Meitu raises a <b>single shared</b> <c>LoadingMaskWidget</c> progress mask
/// for both 抠图 and AI变清晰, and both produced the identical automation id
/// <c>MainWindow.MaskDialog.MaskCenterWidget.LoadingMaskWidget.cancel</c>. Signing two
/// per-operation controls would have fabricated a distinction the workstation does not have.
/// <para>
/// The consequence is the important part, and it is why <see cref="ConfirmedOperations"/>
/// exists alongside <see cref="MeituBusyCancelRule"/>'s Busy correlation: because the control
/// cannot say which operation is running, resolving it is <b>never</b> evidence that the
/// expected operation is the one being cancelled. That has to come from the operation's own
/// signed Busy signature matching at the same instant (§7).
/// </para>
/// <para>
/// Nullable like every other evidence-backed member, and for the same fail-closed reason: a
/// preset that vouches for no cancel evidence leaves Stop unable to invoke anything, and
/// PrintFlow reports that it stopped its own orchestration without cancelling Meitu rather than
/// looking for a button named 取消 (§10).
/// </para>
/// </remarks>
public sealed record MeituBusyCancelSignature(
    MeituControlSignature Control,
    ImmutableArray<string> RequiredAncestorClassNames,
    ImmutableArray<string> ConfirmedOperations)
{
    /// <summary>Whether the signed evidence positively covers <paramref name="operation"/>.</summary>
    /// <remarks>
    /// Exact, ordinal comparison against the stable English enum name. A loose match here would
    /// let an operation nobody observed inherit the confidence of one that was.
    /// </remarks>
    public bool Covers(string operation) =>
        !ConfirmedOperations.IsDefaultOrEmpty &&
        ConfirmedOperations.Contains(operation, StringComparer.Ordinal);
}

/// <summary>
/// The signed, non-writing route that identifies the document currently loaded in Meitu.
/// </summary>
/// <param name="Editor">Positive markers for the loaded editor before Save is invoked.</param>
/// <param name="SaveMarkerName">The exact text marker that anchors the Save control walk.</param>
/// <param name="SaveControl">The structural relationship from that marker to its owning control.</param>
/// <param name="DialogTitle">The exact title of the resulting owned Save surface.</param>
/// <param name="DialogClassName">The exact Win32 class of that surface.</param>
/// <param name="FileNameControl">The value control that exposes the document-derived basename.</param>
/// <param name="CancelControl">The only dialog action this probe may invoke.</param>
/// <param name="OutputBaseNameSuffix">
/// The exact suffix Meitu appends to the source basename. It is part of an exact derived-value
/// comparison; it is never removed with a prefix or substring rule.
/// </param>
public sealed record MeituDocumentIdentitySignature(
    MeituEditorSignature Editor,
    string SaveMarkerName,
    MeituCardShape SaveControl,
    string DialogTitle,
    string DialogClassName,
    MeituControlSignature FileNameControl,
    MeituControlSignature CancelControl,
    string OutputBaseNameSuffix);

/// <summary>
/// The signature of the file picker Meitu opens, as observed and signed on this workstation
/// (Epic 11300 Part B1 §8).
/// </summary>
/// <param name="Kind">
/// What the picker turned out to be, recorded so the report and the failure text can say so
/// plainly rather than implying a Windows common dialog that may not be one.
/// </param>
/// <param name="WindowClassName">The picker window's class, where it is stable.</param>
/// <param name="AcceptedTitles">Titles observed for it. Corroborating evidence, never the authority.</param>
/// <param name="FileNameAutomationId">The automation id of the field the path is written to.</param>
/// <param name="FileNameControlType">That field's control type, checked before it is written to.</param>
/// <param name="ConfirmAutomationId">The automation id of the control that accepts the file.</param>
/// <param name="ConfirmControlType">That control's control type.</param>
/// <remarks>
/// Part A assumed a Windows common dialog (<c>#32770</c>) because that is what Meitu's Ctrl+O
/// would conventionally raise. Part B1 does not carry that assumption forward: the shape is
/// whatever the workstation was actually seen to produce, and if the workstation disproves the
/// common-dialog assumption the evidence — not the code — is what changes (§8).
/// </remarks>
public sealed record MeituFileDialogSignature(
    string Kind,
    string WindowClassName,
    ImmutableArray<string> AcceptedTitles,
    string FileNameAutomationId,
    string FileNameControlType,
    string ConfirmAutomationId,
    string ConfirmControlType);

/// <summary>Where the expected working copy's name has to appear before it counts as shown.</summary>
/// <remarks>
/// Signed rather than assumed, because "the file name appears somewhere in the UI" is a much
/// weaker claim than it looks: a recent-files list on a start page contains file names too. The
/// evidence records the place the name was actually observed, and the classifier accepts it
/// only there (§13, §14).
/// </remarks>
public enum MeituFileNameLocation
{
    /// <summary>Only the window title counts.</summary>
    WindowTitle,

    /// <summary>Only an automation name beneath the window counts.</summary>
    VisibleText,

    /// <summary>Either counts.</summary>
    TitleOrVisibleText,
}

/// <summary>
/// The signature of a Meitu editor screen (Epic 11300 Part B1 §14, §15).
/// </summary>
/// <param name="WindowTitle">The exact window title observed for this screen.</param>
/// <param name="RequiredMarkers">
/// Positive structural markers that must be visible. Positive, never "the expected file name is
/// absent": absence is not evidence, and a screen recognised by what is missing from it would
/// match a failed read just as well as the real thing (§15).
/// </param>
/// <param name="MinimumRequiredMarkers">How many of them must match.</param>
/// <param name="FileNameLocation">Where a document name has to appear, when the state names one.</param>
/// <param name="OpenControl">
/// The control on this screen that raises Meitu's file picker, when the screen has one. Present
/// on the empty editor and absent from an editor already showing a document.
/// </param>
public sealed record MeituEditorSignature(
    string WindowTitle,
    ImmutableArray<string> RequiredMarkers,
    int MinimumRequiredMarkers,
    MeituFileNameLocation FileNameLocation,
    MeituControlSignature? OpenControl = null);

/// <summary>
/// A single control identified by the properties signed evidence records for it
/// (Epic 11300 Part B1 §4, §8).
/// </summary>
/// <param name="Name">The exact automation name, or empty when the control has none.</param>
/// <param name="AutomationIdContains">
/// A substring the automation id must contain — for Qt, the owning widget's path segment.
/// Anchors the control to the part of the tree the evidence describes without pinning a full
/// runtime id whose leading segments may vary.
/// </param>
/// <param name="ControlTypeName">The control type.</param>
/// <param name="ClassName">The framework class.</param>
/// <param name="RequiredPattern">The pattern the control must expose before it is used.</param>
/// <remarks>
/// Deliberately not "find the button called X". Meitu's editor has two controls whose id ends in
/// <c>openButton</c> — a toolbar one with no name, and the empty-state one named 打开图片 — so a
/// name on its own, or an id suffix on its own, each match something the evidence does not
/// describe. Requiring both, plus the type, class and pattern, and then requiring the match to
/// be <i>unique</i>, is what makes the resolution a statement about the observed control rather
/// than about whichever candidate came first.
/// </remarks>
public sealed record MeituControlSignature(
    string Name,
    string AutomationIdContains,
    string ControlTypeName,
    string ClassName,
    UiPatternKind RequiredPattern);

/// <summary>
/// Supplies the verified <see cref="MeituBaseline"/>, or refuses.
/// </summary>
/// <remarks>
/// Infrastructure-only on purpose: an executable path is an environment fact, and letting it
/// surface in <c>PrintFlow.Workflow</c> would give the workflow layer a way to name a program
/// to run. Workflow sees <c>IMeituProcessor</c> and nothing else (§26).
/// </remarks>
public interface IMeituBaselineProvider
{
    /// <summary>
    /// Returns the accepted Meitu identity, or a failure when the signed chain cannot be
    /// verified.
    /// </summary>
    /// <remarks>
    /// Fails closed in every ambiguous case — missing manifest, hash mismatch, absent
    /// <c>meituContract</c>. There is no "assume the default install location" branch, because
    /// searching the machine for a plausible <c>XiuXiu.exe</c> and picking one is precisely
    /// what §7 forbids.
    /// </remarks>
    OperationResult<MeituBaseline> GetVerifiedBaseline();
}

/// <summary>
/// The signed route that returns the loaded editor to its empty state
/// (Epic 11300 Part B2A §2, §28).
/// </summary>
/// <param name="MarkerName">The exact text marker that anchors the close-control walk.</param>
/// <param name="Control">The structural relationship from that marker to its owning control.</param>
/// <remarks>
/// Present because B2A needs the editor emptied twice for reasons that have nothing to do with
/// tidiness. Before the run, §2 forbids enhancing the document B1.1 left loaded — its backing
/// file was deleted underneath Meitu — so the editor has to be emptied before a fresh Working
/// copy can be opened through the signed path. After the run, §28 permits deleting the
/// synthetic workspace only once Meitu has positively let go of it.
///
/// Signed rather than assumed, and structural rather than by name, for the same reason as
/// every other control in this adapter: 关闭图片 names a <c>QLabel</c>, and the thing that
/// actually closes the document is the <c>IconTextButton</c> that owns it.
/// </remarks>
public sealed record MeituCloseDocumentSignature(string MarkerName, MeituCardShape Control);

/// <summary>
/// The signed Enhancement route: which control performs it, what Meitu looks like while it is
/// running, and what positively says it has finished (Epic 11300 Part B2A §8, §9, §13, §14).
/// </summary>
/// <param name="ActionMarkerName">The exact text marker that anchors the action walk.</param>
/// <param name="ActionControl">The structural relationship from that marker to the invokable control.</param>
/// <param name="Busy">Positive markers for Meitu computing.</param>
/// <param name="Completion">Positive markers for Meitu having finished.</param>
/// <remarks>
/// The three parts are separate because they are three separate claims, each of which had to be
/// observed live before it could be signed. Bundling them into one "enhancement works" flag
/// would let an unproven completion rule ride into production on the strength of a proven
/// target rule.
/// </remarks>
public sealed record MeituEnhancementSignature(
    string ActionMarkerName,
    MeituOwnedControlShape ActionControl,
    MeituBusySignature Busy,
    MeituCompletionSignature Completion);

/// <summary>
/// Positive markers that identify Meitu as computing (Epic 11300 Part B2A §12, §13).
/// </summary>
/// <param name="RequiredMarkers">Automation names observed only while processing is in flight.</param>
/// <param name="MinimumRequiredMarkers">How many of them must be visible.</param>
/// <remarks>
/// Positive, and deliberately not "the editor stopped looking normal". Busy outranks every
/// content state in <see cref="MeituStateClassifier"/>, so a Busy rule phrased as an absence
/// would suppress document recognition on any read that happened to come back thin — turning a
/// flaky automation read into a claim that Meitu is working.
/// </remarks>
public sealed record MeituBusySignature(
    ImmutableArray<string> RequiredMarkers,
    int MinimumRequiredMarkers);

/// <summary>
/// Positive markers that identify Enhancement as finished (Epic 11300 Part B2A §14).
/// </summary>
/// <param name="RequiredMarkers">Automation names Meitu shows only after the result exists.</param>
/// <param name="MinimumRequiredMarkers">How many of them must be visible.</param>
/// <param name="RequiresBusyAbsent">
/// Whether the Busy signature must additionally have stopped matching. Recorded rather than
/// assumed, because whether the two states can legitimately overlap is a property of the
/// application, not of PrintFlow.
/// </param>
/// <remarks>
/// §14 is explicit that "Busy disappeared" is not completion, and this record is the shape that
/// makes obeying it structural rather than a matter of remembering. There is no constructor
/// that produces a completion signature with no positive markers:
/// <see cref="MeituEnhancementRule"/> refuses an empty marker list, so the only way to reach a
/// completion verdict is through something Meitu positively showed.
/// </remarks>
public sealed record MeituCompletionSignature(
    ImmutableArray<string> RequiredMarkers,
    int MinimumRequiredMarkers,
    bool RequiresBusyAbsent);

/// <summary>The signed C1 route that enters and leaves Meitu's live 抠图 page.</summary>
public sealed record MeituBackgroundRemovalSignature(
    string ActionMarkerName,
    MeituOwnedControlShape ActionControl,
    string ReturnMarkerName,
    MeituOwnedControlShape ReturnControl,
    string ObservedAutomaticModeName,
    MeituBackgroundRemovalModePolicy ModePolicy,
    bool AutoStartsOnEntry,
    MeituBusySignature Busy,
    MeituCompletionSignature Completion);

/// <summary>Product authority governing the automatic cutout mode.</summary>
public enum MeituBackgroundRemovalModePolicy
{
    OperatorOrReviewedContentDecision,
}

/// <summary>
/// The signed route that gets a finished result out of Meitu and onto a path PrintFlow chose
/// (Epic 11300 Part B2B §5, §6, §8, §11, §12).
/// </summary>
/// <param name="SurfaceTitle">The exact title of the owned Save surface the editor's Save control raises.</param>
/// <param name="SurfaceClassName">The exact Win32 class of that surface.</param>
/// <param name="FileNameControl">The value control carrying the output base name, without extension.</param>
/// <param name="FormatControl">The value control carrying the output format.</param>
/// <param name="RequiredFormatValue">
/// The exact value <see cref="FormatControl"/> must read before anything is invoked. Not
/// inferred from the file name's extension: this surface has a format selector of its own, and
/// §11 requires the format to be positively confirmed or the export to fail closed.
/// </param>
/// <param name="SaveAsControl">The control that opens the destination dialog.</param>
/// <param name="Destination">The dialog in which the controlled destination is actually named.</param>
/// <param name="Result">Meitu's own post-save confirmation surface.</param>
/// <remarks>
/// The member that is <b>absent</b> here is the important one. The Save surface also exposes a
/// <c>folderEdit</c> <c>QLineEdit</c> showing the destination directory, and PrintFlow does not
/// use it — see <see cref="MeituExportDestinationSignature"/> for the live observation that ruled
/// it out. There is deliberately no signature member for it, so no future caller can reach for
/// it without adding evidence and a reason first.
/// </remarks>
public sealed record MeituExportSignature(
    string SurfaceTitle,
    string SurfaceClassName,
    MeituControlSignature FileNameControl,
    MeituControlSignature FormatControl,
    string RequiredFormatValue,
    MeituControlSignature SaveAsControl,
    MeituExportDestinationSignature Destination,
    MeituExportResultSignature Result,
    MeituExportFormatSelectionSignature? FormatSelection = null);

/// <summary>
/// The signed, Meitu-specific fallback that changes a Save surface from its JPG default to PNG.
/// </summary>
/// <remarks>
/// This is deliberately not a generic popup or coordinate contract. It describes one transient
/// Meitu window and one item beneath the already-signed format combo. Bounds, runtime ids and the
/// point itself are absent: the accepted route derives a fresh clickable point from the live item
/// only after every structural and ownership check has passed.
/// </remarks>
public sealed record MeituExportFormatSelectionSignature(
    string InitialFormatValue,
    string RequiredFormatValue,
    MeituControlSignature FormatControl,
    ImmutableArray<UiPatternKind> FormatControlRequiredPatterns,
    string PopupTitle,
    string PopupWindowClassName,
    string PopupUiaClassName,
    string PopupControlType,
    ImmutableArray<UiPatternKind> PopupRequiredPatterns,
    string ItemName,
    string ItemControlType,
    string ItemClassName,
    string ItemAutomationId,
    string RequiredParentControlType,
    string RequiredParentClassName,
    string RequiredComboAncestorControlType,
    string RequiredComboAncestorClassName,
    int ComboAncestorDepth,
    MeituExportFormatActivation RequiredActivation,
    bool SaveSurfaceMustRemainForeground,
    bool PopupMustDisappear);

/// <summary>The only pointer-backed Meitu action accepted by the signed export evidence.</summary>
public enum MeituExportFormatActivation
{
    RuntimeDerivedClickablePoint,
}

/// <summary>
/// The dialog in which the export's destination directory is named
/// (Epic 11300 Part B2B §6, §10).
/// </summary>
/// <param name="WindowClassName">The dialog's Win32 class.</param>
/// <param name="AcceptedTitles">Titles observed for it. Corroborating evidence, never the authority.</param>
/// <param name="FileNameAutomationId">The automation id of the field the full path is written to.</param>
/// <param name="FileNameControlType">That field's control type, which is what makes the match unique.</param>
/// <param name="ConfirmAutomationId">The automation id of the control that writes the file.</param>
/// <param name="ConfirmControlType">That control's control type.</param>
/// <param name="CancelAutomationId">The automation id of the control that backs out writing nothing.</param>
/// <param name="CancelControlType">That control's control type.</param>
/// <remarks>
/// This dialog exists in the route because of a live observation that a read-back check did not
/// catch, and it is worth recording next to the thing it disproves. Invoking 保存 on the Save
/// surface writes the file named by <c>fileNameEdit</c> in the format named by
/// <c>formatCombo</c> — both of which a value write does control — into the directory Meitu
/// remembers, <b>not</b> the one <c>folderEdit</c> displays. A value written to
/// <c>folderEdit</c> is accepted, reads back exactly, and changes nothing: the export landed in
/// the operator's Downloads folder while the field read the controlled path.
///
/// So the destination is named here instead, in a Windows common dialog whose file-name field
/// takes a full path — the same shape, and the same delivery mechanism, as the open picker
/// Part B1 already drives. §8's read-back rule is kept for every value this route writes; what
/// this record encodes is that read-back alone was not sufficient evidence for <i>that one
/// control</i>, and the route that does not depend on it was chosen instead.
/// </remarks>
public sealed record MeituExportDestinationSignature(
    string WindowClassName,
    ImmutableArray<string> AcceptedTitles,
    string FileNameAutomationId,
    string FileNameControlType,
    string ConfirmAutomationId,
    string ConfirmControlType,
    string CancelAutomationId,
    string CancelControlType);

/// <summary>
/// Meitu's post-save confirmation surface (Epic 11300 Part B2B §13, §24).
/// </summary>
/// <param name="RequiredMarkers">Positive markers that identify it as the save-result surface.</param>
/// <param name="MinimumRequiredMarkers">How many of them must be visible.</param>
/// <param name="CloseControl">The control that dismisses it, having written nothing further.</param>
/// <remarks>
/// Signed because §24 forbids clicking a Meitu-owned surface whose exact action has not been
/// observed, and this one has to be dismissed before the editor can be returned to a neutral
/// state — it disables the editor while it is up.
///
/// It is recorded as a <i>cleanup</i> affordance and never as evidence of where anything landed.
/// The same surface appears for 保存 and for 另存为, and it names no path, so treating it as
/// proof of a successful export would be exactly the "the dialog closed, so it worked" inference
/// §13 rules out. The filesystem is the authority; this is how the screen is tidied afterwards.
/// </remarks>
public sealed record MeituExportResultSignature(
    ImmutableArray<string> RequiredMarkers,
    int MinimumRequiredMarkers,
    MeituControlSignature CloseControl);
