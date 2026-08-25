using System.IO;
using System.Text.RegularExpressions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// The shell stays a reader of the reviewed-content authority and never a second author of it
/// (Epic 11300 Part C2B2 §17, §28).
/// </summary>
/// <remarks>
/// C2B1 put one predicate behind every staleness question:
/// <see cref="BackgroundRemovalAuthority.Authorises"/>, reached through
/// <c>WorkflowSnapshot.UsableBackgroundRemovalAuthority</c> and reported by the read model. The
/// specific way this slice could go wrong is a screen deciding for itself whether an authority
/// still applies — comparing Revisions, comparing hashes, or hashing a file to find out. Two
/// answers to "is this still valid" is one answer too many, and the one that decides whether a
/// control is offered would be the wrong one to be wrong.
/// <para>
/// Source text rather than reflection, because the failure this catches is a line someone adds
/// while wiring something up, not a type that reaches a signature.
/// </para>
/// </remarks>
public sealed class BackgroundRemovalAuthorityBoundaryTests
{
    /// <summary>
    /// The shell computes no hash (§17, §20, §28).
    /// </summary>
    /// <remarks>
    /// Every hash the screen sends came out of the read model. A shell that could produce one
    /// could "helpfully" re-hash a file that had changed and authorise whatever was there now,
    /// which is precisely the refusal §20 asks for turned into a silent success.
    /// </remarks>
    [Theory]
    [InlineData("SHA256")]
    [InlineData("SHA1")]
    [InlineData("MD5")]
    [InlineData("HashAlgorithm")]
    [InlineData("IncrementalHash")]
    [InlineData("ComputeHash")]
    [InlineData("System.Security.Cryptography")]
    public void The_shell_names_no_hashing_api(string banned)
    {
        Offenders("PrintFlow.App", subdirectory: null, banned).ShouldBeEmpty(
            "hashes reach the screen through the read model; the shell must not be able to make one.");
    }

    /// <summary>
    /// No view model re-derives whether an authority still covers the content on screen
    /// (§2, §6, §28).
    /// </summary>
    /// <remarks>
    /// The members named here are the raw materials of a second staleness rule. The read model
    /// already reports only the <i>usable</i> authority, so a screen has nothing left to decide
    /// — and a comparison written here would be a rule that could drift from the engine's.
    /// <para>
    /// <c>BackgroundRemovalDecisionRevisionId</c> is deliberately not banned: the screen reads it
    /// to <i>name</i> the covered Revision in the status line, which is display, not a decision.
    /// What must not appear is a comparison, and the members below are the only ones that could
    /// form one.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Authorises")]
    [InlineData("UsableBackgroundRemovalAuthority")]
    [InlineData("ReviewedRevisionId")]
    [InlineData("ReviewedSha256")]
    public void No_view_model_restates_the_stale_authority_rule(string banned)
    {
        Offenders("PrintFlow.App", "ViewModels", banned).ShouldBeEmpty(
            "staleness is decided once, in the workflow layer; the shell reads the answer.");
    }

    /// <summary>
    /// The views state no authority condition of their own (§7, §9, §28).
    /// </summary>
    /// <remarks>
    /// XAML has no place to put a comparison, but it has plenty of places to put a condition —
    /// a trigger on a decision value, a converter comparing two bound Revisions. Every panel in
    /// this slice binds to a single boolean the workflow layer already answered, so none of the
    /// names below should appear in a view at all.
    /// </remarks>
    [Theory]
    [InlineData("Authorises")]
    [InlineData("ReviewedRevisionId")]
    [InlineData("ReviewedSha256")]
    [InlineData("UseAutomaticSelectionForReviewedContent")]
    public void No_view_states_an_authority_condition(string banned)
    {
        List<string> offenders = [];

        foreach ((string file, string[] lines) in SourceOf("PrintFlow.App", "Views", "*.xaml"))
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (Contains(lines[i], banned))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        offenders.ShouldBeEmpty("eligibility and staleness are not conditions a view may hold.");
    }

    /// <summary>
    /// The read model reports the attempt's authority and the session's separately (§15, §16).
    /// </summary>
    /// <remarks>
    /// A structural check on the shape §15 depends on: if the review audit and the pending
    /// decision were one field, "what produced this result" and "what may run next" would be the
    /// same value by construction, and no test could tell them apart. Four members, two
    /// questions, and the compiler keeps them separate.
    /// </remarks>
    [Fact]
    public void The_read_model_keeps_the_pending_and_the_producing_authority_apart()
    {
        Type view = typeof(SessionView);

        view.GetProperty(nameof(SessionView.BackgroundRemovalDecision)).ShouldNotBeNull();
        view.GetProperty(nameof(SessionView.BackgroundRemovalDecisionRevisionId)).ShouldNotBeNull();
        view.GetProperty(nameof(SessionView.BackgroundRemovalAttemptDecision)).ShouldNotBeNull();
        view.GetProperty(nameof(SessionView.BackgroundRemovalAttemptReviewedRevisionId)).ShouldNotBeNull();

        // And nothing here is an entity: the read model exposes an enum and an id, not a row.
        view.GetProperty(nameof(SessionView.BackgroundRemovalAttemptDecision))!
            .PropertyType.ShouldBe(typeof(BackgroundRemovalDecision));
    }

    private static IReadOnlyList<string> Offenders(string project, string? subdirectory, string banned)
    {
        List<string> offenders = [];

        foreach ((string file, string[] lines) in SourceOf(project, subdirectory, "*.cs"))
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (Contains(lines[i], banned))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        return offenders;
    }

    /// <summary>
    /// Whole-identifier matching, and never inside a comment.
    /// </summary>
    /// <remarks>
    /// The comments in this slice discuss these very names — explaining why the screen does not
    /// compare Revisions requires saying "ReviewedRevisionId" — and a scan that failed on the
    /// explanation would push the reasoning out of the code, which is the opposite of what these
    /// tests are for. What is banned is the identifier in executable text.
    /// </remarks>
    private static bool Contains(string line, string identifier)
    {
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
            trimmed.StartsWith("///", StringComparison.Ordinal) ||
            trimmed.StartsWith("*", StringComparison.Ordinal) ||
            trimmed.StartsWith("<!--", StringComparison.Ordinal))
        {
            return false;
        }

        return Regex.IsMatch(
            line, $@"\b{Regex.Escape(identifier)}\b", RegexOptions.None, TimeSpan.FromSeconds(5));
    }

    private static IEnumerable<(string File, string[] Lines)> SourceOf(
        string project, string? subdirectory, string pattern)
    {
        string directory = FindProjectDirectory(project);
        if (subdirectory is not null)
        {
            directory = Path.Combine(directory, subdirectory);
        }

        foreach (string file in Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (file, File.ReadAllLines(file));
        }
    }

    private static string FindProjectDirectory(string projectName)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        return current is null
            ? throw new InvalidOperationException(
                "Could not locate the repository root (PrintFlowStudio.sln) above " + AppContext.BaseDirectory)
            : Path.Combine(current.FullName, "src", projectName);
    }
}
