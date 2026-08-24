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
/// <param name="EditorWithWorkingCopy">
/// The signature that proves the editor is showing a named document, or <c>null</c>. Null
/// leaves <see cref="MeituStartingState.KnownEditorWithExpectedWorkingCopy"/> unreachable.
/// </param>
/// <param name="EditorEmpty">
/// The signature of the editor with no document loaded, or <c>null</c>. Null leaves
/// <see cref="MeituStartingState.KnownEditorEmpty"/> unreachable — which is the Part A
/// position, kept until positive markers for that screen are actually observed (§10, §15).
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
    MeituEditorSignature? EditorWithWorkingCopy = null,
    MeituEditorSignature? EditorEmpty = null);

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
