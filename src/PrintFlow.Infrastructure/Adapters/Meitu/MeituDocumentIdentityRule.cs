using System.IO;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>The signed document surface from which Save may be used as an identity probe.</summary>
public enum MeituDocumentSurfacePhase
{
    Unknown,
    LoadedEditor,
    EnhancementResult,
    BackgroundRemovalResult,
    AmbiguousResult,
}

/// <summary>
/// Compares Meitu's signed Save-default identity with the Working-copy filename PrintFlow chose.
/// </summary>
/// <remarks>
/// Meitu 7.8.7.5 exposes a loaded source named <c>A.png</c> as the exact Save basename
/// <c>A_副本</c>. This rule models that transformation explicitly. It never accepts a prefix,
/// substring, recent-file entry, or merely the presence of a loaded document.
/// </remarks>
public static class MeituDocumentIdentityRule
{
    /// <summary>
    /// Classifies a settled document surface on which the signed Save control may be invoked only
    /// to read and cancel its default-name identity.
    /// </summary>
    /// <remarks>
    /// Enhancement and cutout completion screens are legitimate document surfaces even when the
    /// ordinary editor toolbar markers are hidden. Operation-specific completion markers are
    /// therefore alternatives to, not substitutes for, the common exact-title, enabled-window
    /// and no-owned-dialog guards. If both result signatures match, the screen is ambiguous and
    /// remains ineligible for input.
    /// </remarks>
    public static MeituDocumentSurfacePhase ClassifyIdentityProbeSurface(
        MeituBaseline baseline,
        MeituObservation observation)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(observation);

        if (baseline.DocumentIdentity is not { } identity ||
            !string.Equals(observation.WindowTitle, identity.Editor.WindowTitle, StringComparison.Ordinal) ||
            !observation.OwnedDialogTitles.IsDefaultOrEmpty ||
            !observation.MainWindowEnabled)
        {
            return MeituDocumentSurfacePhase.Unknown;
        }

        bool enhancementResult = baseline.Enhancement is { } enhancement &&
            MeituEnhancementRule.Classify(enhancement, observation) == MeituEnhancementPhase.Complete;
        bool backgroundRemovalResult = baseline.BackgroundRemoval is { } backgroundRemoval &&
            MeituBackgroundRemovalRule.Classify(backgroundRemoval, observation) ==
                MeituBackgroundRemovalPhase.Complete;

        if (enhancementResult && backgroundRemovalResult)
        {
            return MeituDocumentSurfacePhase.AmbiguousResult;
        }

        if (enhancementResult)
        {
            return MeituDocumentSurfacePhase.EnhancementResult;
        }

        if (backgroundRemovalResult)
        {
            return MeituDocumentSurfacePhase.BackgroundRemovalResult;
        }

        return MatchesLoadedEditor(identity, observation)
            ? MeituDocumentSurfacePhase.LoadedEditor
            : MeituDocumentSurfacePhase.Unknown;
    }

    /// <summary>Whether positive signed markers identify the loaded editor before Save.</summary>
    public static bool MatchesLoadedEditor(
        MeituDocumentIdentitySignature signature, MeituObservation observation)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(observation);

        MeituEditorSignature editor = signature.Editor;
        if (!string.Equals(observation.WindowTitle, editor.WindowTitle, StringComparison.Ordinal) ||
            editor.RequiredMarkers.IsDefaultOrEmpty || editor.MinimumRequiredMarkers <= 0 ||
            !observation.OwnedDialogTitles.IsDefaultOrEmpty || !observation.MainWindowEnabled)
        {
            return false;
        }

        int seen = editor.RequiredMarkers.Count(marker =>
            observation.VisibleTexts.Any(text => text.Contains(marker, StringComparison.Ordinal)));
        return seen >= editor.MinimumRequiredMarkers;
    }

    /// <summary>Returns the exact Save basename expected for a Working-copy filename.</summary>
    public static OperationResult<string> ExpectedSaveBaseName(
        MeituDocumentIdentitySignature signature, string expectedWorkingCopyFileName)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedWorkingCopyFileName);

        string fileName = Path.GetFileName(expectedWorkingCopyFileName);
        string baseName = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);

        if (!string.Equals(fileName, expectedWorkingCopyFileName, StringComparison.Ordinal) ||
            baseName.Length == 0 || extension.Length == 0)
        {
            return OperationResult.Fail<string>(
                FailureCode.MeituUnknownState,
                $"'{expectedWorkingCopyFileName}' is not a filename with an extension, so no exact Meitu " +
                "Save-default identity can be derived from it.");
        }

        return OperationResult.Ok(baseName + signature.OutputBaseNameSuffix);
    }

    /// <summary>
    /// Whether the observed value exactly identifies the expected Working copy under Windows
    /// case-insensitive filename semantics.
    /// </summary>
    public static bool MatchesExpectedWorkingCopy(
        MeituDocumentIdentitySignature signature,
        string expectedWorkingCopyFileName,
        string? observedDocumentIdentity)
    {
        if (string.IsNullOrEmpty(observedDocumentIdentity))
        {
            return false;
        }

        OperationResult<string> expected = ExpectedSaveBaseName(signature, expectedWorkingCopyFileName);
        return expected.IsSuccess && string.Equals(
            observedDocumentIdentity, expected.Value, StringComparison.OrdinalIgnoreCase);
    }
}
