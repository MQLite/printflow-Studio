using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Workspace;

/// <summary>
/// Resolves a relative path against the workspace root and proves it did not escape — the
/// authoritative containment check (Epic 11100 Task 11106b; plan §12.3).
/// </summary>
/// <remarks>
/// <see cref="Domain.Files.WorkspaceRefs"/> validation is a string-level guard against an
/// obviously malformed reference; this is the proof, using the resolved full path, that the
/// result genuinely stays under the configured root and outside the two protected evidence
/// directories.
/// </remarks>
internal static class PathGuard
{
    private const int MaxPathLength = 240;

    public static OperationResult<string> ResolveWithinRoot(string rootAbsolute, string relativePath)
    {
        string rootFull = System.IO.Path.GetFullPath(rootAbsolute);
        string candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(rootFull, relativePath));

        string rootWithSeparator = rootFull.EndsWith(System.IO.Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + System.IO.Path.DirectorySeparatorChar;

        bool isRootItself = string.Equals(candidate, rootFull, StringComparison.OrdinalIgnoreCase);
        bool isUnderRoot = candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);

        if (!isRootItself && !isUnderRoot)
        {
            return OperationResult.Fail<string>(
                Domain.Results.FailureCode.WorkspaceError,
                $"Path '{relativePath}' resolves outside the workspace root.");
        }

        if (candidate.Length >= MaxPathLength)
        {
            return OperationResult.Fail<string>(
                Domain.Results.FailureCode.WorkspaceError,
                $"Resolved path exceeds the {MaxPathLength}-character guard: '{candidate}'.");
        }

        if (IsUnderProtectedArea(candidate, rootFull, "Baseline") ||
            IsUnderProtectedArea(candidate, rootFull, "TestData"))
        {
            return OperationResult.Fail<string>(
                Domain.Results.FailureCode.WorkspaceError,
                $"Path '{relativePath}' targets a protected evidence area (Baseline/TestData) and is refused.");
        }

        return OperationResult.Ok(candidate);
    }

    private static bool IsUnderProtectedArea(string candidate, string rootFull, string protectedName)
    {
        string protectedFull = System.IO.Path.Combine(rootFull, protectedName);
        string protectedWithSeparator = protectedFull + System.IO.Path.DirectorySeparatorChar;

        return string.Equals(candidate, protectedFull, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(protectedWithSeparator, StringComparison.OrdinalIgnoreCase);
    }
}
