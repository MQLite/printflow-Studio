using System.Globalization;
using System.IO;
using System.Text;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using FileWorkspace = PrintFlow.Infrastructure.Workspace.FileWorkspace;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// The controlled workstation smoke (Epic 11300 Part A §27, §28).
/// </summary>
/// <remarks>
/// Opt-in and inert by default. An ordinary <c>dotnet test</c> run — on this workstation or any
/// other — does nothing here, because a test suite that could launch Meitu and drive its UI as a
/// side effect of a routine build would be its own hazard.
///
/// Two switches, in increasing order of consequence:
/// <list type="bullet">
///   <item><c>PRINTFLOW_MEITU_SMOKE=1</c> runs the read-only half: verify the signed baseline,
///         identify the accepted process and window, and classify the state. This sends nothing
///         and changes nothing.</item>
///   <item><c>PRINTFLOW_MEITU_SMOKE_OPEN=1</c> additionally allows the open step, which does
///         produce input — and only ever through the guarded driver, only after the state has
///         been recognised as safe, and only with a synthetic file the smoke created itself.</item>
/// </list>
///
/// The working copy lives under the OS temp directory (§18). Neither <c>D:\PrintFlowStudio</c>
/// nor any of Documents, Desktop or Downloads is read, written, or navigated to.
/// </remarks>
public sealed class MeituWorkstationSmoke
{
    private const string EnableVariable = "PRINTFLOW_MEITU_SMOKE";
    private const string EnableOpenVariable = "PRINTFLOW_MEITU_SMOKE_OPEN";

