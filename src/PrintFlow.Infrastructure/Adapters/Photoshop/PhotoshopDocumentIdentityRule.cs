using System.IO;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>
/// Decides whether the document Photoshop is holding is the exact managed file PrintFlow asked
/// for (Epic 11400 Part A §11, §12).
/// </summary>
/// <remarks>
/// Two independent readings have to agree, and the reason is the ambiguity the workstation
/// actually has. Photoshop's window title carries the document's basename and nothing else, so
/// on the title alone <c>…\Working\A.png</c> and <c>…\Downloads\A.png</c> are the same document.
/// The Save As surface carries the basename <i>and</i> the folder, and live discovery confirmed
/// the folder follows the active document across directories rather than remembering the
/// last-used one — so combining the two yields an absolute path, and identity is decided on
/// that.
///
/// Every comparison here is exact. There is no prefix rule, no substring rule, no "contains the
/// expected name" rule and no fallback that accepts a basename when the folder could not be
/// read: a folder PrintFlow failed to read is a refusal, because the alternative is to downgrade
/// the claim silently at exactly the moment the evidence got weaker (§12).
///
/// Case is compared insensitively because Windows filenames are, and comparing them ordinally
/// would refuse a document that genuinely is the expected file.
/// </remarks>
public static class PhotoshopDocumentIdentityRule
{
    /// <summary>
    /// Returns the document basename a Photoshop window title names, or <c>null</c> when the
    /// title names no document.
    /// </summary>
    /// <remarks>
    /// A loaded document produces <c>NAME.png @ 100% (图层 1, RGB/8)</c>; no document produces
    /// the bare application title. Splitting on the first occurrence of the signed separator is
    /// what distinguishes them, and taking the <i>first</i> occurrence matters: a filename could
    /// itself contain the separator, and the leading segment is the part Photoshop wrote the
    /// name into.
    /// </remarks>
    public static string? DocumentNameInTitle(
        PhotoshopDocumentIdentitySignature signature, string? windowTitle)
    {
        ArgumentNullException.ThrowIfNull(signature);

        if (string.IsNullOrEmpty(windowTitle) || signature.TitleSeparator.Length == 0)
        {
            return null;
        }

        int separator = windowTitle.IndexOf(signature.TitleSeparator, StringComparison.Ordinal);
        if (separator <= 0)
        {
            return null;
        }

        string candidate = windowTitle[..separator];
        return candidate.Length == 0 ? null : candidate;
    }

    /// <summary>Whether the window title names exactly <paramref name="expectedFileName"/>.</summary>
    /// <remarks>
    /// A necessary condition, never a sufficient one. It is what lets a wrong or stale document
    /// be refused cheaply, before the identity probe raises any surface at all — but on its own
    /// it proves only that <i>a</i> file of that name is loaded.
    /// </remarks>
    public static bool TitleNamesExpectedDocument(
        PhotoshopDocumentIdentitySignature signature, string? windowTitle, string expectedFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFileName);

        string? observed = DocumentNameInTitle(signature, windowTitle);
        return observed is not null &&
               string.Equals(observed, expectedFileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Strips the signed prefix from the address bar's text and returns the folder path.
    /// </summary>
    /// <remarks>
    /// An exact prefix match, and a failure when it does not match. Searching the text for
    /// something that looks like a path would keep working when the surface changed shape, which
    /// is the opposite of what is wanted: the whole value of this reading is that it stops being
    /// trusted the moment it stops being the thing that was signed.
    /// </remarks>
    public static OperationResult<string> FolderFromAddressText(
        PhotoshopDocumentIdentitySignature signature, string? addressText)
    {
        ArgumentNullException.ThrowIfNull(signature);

        if (string.IsNullOrWhiteSpace(addressText))
        {
            return OperationResult.Fail<string>(
                FailureCode.PhotoshopDocumentIdentityUnconfirmed,
                "The Photoshop identity surface reported no address text, so the folder holding the " +
                "loaded document could not be established. Identity is refused rather than reduced " +
                "to the file name alone.");
        }

        if (!addressText.StartsWith(signature.AddressTextPrefix, StringComparison.Ordinal))
        {
            return OperationResult.Fail<string>(OperationFailure.Create(
                FailureCode.PhotoshopDocumentIdentityUnconfirmed,
                $"The Photoshop identity surface's address text does not begin with the signed prefix " +
                $"'{signature.AddressTextPrefix}', so PrintFlow cannot say which folder it describes.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["expectedPrefix"] = signature.AddressTextPrefix,
                }));
        }

        string folder = addressText[signature.AddressTextPrefix.Length..].Trim();
        return folder.Length == 0
            ? OperationResult.Fail<string>(
                FailureCode.PhotoshopDocumentIdentityUnconfirmed,
                "The Photoshop identity surface's address text carried the signed prefix and nothing " +
                "after it, so no folder could be read.")
            : OperationResult.Ok(folder);
    }

    /// <summary>
    /// Combines the two readings into the absolute path of the loaded document.
    /// </summary>
    public static OperationResult<string> ResolveObservedPath(string folder, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return OperationResult.Fail<string>(
                FailureCode.PhotoshopDocumentIdentityUnconfirmed,
                "The Photoshop identity surface reported no document file name.");
        }

        // A reported "file name" that is really a path would mean the surface is not the one
        // that was signed, and combining it would produce a plausible-looking absolute path out
        // of something PrintFlow does not understand.
        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            return OperationResult.Fail<string>(OperationFailure.Create(
                FailureCode.PhotoshopDocumentIdentityUnconfirmed,
                $"The Photoshop identity surface reported '{fileName}', which is not a bare file name. " +
                "PrintFlow will not construct a document path from it.",
                isRetryable: false));
        }

        try
        {
            return OperationResult.Ok(Path.GetFullPath(Path.Combine(folder, fileName)));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return OperationResult.Fail<string>(
                FailureCode.PhotoshopDocumentIdentityUnconfirmed,
                $"The folder and file name Photoshop reported do not combine into a usable path: {ex.Message}");
        }
    }

    /// <summary>
    /// Whether the observed absolute path is exactly the expected managed file.
    /// </summary>
    /// <remarks>
    /// Both sides are normalised through <see cref="Path.GetFullPath(string)"/> first, so a
    /// trailing separator or a <c>.\</c> segment on either side does not become a spurious
    /// refusal. Nothing else is normalised away — in particular, no attempt is made to resolve
    /// links or 8.3 names, because two paths PrintFlow cannot show to be the same file are two
    /// paths it must refuse.
    /// </remarks>
    public static bool MatchesExpectedDocument(string expectedAbsolutePath, string? observedAbsolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAbsolutePath);

        if (string.IsNullOrWhiteSpace(observedAbsolutePath))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(expectedAbsolutePath),
                Path.GetFullPath(observedAbsolutePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }
    }
}
