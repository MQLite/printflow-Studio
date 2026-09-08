using System.Collections.Immutable;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>
/// The window classes that tell Photoshop's screens apart (Epic 11400 Part A §7).
/// </summary>
/// <param name="StartScreenMarkerClass">
/// The class of the child window that exists only on the start/home screen, observed as
/// <c>OWL.WelcomeScreenView</c>.
/// </param>
/// <param name="DocumentMarkerClass">
/// The class of the child window that exists only when a document is loaded, observed as
/// <c>OWL.Document</c>.
/// </param>
/// <param name="EditorChromeClasses">
/// Classes present whenever the editor frame is up at all, observed as <c>OWL.MenuBar</c>,
/// <c>OWL.ApplicationBar</c> and <c>OWL.Dock</c>. Their presence without either marker above is
/// what positively identifies an editor holding no document.
/// </param>
/// <remarks>
/// Classes rather than visible text, deliberately. Photoshop's window tree carries the loaded
/// document's measurements and, on some panels, values derived from its content; recognising
/// screens by class means the classifier reads nothing that could describe a customer's
/// artwork. It also means recognition does not move when the UI language does.
/// <para>
/// These are the discriminators live discovery actually produced. The start screen showed ten
/// visible children including <c>OWL.WelcomeScreenView</c> and no <c>OWL.Document</c>; a loaded
/// editor showed sixty-four including <c>OWL.Document</c> and no <c>OWL.WelcomeScreenView</c>.
/// Nothing here was assumed from Photoshop documentation.
/// </para>
/// </remarks>
public sealed record PhotoshopWindowStateSignature(
    string StartScreenMarkerClass,
    string DocumentMarkerClass,
    ImmutableArray<string> EditorChromeClasses);

/// <summary>The active working spaces required by the accepted Photoshop preset.</summary>
public sealed record PhotoshopColourSettingsContract(
    string RgbWorkingSpace,
    string CmykWorkingSpace,
    string GrayWorkingSpace,
    string SpotWorkingSpace)
{
    public override string ToString() =>
        $"RGB: {RgbWorkingSpace}; CMYK: {CmykWorkingSpace}; Gray: {GrayWorkingSpace}; Spot: {SpotWorkingSpace}";
}

/// <summary>
/// The signature of the Open dialog Photoshop raises, as observed and signed on this
/// workstation (Epic 11400 Part A §10).
/// </summary>
/// <param name="WindowClassName">The dialog's window class, observed as <c>#32770</c>.</param>
/// <param name="Title">Its exact title, observed as <c>打开</c>. Corroborating evidence; ownership identifies it.</param>
/// <param name="FileNameControlId">The dialog control id of the filename field, observed as 1148.</param>
/// <param name="FileNameControlClass">That field's window class, observed as <c>ComboBoxEx32</c>.</param>
/// <param name="ConfirmControlId">The control id of the Open button, observed as 1.</param>
/// <param name="ConfirmControlClass">Its class, observed as <c>Button</c>.</param>
/// <param name="CancelControlId">The control id of the Cancel button, observed as 2.</param>
/// <param name="CancelControlClass">Its class, observed as <c>Button</c>.</param>
/// <remarks>
/// Recorded as a shape rather than assumed to be "the standard Windows Open dialog". It happens
/// to be one, but Photoshop adds its own controls to it (an 图像序列 checkbox), and a future
/// version could replace it outright — in which case the evidence, not the code, is what has to
/// change, and until it does the open path refuses rather than writing into whatever now sits
/// at control 1148.
/// </remarks>
public sealed record PhotoshopOpenDialogSignature(
    string WindowClassName,
    string Title,
    int FileNameControlId,
    string FileNameControlClass,
    int ConfirmControlId,
    string ConfirmControlClass,
    int CancelControlId,
    string CancelControlClass);

