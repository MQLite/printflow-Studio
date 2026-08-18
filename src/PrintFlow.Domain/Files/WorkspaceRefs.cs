namespace PrintFlow.Domain.Files;

/// <summary>
/// The logical area of a session workspace a file belongs to (MVP design §10).
/// </summary>
public enum WorkspaceArea
{
    /// <summary>The immutable snapshot of the operator's imported file.</summary>
    Source,

    /// <summary>Temporary copies handed to external applications.</summary>
    Working,

    /// <summary>Approved PNG and TIFF results.</summary>
    Approved,

    /// <summary>Derived results retained for comparison after rejection.</summary>
    Rejected,

    /// <summary>Session-scoped logs.</summary>
    Logs,
}

/// <summary>
/// A reference to a directory inside a session workspace, always relative to the
/// configured workspace root.
/// </summary>
/// <remarks>
/// Relative by construction so the database never stores an absolute machine path and the
/// workspace root can move. Only the workspace module (Epic 11100 Task 11106) resolves
/// these to absolute paths; no other module joins a path (MVP design invariant 12).
/// </remarks>
public readonly record struct WorkspaceDirRef
{
    private WorkspaceDirRef(string relativePath) => RelativePath = relativePath;

    /// <summary>Forward-slash relative path, for example <c>Sessions/S_20260818T101500Z_a1b2c3/Working</c>.</summary>
    public string RelativePath { get; }

    public static WorkspaceDirRef Create(string relativePath)
    {
        string normalised = PathRefValidation.Normalise(relativePath, nameof(relativePath));
        return new WorkspaceDirRef(normalised);
    }

    public override string ToString() => RelativePath ?? string.Empty;
}

/// <summary>
/// A reference to a file inside a session workspace, always relative to the workspace root.
/// </summary>
public readonly record struct WorkspaceFileRef
{
    private WorkspaceFileRef(string relativePath, WorkspaceArea area)
    {
        RelativePath = relativePath;
        Area = area;
    }

    /// <summary>Forward-slash relative path including the file name.</summary>
    public string RelativePath { get; }

    /// <summary>The logical workspace area this file lives in.</summary>
    public WorkspaceArea Area { get; }

    /// <summary>The file name including extension.</summary>
    public string FileName
    {
        get
        {
            string path = RelativePath ?? string.Empty;
            int slash = path.LastIndexOf('/');
            return slash < 0 ? path : path[(slash + 1)..];
        }
    }

    public static WorkspaceFileRef Create(string relativePath, WorkspaceArea area)
    {
        string normalised = PathRefValidation.Normalise(relativePath, nameof(relativePath));
        if (normalised.EndsWith('/'))
        {
            throw new ArgumentException(
                "A file reference must not end with a separator.", nameof(relativePath));
        }

        return new WorkspaceFileRef(normalised, area);
    }

    public override string ToString() => RelativePath ?? string.Empty;
}

/// <summary>
/// Shared validation for workspace references.
/// </summary>
/// <remarks>
/// This is pure string work — it performs no file-system access, which is why it can live
/// in the domain. It is a containment *guard*, not a containment *proof*: the authoritative
/// check resolves the real path and verifies it stays under the session root, and belongs
/// to the workspace module.
/// </remarks>
internal static class PathRefValidation
{
    internal static string Normalise(string? relativePath, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("A workspace reference cannot be empty.", parameterName);
        }

        string normalised = relativePath.Replace('\\', '/').Trim();

        if (normalised.StartsWith('/'))
        {
            throw new ArgumentException(
                "A workspace reference must be relative to the workspace root.", parameterName);
        }

        if (normalised.Length >= 2 && normalised[1] == ':')
        {
            throw new ArgumentException(
                "A workspace reference must not be a rooted path.", parameterName);
        }

        foreach (string segment in normalised.Split('/'))
        {
            if (segment == "..")
            {
                throw new ArgumentException(
                    "A workspace reference must not traverse outside the workspace root.", parameterName);
            }
        }

        return normalised;
    }
}
