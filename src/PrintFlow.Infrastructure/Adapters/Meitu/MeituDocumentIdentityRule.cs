using System.IO;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

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