/// <summary>
/// The signed, non-writing route that identifies the document currently loaded in Photoshop
/// (Epic 11400 Part A §11, §13).
/// </summary>
/// <param name="TitleSeparator">
/// The exact text separating the document's basename from the rest of the window title,
/// observed as <c>" @ "</c> in titles of the form
/// <c>NAME.png @ 100% (图层 1, RGB/8)</c>.
/// </param>
/// <param name="DialogClassName">The identity surface's window class, observed as <c>#32770</c>.</param>
/// <param name="DialogTitle">Its exact title, observed as <c>另存为</c>.</param>
/// <param name="FileNameControlId">
/// The control id of the field pre-filled with the loaded document's basename, observed as 1001.
/// </param>
/// <param name="FileNameControlClass">That field's class, observed as <c>Edit</c>.</param>
/// <param name="AddressControlId">
/// The control id of the address bar, observed as 1001 — the <i>same</i> id as the filename
/// field, which is exactly why the class is recorded alongside it.
/// </param>
/// <param name="AddressControlClass">The address bar's class, observed as <c>ToolbarWindow32</c>.</param>
/// <param name="AddressTextPrefix">
/// The exact prefix the address bar puts before the folder path, observed as <c>"地址: "</c>.
/// Removed by exact prefix match; never by searching the text for something that looks like a
/// path.
/// </param>
/// <param name="CancelControlId">The only control on this surface PrintFlow may press, observed as 2.</param>
/// <param name="CancelControlClass">Its class, observed as <c>Button</c>.</param>
/// <remarks>
/// This exists because Photoshop CC 2019 exposes no UI Automation tree at all — one element for
/// the entire main window, with no children — so there is no document tab, no panel and no
/// automation property anywhere that names the loaded file. Two surfaces do name it, and this
/// record describes both because neither is sufficient alone:
/// <list type="bullet">
///   <item>the main window title, which carries the basename but no folder;</item>
///   <item>the Save As dialog, which carries the basename <i>and</i> the document's own
///         containing folder — verified live to follow the active document across
///         directories rather than remembering the last-used one.</item>
/// </list>
/// Together they bind an absolute path. That matters because a basename alone cannot tell
/// <c>…\Working\A.png</c> from a same-named file somewhere else, and Epic 11400 Part A §12
/// forbids quietly settling for the weaker claim.
/// <para>
/// Nothing on this surface is written and nothing is saved: the probe raises it, reads two
/// values, and presses <see cref="CancelControlId"/>.
/// </para>
/// </remarks>
public sealed record PhotoshopDocumentIdentitySignature(
    string TitleSeparator,
    string DialogClassName,
    string DialogTitle,
    int FileNameControlId,
    string FileNameControlClass,
    int AddressControlId,
    string AddressControlClass,
    string AddressTextPrefix,
    int CancelControlId,
    string CancelControlClass);

/// <summary>One exact native control on Photoshop's signed owned-document discard prompt.</summary>
public sealed record PhotoshopDiscardPromptControlSignature(
    int ControlId,
    string ControlClass,
    string Text);

/// <summary>The signed question that binds the discard prompt to the document just closed.</summary>
public sealed record PhotoshopDiscardPromptMessageSignature(
    int ControlId,
    string ControlClass,
    string TextPrefix,
    string TextSuffix,
    string TruncationMarker,
    int MinimumDocumentNamePrefixLength);

/// <summary>
/// The closed, signed contract for disposing one dirty document after its TIFF is validated.
/// </summary>
/// <remarks>
/// This is deliberately not a generic dialog contract. It describes only the prompt observed
/// after PrintFlow requested closure of an already path-proved document, including the exact
/// question and all three native buttons. The only actionable member is
/// <see cref="DiscardControl"/>; the Save and Cancel controls are required decoys whose presence
/// makes the surface narrower, not alternate actions.
/// </remarks>
public sealed record PhotoshopOwnedDocumentCleanupSignature(
    bool SaveAsCopyMaySubstituteIdentityFileExtension,
    string PromptWindowClassName,
    string PromptTitle,
    PhotoshopDiscardPromptMessageSignature Message,
    PhotoshopDiscardPromptControlSignature SaveControl,
    PhotoshopDiscardPromptControlSignature DiscardControl,
    PhotoshopDiscardPromptControlSignature CancelControl);

