using System.IO;

namespace PrintFlow.Infrastructure.Workspace;

/// <summary>Refuses filesystem mutation through a reparse point at any level of a path.</summary>
internal static class ReparsePointGuard
{
    public static void RefuseAncestry(string absolutePath)
    {
        for (string? current = absolutePath; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Retention refuses reparse traversal at '{current}'.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
