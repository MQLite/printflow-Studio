using System.Diagnostics;
using System.IO;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;
using Xunit.Abstractions;

using static PrintFlow.Tests.Fixtures.WorkstationObservation;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// Asks the one question Epic 11600 Part B §10 turns on: can a PrintFlow-owned Photoshop working
/// document actually be closed, through the accepted seam, without answering a prompt?
/// </summary>
/// <remarks>
/// Part A chose Policy A partly on the reasoning that closing a dirty working document would raise
/// a discard prompt, and that answering a prompt PrintFlow did not raise is forbidden. That
/// reasoning was never tested against the real application, and §10 cannot be decided without it:
/// if the close is clean, bounded cleanup is available; if it prompts, Policy A's accumulation is
/// a constraint rather than a preference.
/// <para>
/// <b>Everything here goes through <c>IPhotoshopAutomationFoundation</c>.</b> No keystroke is
/// synthesised by this file. The seam re-proves the document's absolute path immediately before
/// closing, closes the active document only, refuses if a prompt appears, and never terminates
/// Photoshop — so the strongest thing a refusal here can mean is "the document is still open".
/// </para>
/// <para>
/// Opt in with <c>PRINTFLOW_PS_CLEANUP_PROBE=1</c>. Targets are discovered from the workspace by
/// the synthetic prefix and are always managed <c>Working</c> references; an operator document
/// cannot be named by this file, and could not be closed by the seam if it were.
/// </para>
/// </remarks>
public sealed class PhotoshopOwnedDocumentCleanupProbe(ITestOutputHelper output)
{
    private const string EnableVariable = "PRINTFLOW_PS_CLEANUP_PROBE";

    /// <summary>The prefix PrintFlow's own synthetic soak working copies carry.</summary>
    private const string SyntheticPrefix = "PF_11600";

    /// <summary>How many documents to attempt, so a probe cannot become an unbounded loop.</summary>
    private const int MaximumAttempts = 20;