/// <summary>One exact runtime Action and the command names Photoshop reports for it.</summary>
public sealed record PhotoshopW1BranchContract(
    WhiteUnderbaseBranch Branch,
    string ActionName,
    ImmutableArray<string> RuntimeCommands);

/// <summary>
/// The verified, closed CMYK + W1 Action contract. Callers never supply any of these strings.
/// </summary>
public sealed record PhotoshopW1ActionContract(
    string ArtifactPath,
    Sha256 ArtifactSha256,
    string SetName,
    ImmutableArray<PhotoshopW1BranchContract> Branches);

/// <summary>
/// The accepted identity and recognition signals for Photoshop on this workstation, as read
/// from the signed Epic 11000 preset chain (Epic 11400 Part A §5).
/// </summary>
/// <param name="ExecutablePath">The one executable PrintFlow may launch or match against.</param>
/// <param name="ExecutableSha256">The accepted binary digest for that executable.</param>
/// <param name="AcceptedProductVersion">The accepted product version, checked before any input.</param>
/// <param name="AcceptedFileVersion">The accepted file version, checked before any input.</param>
/// <param name="UiLanguage">The UI language the recognition signals were captured in.</param>
/// <param name="MainWindowClassName">The main window's class, observed as <c>Photoshop</c>.</param>
/// <param name="NoDocumentWindowTitle">
/// The exact window title Photoshop shows when no document is loaded, observed as
/// <c>Adobe Photoshop CC 2019</c> on both the start screen and an empty editor.
/// </param>
/// <param name="ExcludedInstallations">
/// Other Photoshop installations Epic 11000 explicitly refused. Recorded so a failure can say
/// which binary was found rather than only that the accepted one was not.
/// </param>
/// <param name="WindowStates">
/// The screen discriminators, or <c>null</c> when the verified chain vouches for none — in
/// which case every screen classifies as unknown and nothing is opened.
/// </param>
/// <param name="OpenDialog">
/// The signed Open-dialog shape, or <c>null</c>. Null leaves the open path refused rather than
/// guessing at a dialog.
/// </param>
/// <param name="DocumentIdentity">
/// The signed identity route, or <c>null</c>. Null leaves
/// <see cref="PhotoshopStartingState.KnownEditorWithExpectedDocument"/> unreachable, and
/// therefore leaves every open unconfirmable.
/// </param>
/// <remarks>
/// This record is the adapter's <i>only</i> source of Photoshop facts. Nothing in the adapter
/// hard-codes a path, a version, a control id or a label, so a Photoshop upgrade is a preset
/// revalidation rather than a code change.
///
/// The three evidence-backed members are nullable and the nullability is the safety mechanism
/// rather than a convenience: a surface the signed chain does not carry stays unreachable, so
/// "PrintFlow has not been shown this" and "PrintFlow refuses to act on this" are the same
/// condition.
/// </remarks>
public sealed record PhotoshopBaseline(
    string ExecutablePath,
    Sha256 ExecutableSha256,
    string AcceptedProductVersion,
    string AcceptedFileVersion,
    string UiLanguage,
    string MainWindowClassName,
    string NoDocumentWindowTitle,
    ImmutableArray<string> ExcludedInstallations,
    PhotoshopWindowStateSignature? WindowStates = null,
    PhotoshopOpenDialogSignature? OpenDialog = null,
    PhotoshopDocumentIdentitySignature? DocumentIdentity = null,
    PhotoshopW1ActionContract? W1Action = null,
    PhotoshopOwnedDocumentCleanupSignature? OwnedDocumentCleanup = null,
    PhotoshopColourSettingsContract? ColourSettings = null);

/// <summary>Supplies the verified Photoshop baseline.</summary>
public interface IPhotoshopBaselineProvider
{
    /// <summary>The accepted baseline, or a structured failure if the signed chain does not vouch for one.</summary>
    OperationResult<PhotoshopBaseline> GetVerifiedBaseline();
}
