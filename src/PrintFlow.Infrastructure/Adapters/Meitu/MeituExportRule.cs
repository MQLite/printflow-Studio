using System.IO;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// The controlled destination an export must land on, taken apart into the pieces the Save
/// surface and the destination dialog each need (Epic 11300 Part B2B §4, §9, §10, §11).
/// </summary>
/// <param name="AbsolutePath">The full path written into the destination dialog.</param>
/// <param name="Directory">The controlled attempt directory it lives in.</param>
/// <param name="BaseName">The file name without its extension — what the Save surface's field takes.</param>
/// <param name="Extension">The extension, without the dot, matched against the signed format.</param>
/// <remarks>
/// Split here rather than at the point of use because the two controls want different halves of
/// the same fact, and deriving one from the other twice is how they drift apart. Meitu's
/// file-name field carries the stem only; the extension comes from the format selector, and the
/// destination dialog wants the whole path.
/// </remarks>
public sealed record MeituExportDestination(
    string AbsolutePath,
    string Directory,
    string BaseName,
    string Extension);

/// <summary>
/// The pure parts of the export route: what a controlled destination has to look like, and how
/// Meitu's post-save surface is recognised (Epic 11300 Part B2B §4, §11, §24, §33).
/// </summary>
/// <remarks>
/// Static and side-effect-free for the same reason as every other rule in this adapter — the
/// interesting behaviour is a set of refusals, and a refusal is only worth trusting when a test
/// can construct the situation that must produce it without a desktop, a window or a file.
///
/// The one thing this class deliberately cannot do is touch the file system. Whether the target
/// already exists is a question about the world at a moment in time and belongs with the code
/// that can act on the answer; whether the path is one PrintFlow is willing to name at all is a
/// property of the string, and belongs here.
/// </remarks>
public static class MeituExportRule
{
    /// <summary>
    /// Takes a controlled destination apart, refusing every shape the export must not accept.
    /// </summary>
    /// <param name="absolutePath">The path the adapter resolved from the attempt's managed reference.</param>
    /// <param name="requiredFormatValue">The exact format value the signed evidence records.</param>
    /// <remarks>
    /// The extension check is not belt-and-braces over the format selector — it is the other
    /// half of the same guarantee. The selector decides what Meitu encodes; the extension
    /// decides what the file is called. A destination whose extension disagreed with the signed
    /// format would produce a file named <c>.png</c> containing something else, which every
    /// downstream check that trusts the name would then get wrong.
    /// </remarks>
    public static OperationResult<MeituExportDestination> ResolveDestination(
        string absolutePath, string requiredFormatValue)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return OperationResult.Fail<MeituExportDestination>(
                FailureCode.PreconditionNotMet,
                "No export destination was supplied. Nothing was invoked.");
        }

        if (string.IsNullOrWhiteSpace(requiredFormatValue))
        {
            return OperationResult.Fail<MeituExportDestination>(
                FailureCode.EnvironmentNotVerified,
                "The signed export evidence records no required format value, so PrintFlow has no way to " +
                "confirm the format positively and will not export. Nothing was invoked.");
        }

        if (!Path.IsPathFullyQualified(absolutePath))
        {
            return OperationResult.Fail<MeituExportDestination>(
                FailureCode.PreconditionNotMet,
                $"'{absolutePath}' is not a fully qualified path. A destination that depends on a current " +
                "directory is a destination PrintFlow does not control. Nothing was invoked.");
        }

        string? directory = Path.GetDirectoryName(absolutePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return OperationResult.Fail<MeituExportDestination>(
                FailureCode.PreconditionNotMet,
                $"'{absolutePath}' names no directory to export into. Nothing was invoked.");
        }

        string fileName = Path.GetFileName(absolutePath);
        string baseName = Path.GetFileNameWithoutExtension(absolutePath);
        string extension = Path.GetExtension(absolutePath).TrimStart('.');

        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(baseName))
        {
            return OperationResult.Fail<MeituExportDestination>(
                FailureCode.PreconditionNotMet,
                $"'{absolutePath}' has no file name to give Meitu. Nothing was invoked.");
        }

        if (!string.Equals(extension, requiredFormatValue, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail<MeituExportDestination>(
                FailureCode.PreconditionNotMet,
                $"The export destination '{fileName}' has extension '{extension}', but the signed export " +
                $"evidence requires the format '{requiredFormatValue}'. PrintFlow will not write a file " +
                "whose name disagrees with the format it asked Meitu for. Nothing was invoked.");
        }

        return OperationResult.Ok(new MeituExportDestination(
            absolutePath, directory, baseName, extension));
    }

    /// <summary>
    /// Whether an observation shows Meitu's post-save confirmation surface.
    /// </summary>
    /// <remarks>
    /// Positive markers only, and it is worth saying why here as well as in the evidence: the
    /// Save surface and the result surface share a window class <i>and</i> a window title on
    /// this build, so "an owned surface of the right class is up" identifies neither of them.
    /// What separates them is what they contain.
    /// </remarks>
    public static bool ShowsResultSurface(
        MeituExportResultSignature signature, IReadOnlyList<string> visibleNames)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(visibleNames);

        if (signature.RequiredMarkers.IsDefaultOrEmpty ||
            signature.MinimumRequiredMarkers <= 0 ||
            signature.MinimumRequiredMarkers > signature.RequiredMarkers.Length)
        {
            // Evidence that names no marker describes no screen. Matching everything here would
            // let PrintFlow dismiss any owned Meitu surface it happened to find.
            return false;
        }

        int seen = 0;
        foreach (string marker in signature.RequiredMarkers)
        {
            foreach (string name in visibleNames)
            {
                if (name.Contains(marker, StringComparison.Ordinal))
                {
                    seen++;
                    break;
                }
            }
        }

        return seen >= signature.MinimumRequiredMarkers;
    }
}
