using System.IO;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// A throwaway workspace root under the OS temp directory, laid out with the protected
/// <c>Baseline\</c>/<c>TestData\</c> areas so containment tests have somewhere real to target.
/// </summary>
/// <remarks>
/// Never touches <c>D:\PrintFlowStudio</c>. Deleted on dispose; a best-effort cleanup swallows
/// failures so a locked file from a slow antivirus scan cannot fail an unrelated test.
/// </remarks>
internal sealed class TempWorkspace : IDisposable
{
    public string Root { get; }

    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "PrintFlowTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Path.Combine(Root, "Baseline"));
        Directory.CreateDirectory(Path.Combine(Root, "TestData"));
    }

    public string CreateSourceFile(string fileName, byte[] content)
    {
        string path = Path.Combine(Root, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                // Clear any read-only attributes InputSnapshot handling may have set, or the
                // recursive delete below throws.
                foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; a locked file must not fail an unrelated test.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