    [Fact]
    public async Task Closing_an_owned_working_document_through_the_accepted_seam()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            // Inert by design; see the class remarks.
            return;
        }

        await using WorkstationAutomationLeaseScope automationLease =
            await WorkstationAutomationLeaseScope.AcquireDefaultAsync();

        PrintFlowConfiguration configuration =
            PrintFlowConfiguration.LoadFromFile(RepositoryFile("appsettings.json"));
        configuration.Adapters.Mode.ShouldBe("Production");

        string workspaceRoot = Path.GetFullPath(configuration.Workspace.Root);
        string manifestPath = Path.Combine(workspaceRoot, configuration.Preset.Path);
        FileWorkspace workspace = new(workspaceRoot);

        IPhotoshopAutomationFoundation foundation = PhotoshopAutomationComposition.CreateFoundation(
            manifestPath,
            Sha256.Parse(configuration.Preset.ExpectedSha256),
            workspace,
            Path.Combine(workspaceRoot, "Evidence"),
            TimeProvider.System);

        output.WriteLine("=== Photoshop before ===");
        int documentsAtStart = DocumentCount(WindowClassCensus.Read("Photoshop"));
        ReportPhotoshop();

        int closed = 0;
        int refused = 0;

        for (int attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            // The active document names itself in the window title. That is used only to *find* a
            // candidate on disk — the ownership decision is made further down by the seam, against
            // the absolute path it reads back from Photoshop itself.
            string? activeFileName = ActiveDocumentFileName();
            if (activeFileName is null)
            {
                output.WriteLine($"attempt {attempt}: the active document is not one of PrintFlow's. Stopping.");
                break;
            }

            // A name can belong to more than one managed Working copy — a retry gives the same
            // file name a second attempt folder. Every one of them is PrintFlow's own, so the
            // ambiguity is about *which* to ask for, not about whether it may be touched: each is
            // offered to the seam in turn, and the seam proves the absolute path either way.
            WorkspaceFileRef[] candidates = ManagedWorkingCopies(workspaceRoot, activeFileName);
            if (candidates.Length == 0)
            {
                output.WriteLine(
                    $"attempt {attempt}: '{activeFileName}' resolves to no managed Working file. " +
                    "Nothing was touched. Stopping.");
                break;
            }

            WorkspaceFileRef target = candidates[0];

            output.WriteLine(string.Empty);
            output.WriteLine($"--- attempt {attempt}: {target.RelativePath} ---");

            OperationResult<PhotoshopOpenedDocument> opened =
                await foundation.OpenManagedWorkingFileAsync(target, CancellationToken.None);
            if (opened.IsFailure)
            {
                output.WriteLine($"  open REFUSED   : {opened.Failure.Code}");
                output.WriteLine($"  detail         : {opened.Failure.TechnicalDetail}");
                refused++;
                break;
            }

            output.WriteLine($"  identity proved: {opened.Value.Identity.ObservedFullPath}");
            output.WriteLine($"  other docs open: {opened.Value.OtherDocumentsMayBeOpen}");

            WindowClassCensus before = WindowClassCensus.Read("Photoshop");

            OperationResult<PhotoshopTarget> result =
                await foundation.CloseExactDocumentAsync(opened.Value, target, CancellationToken.None);

            WindowClassCensus after = WindowClassCensus.Read("Photoshop");

            if (result.IsFailure)
            {
                output.WriteLine($"  close REFUSED  : {result.Failure.Code}");
                output.WriteLine($"  detail         : {result.Failure.TechnicalDetail}");
                foreach (KeyValuePair<string, string> entry in result.Failure.Context)
                {
                    output.WriteLine($"    {entry.Key,-18}: {entry.Value}");
                }

                output.WriteLine($"  documents      : {DocumentCount(before)} -> {DocumentCount(after)}");
                refused++;

                // A refusal is not necessarily "nothing happened": closure is confirmed by the
                // file name the title reports, so two loaded documents sharing a name — which a
                // retry produces — make it unconfirmable even when one of them did close. The
                // census is what says whether anything moved; the seam's answer is what says
                // whether PrintFlow is willing to claim it. Both are reported, and the loop keeps
                // going only while the census is still falling.
                if (DocumentCount(after) >= DocumentCount(before))
                {
                    output.WriteLine("  nothing closed — stopping.");
                    break;
                }

                continue;
            }

            closed++;
            output.WriteLine($"  CLOSED cleanly — no prompt appeared, none was answered");
            output.WriteLine($"  documents      : {DocumentCount(before)} -> {DocumentCount(after)}");
            output.WriteLine($"  title after    : '{result.Value.Window.Title}'");
        }

        output.WriteLine(string.Empty);
        output.WriteLine("=== Photoshop after ===");
        ReportPhotoshop();
        output.WriteLine(string.Empty);
        int documentsAtEnd = DocumentCount(WindowClassCensus.Read("Photoshop"));

        output.WriteLine($"closes confirmed by seam : {closed}");
        output.WriteLine($"refusals                 : {refused}");
        output.WriteLine($"documents by census      : {documentsAtStart} -> {documentsAtEnd} " +
                         $"({documentsAtStart - documentsAtEnd} removed)");
        output.WriteLine(
            documentsAtStart > documentsAtEnd
                ? "A dirty PrintFlow-owned working document CAN be closed through the accepted seam."
                : "No document was closed. Policy A's accumulation is a constraint, not a preference.");
    }

    // -----------------------------------------------------------------------------------
    // Observation
    // -----------------------------------------------------------------------------------

    private void ReportPhotoshop()
    {
        ProcessVitals vitals = ProcessVitals.Read("Photoshop");
        WindowClassCensus windows = WindowClassCensus.Read("Photoshop");
        output.WriteLine($"  {vitals.Describe()}");
        output.WriteLine($"  title     : '{vitals.WindowTitle}'");
        output.WriteLine($"  windows   : {windows.Describe()}");
        output.WriteLine($"  documents : {DocumentCount(windows)}");
    }

    /// <summary>
    /// Photoshop's open-document count, read as the <c>OWL.Document</c> child-window count.
    /// </summary>
    /// <remarks>
    /// Established empirically during the soak rather than assumed: the count moved by exactly one
    /// for every document PrintFlow opened, across nine consecutive observations. It is reported
    /// as the raw class count, not corrected by a baseline, because the baseline is a property of
    /// the running Photoshop rather than of this probe.
    /// </remarks>
    private static int DocumentCount(WindowClassCensus census) =>
        census.ByClass.GetValueOrDefault("OWL.Document");

    /// <summary>
    /// The active document's file name, if the title says it is one of PrintFlow's synthetic
    /// working copies, and <c>null</c> otherwise.
    /// </summary>
    /// <remarks>
    /// A title read, used only to pick a candidate to *ask* about. It decides nothing: the file it
    /// points at is turned into a managed <c>Working</c> reference, and the seam then proves by
    /// absolute path what Photoshop is actually holding before anything is closed. A title that
    /// lied would produce a refusal, not a wrong close.
    /// </remarks>
    private static string? ActiveDocumentFileName()
    {
        foreach (Process process in Process.GetProcessesByName("Photoshop"))
        {
            using (process)
            {
                string title = process.MainWindowTitle;
                int at = title.IndexOf(SyntheticPrefix, StringComparison.Ordinal);
                if (at < 0)
                {
                    return null;
                }

                int end = title.IndexOf(".png", at, StringComparison.OrdinalIgnoreCase);
                return end < 0 ? null : title[at..(end + 4)];
            }
        }

        return null;
    }

    /// <summary>
    /// Every managed <c>Working</c> reference carrying <paramref name="fileName"/>, newest first.
    /// </summary>
    /// <remarks>
    /// Restricted to files that sit under a session's <c>Working</c> directory, so what comes back
    /// is always a reference the adapter's own area check would accept. Nothing outside the
    /// managed workspace can be named by this method, and the seam re-proves the absolute path
    /// before acting on any of them.
    /// </remarks>
    private static WorkspaceFileRef[] ManagedWorkingCopies(string workspaceRoot, string fileName)
    {
        string sessions = Path.Combine(workspaceRoot, "Sessions");
        if (!Directory.Exists(sessions))
        {
            return [];
        }

        return [.. Directory
            .EnumerateFiles(sessions, fileName, SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Working{Path.DirectorySeparatorChar}",
                                         StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Select(path => WorkspaceFileRef.Create(
                Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/'), WorkspaceArea.Working))];
    }

    private static string RepositoryFile(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        current.ShouldNotBeNull("the repository root must be locatable from the test output.");
        return Path.Combine(current.FullName, relativePath);
    }
}
