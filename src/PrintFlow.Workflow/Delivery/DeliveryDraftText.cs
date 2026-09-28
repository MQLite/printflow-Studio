namespace PrintFlow.Workflow.Delivery;

public enum DraftNameProblem { None, Invalid, WrongType }

/// <summary>
/// Pure text rules for the displayed save draft (SCRUM-11145). Nothing here touches the file
/// system or grants anything; every delivery revalidates the name and folder under its guards.
/// </summary>
public static class DeliveryDraftText
{
    private static readonly char[] Forbidden = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>
    /// The complete effective name shown before confirmation. A missing extension is added
    /// visibly; a wrong one is refused, never converted; nothing is silently sanitized.
    /// </summary>
    public static (string? Effective, DraftNameProblem Problem, bool ExtensionAdded) EffectiveFileName(
        string? text, ArtifactKind kind)
    {
        if (string.IsNullOrWhiteSpace(text) || text is "." or ".." || text.EndsWith(' ') || text.EndsWith('.') ||
            text.IndexOfAny(Forbidden) >= 0 || text.Any(char.IsControl))
            return (null, DraftNameProblem.Invalid, false);
        string extension = Path.GetExtension(text);
        if (extension.Length == 0)
            return (text + (kind == ArtifactKind.ApprovedAssetPng ? ".png" : ".tif"), DraftNameProblem.None, true);
        bool matches = kind == ArtifactKind.ApprovedAssetPng
            ? extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            : extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
        return matches ? (text, DraftNameProblem.None, false) : (null, DraftNameProblem.WrongType, false);
    }

    public static string FileNameOf(string path) => Path.GetFileName(path);

    public static string FolderOf(string path) => Path.GetDirectoryName(path) ?? string.Empty;

    public static string Combine(string folder, string fileName) => Path.Combine(folder, fileName);

    /// <summary>The staged or final copy did not hash to the approved bytes (a delivery fact, not workstation readiness).</summary>
    public static bool IsCopyMismatch(DeliveryCode code) => code == DeliveryCode.VerificationFailed;

    public static bool SamePath(string? left, string? right) =>
        left is not null && right is not null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
