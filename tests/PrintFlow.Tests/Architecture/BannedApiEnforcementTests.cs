using System.IO;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// Compile/test-time enforcement that <c>Domain</c> and <c>Workflow</c> never acquire direct
/// file-system access (Epic 11100 plan §17.4; task §40).
/// </summary>
/// <remarks>
/// Part 1 deferred <c>BannedApiAnalyzers</c>: adding it would put a <c>PackageReference</c> in
/// <c>PrintFlow.Domain</c>, which the plan also requires to have none, and at the time no
/// <c>System.IO</c> call site existed anywhere in the slice for the analyzer to guard. Now that
/// Task 11106 has introduced real file I/O elsewhere in the solution, the boundary is worth
/// enforcing — but a source-text scan achieves the same guarantee without adding an analyzer
/// package to a project whose entire point is having none. It runs at test time rather than
/// compile time, which is the trade-off recorded for this choice.
///
/// The scan is deliberately narrow: it looks for <c>System.IO</c> usage (the namespace that
/// carries every file/directory API) in source text, excluding generated <c>obj\</c> output.
/// <c>PrintFlow.Infrastructure</c>, <c>PrintFlow.App.Composition</c>, and every test file are
/// the permitted homes for that namespace.
/// </remarks>
public sealed class BannedApiEnforcementTests
{
    [Fact]
    public void Domain_source_contains_no_System_IO_usage()
    {
        AssertNoFileIo("PrintFlow.Domain");
    }

    [Fact]
    public void Workflow_source_contains_no_System_IO_usage()
    {
        AssertNoFileIo("PrintFlow.Workflow");
    }

    /// <summary>
    /// View models drive the UI through <c>ISessionService</c>, never through the file system
    /// (Epic 11100 Part 3C3A §18).
    /// </summary>
    /// <remarks>
    /// Scoped to <c>ViewModels\</c> rather than the whole shell project on purpose: file dialogs
    /// (<c>Navigation\</c>) and the composition root legitimately name paths, and forbidding it
    /// there would be a rule the code could not follow. What must stay true is that the layer
    /// holding the operator's actions never opens, reads, copies or inspects a file — the
    /// closest a view model comes is handing a path a dialog gave it straight to
    /// <c>ImportAsync</c> (plan §17.4).
    /// </remarks>
    [Fact]
    public void Shell_view_models_contain_no_System_IO_usage()
    {
        AssertNoFileIo("PrintFlow.App", subdirectory: "ViewModels");
    }

    private static void AssertNoFileIo(string projectName, string? subdirectory = null)
    {
        string projectDirectory = FindProjectDirectory(projectName);
        if (subdirectory is not null)
        {
            projectDirectory = Path.Combine(projectDirectory, subdirectory);
        }

        List<string> offenders = [];
        foreach (string file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Contains("System.IO", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
                }
            }
        }

        string scope = subdirectory is null ? projectName : $@"{projectName}\{subdirectory}";
        offenders.ShouldBeEmpty(
            $"{scope} must have no System.IO usage — file I/O belongs only in " +
            "PrintFlow.Infrastructure or PrintFlow.App.Composition.");
    }

    /// <summary>
    /// Walks up from the test assembly's output directory to the repository root (the
    /// directory containing <c>PrintFlowStudio.sln</c>), then down into <c>src/&lt;project&gt;</c>.
    /// </summary>
    private static string FindProjectDirectory(string projectName)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        if (current is null)
        {
            throw new InvalidOperationException(
                "Could not locate the repository root (PrintFlowStudio.sln) above " + AppContext.BaseDirectory);
        }

        string projectDirectory = Path.Combine(current.FullName, "src", projectName);
        if (!Directory.Exists(projectDirectory))
        {
            throw new InvalidOperationException($"Expected project directory not found: {projectDirectory}");
        }

        return projectDirectory;
    }
}