    [Fact]
    public async Task Locate_identify_and_classify_the_workstation_Meitu()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            // Inert by design; see the class remarks.
            return;
        }

        StringBuilder transcript = new();
        void Log(string line)
        {
            transcript.AppendLine(line);
            Console.WriteLine(line);
        }

        string root = Path.Combine(Path.GetTempPath(), "PrintFlowMeituSmoke", Guid.NewGuid().ToString("N"));
        string evidenceDirectory = Path.Combine(root, "Evidence");

        try
        {
            Log($"# Meitu workstation smoke — {DateTimeOffset.Now:O}");
            Log($"controlled workspace : {root}");

            PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(RepositoryFile("appsettings.json"));
            string manifest = Path.Combine(configuration.Workspace.Root, configuration.Preset.Path);
            Sha256 expected = Sha256.Parse(configuration.Preset.ExpectedSha256);
            Log($"preset manifest      : {manifest}");

            // The baseline first, on its own, so a failure here is reported as "the signed chain
            // could not be verified" rather than as "Meitu could not be found".
            PresetMeituBaselineProvider baselines = new(manifest, expected);
            OperationResult<MeituBaseline> baseline = baselines.GetVerifiedBaseline();
            if (baseline.IsFailure)
            {
                Log($"RESULT               : baseline REFUSED — {baseline.Failure.Code}");
                Log($"detail               : {baseline.Failure.TechnicalDetail}");
                return;
            }

            Log($"accepted executable  : {baseline.Value.ExecutablePath}");
            Log($"accepted version     : {baseline.Value.AcceptedVersion} ({baseline.Value.UiLanguage})");
            Log($"accepted titles      : {string.Join(", ", baseline.Value.AcceptedWindowTitles)}");
            Log($"signed markers ({baseline.Value.WelcomeMarkers.Length,2}) : {string.Join(", ", baseline.Value.WelcomeMarkers)}");

            (string workingCopyPath, WorkspaceFileRef workingCopy, IWorkspace workspace) =
                PrepareSyntheticWorkingCopy(root);
            Log($"synthetic working copy: {workingCopyPath}");

            IMeituAutomationFoundation foundation = MeituAutomationComposition.CreateFoundation(
                manifest, expected, workspace, evidenceDirectory, TimeProvider.System);

            Log(string.Empty);
            Log("## Phase 1 — identify and classify (read-only, no input)");

            OperationResult<MeituReadiness> ready = await foundation.EnsureReadyAsync(CancellationToken.None);
            if (ready.IsFailure)
            {
                Log($"RESULT               : STOPPED — {ready.Failure.Code}");
                Log($"detail               : {ready.Failure.TechnicalDetail}");
                foreach (KeyValuePair<string, string> entry in ready.Failure.Context)
                {
                    Log($"  {entry.Key,-20}: {entry.Value}");
                }

                Log(string.Empty);
                Log("Nothing was clicked, typed, dismissed or closed. Meitu was left exactly as found.");
                return;
            }

            Log($"process id           : {ready.Value.Target.Process.ProcessId}");
            Log($"process image        : {ready.Value.Target.Process.ExecutablePath}");
            Log($"window handle        : {ready.Value.Target.Window.Handle}");
            Log($"window title / class : '{ready.Value.Target.Window.Title}' / {ready.Value.Target.Window.ClassName}");
            Log($"window bounds        : {ready.Value.Target.Window.Bounds}");
            Log($"state                : {ready.Value.State.State}");
            Log($"matched markers      : {string.Join(", ", ready.Value.State.MatchedMarkers)}");
            Log($"launched by PrintFlow: {ready.Value.WasLaunched}");

            if (Environment.GetEnvironmentVariable(EnableOpenVariable) != "1")
            {
                Log(string.Empty);
                Log($"## Phase 2 — skipped ({EnableOpenVariable} is not set). No input was produced.");
                return;
            }

            Log(string.Empty);
            Log("## Phase 2 — open the synthetic working copy (guarded input)");

            OperationResult<MeituOpenedWorkingCopy> opened =
                await foundation.OpenWorkingCopyAsync(workingCopy, CancellationToken.None);

            if (opened.IsFailure)
            {
                Log($"RESULT               : OPEN FAILED — {opened.Failure.Code}");
                Log($"detail               : {opened.Failure.TechnicalDetail}");
                foreach (KeyValuePair<string, string> entry in opened.Failure.Context)
                {
                    Log($"  {entry.Key,-20}: {entry.Value}");
                }

                DescribeOpenSurface(ready.Value, baseline.Value, Log);
                return;
            }

            Log($"state                : {opened.Value.State.State}");
            Log($"window title         : '{opened.Value.Target.Window.Title}'");
            Log(string.Empty);
            Log("STOP. No Enhancement, no Background Removal, no export, no Revision (§27).");
        }
        finally
        {
            WriteTranscript(transcript.ToString());
            CleanUp(root, evidenceDirectory);
        }
    }

    /// <summary>
    /// Records, read-only, what the open path actually had to work with when it failed.
    /// </summary>
    /// <remarks>
    /// Diagnostic, not product behaviour. When the guarded open path stops, the useful questions
    /// are which windows Meitu had open and whether its start-page entry exposes any automation
    /// pattern at all — and both are cheap reads that send nothing. Answering them here is what
    /// turns "the open step failed" into a specific piece of work for Part B.
    /// </remarks>
    private static void DescribeOpenSurface(
        MeituReadiness ready, MeituBaseline baseline, Action<string> log)
    {
        Win32ExternalAppWindowLocator locator = new();
        UiaElementProvider elements = new();

        log(string.Empty);
        log("### Diagnostic (read-only): what the open path had to work with");

        OperationResult<IReadOnlyList<ExternalWindowRef>> windows =
            locator.FindTopLevelWindows(ready.Target.Process);
        if (windows.IsSuccess)
        {
            log($"top-level windows    : {windows.Value.Count}");
            foreach (ExternalWindowRef window in windows.Value)
            {
                log($"  {window.Handle} class='{window.ClassName}' title='{window.Title}' bounds={window.Bounds}");
            }
        }

        OperationResult<UiElementRef> entry = elements.Find(
            ready.Target.Window.Handle,
            new UiElementQuery(UiControlKind.Any, Name: baseline.WelcomeMarkers.Contains("图片编辑") ? "图片编辑" : null));

        if (entry.IsFailure)
        {
            log($"start-page entry     : NOT FOUND — {entry.Failure.Code}");
            return;
        }

        log($"start-page entry     : found ('{entry.Value.Name}')");

        // Whether UI Automation can activate this control at all is the single fact that decides
        // what Part B has to build: an invokable element means the preferred route works and the
        // problem is elsewhere, while a bare Text element means Meitu's start page is not
        // automatable through UIA and needs the next option down §4's priority list.
        if (entry.Value.Native is System.Windows.Automation.AutomationElement native)
        {
            log($"  control type       : {native.Current.ControlType.ProgrammaticName}");
            log($"  automation id      : '{native.Current.AutomationId}'");
            log($"  enabled / offscreen: {native.Current.IsEnabled} / {native.Current.IsOffscreen}");
            log($"  supported patterns : {string.Join(", ",
                native.GetSupportedPatterns().Select(p => p.ProgrammaticName))}");
        }
    }

    /// <summary>
    /// Creates a small synthetic PNG in a throwaway workspace laid out like a real session.
    /// </summary>
    /// <remarks>
    /// A generated image, never a customer file and never one of the Epic 11000 fixtures: the
    /// smoke has to be safe to run repeatedly and safe to delete afterwards.
    /// </remarks>
    private static (string AbsolutePath, WorkspaceFileRef Reference, IWorkspace Workspace)
        PrepareSyntheticWorkingCopy(string root)
    {
        WorkspaceFileRef reference = WorkspaceFileRef.Create(
            "Sessions/S_SMOKE/Working/A_1/PRINTFLOW-SMOKE.png", WorkspaceArea.Working);

        IWorkspace workspace = new FileWorkspace(root);
        string absolute = workspace.ResolveAbsolute(reference);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllBytes(absolute, SyntheticImages.Png(320, 240, dpi: 300, alpha: true));

        return (absolute, reference, workspace);
    }

    /// <summary>
    /// Writes the transcript beside the test output so it can be pasted into the report.
    /// </summary>
    /// <remarks>
    /// Text only. Any window capture the run produced stays in the evidence directory and is
    /// deleted below — screenshots are never committed and never leave the workstation (§20, §31).
    /// </remarks>
    private static void WriteTranscript(string transcript)
    {
        if (transcript.Length == 0)
        {
            return;
        }

        string path = Path.Combine(
            Path.GetTempPath(),
            string.Create(CultureInfo.InvariantCulture, $"printflow-meitu-smoke-{DateTimeOffset.Now:yyyyMMddHHmmss}.md"));

        File.WriteAllText(path, transcript, Encoding.UTF8);
        Console.WriteLine($"transcript written to {path}");
    }

    private static void CleanUp(string root, string evidenceDirectory)
    {
        if (Directory.Exists(evidenceDirectory))
        {
            Console.WriteLine(
                $"evidence captures in {evidenceDirectory}: " +
                $"{Directory.EnumerateFiles(evidenceDirectory).Count()} (deleted below, never committed)");
        }

        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Meitu may still hold the working copy open; leaving a temp file behind is the
            // right trade against failing a smoke that already produced its answer.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string RepositoryFile(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        return current is null
            ? throw new InvalidOperationException("Could not locate the repository root.")
            : Path.Combine(current.FullName, relativePath);
    }
}
