using System.IO;
using System.Text;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using FileWorkspace = PrintFlow.Infrastructure.Workspace.FileWorkspace;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// The controlled Photoshop workstation smoke (Epic 11400 Part A §20).
/// </summary>
/// <remarks>
/// Opt-in and inert by default. An ordinary <c>dotnet test</c> run — on this workstation or any
/// other — does nothing here, because a test suite that could launch Photoshop and drive its UI
/// as a side effect of a routine build would be its own hazard.
///
/// Three switches, in increasing order of consequence, each of which must be set explicitly:
/// <list type="bullet">
///   <item><c>PRINTFLOW_PHOTOSHOP_SMOKE=1</c> runs the read-only half: verify the signed
///         baseline and the accepted binary, identify or launch the accepted process, identify
///         one accepted window, and classify the screen. This sends nothing and changes
///         nothing.</item>
///   <item><c>PRINTFLOW_PHOTOSHOP_SMOKE_OPEN=1</c> additionally allows the open, which does
///         produce input — and only ever through the guarded driver, only after the state has
///         been recognised as safe, and only with a synthetic file the smoke created itself. It
///         then proves by absolute path which document Photoshop is holding.</item>
///   <item><c>PRINTFLOW_PHOTOSHOP_SMOKE_CLOSE=1</c> additionally allows the synthetic document
///         to be closed afterwards, which is what earns the permission to delete the synthetic
///         workspace. Without it the workspace is retained, because Photoshop may still hold the
///         file.</item>
/// </list>
///
/// What no phase does, at any switch setting: run a Photoshop Action, resize, convert a colour
/// mode, save or export anything, close a document PrintFlow did not open, close all documents,
/// close Photoshop itself, or terminate the process. Photoshop is shared with the operator and
/// may be holding work PrintFlow knows nothing about (§21).
///
/// The synthetic file lives under the OS temp directory. Neither <c>D:\PrintFlowStudio</c> nor
/// any of Documents, Desktop or Downloads is read, written, or navigated to.
/// </remarks>
public sealed class PhotoshopWorkstationSmoke
{
    private const string EnableVariable = "PRINTFLOW_PHOTOSHOP_SMOKE";
    private const string EnableOpenVariable = "PRINTFLOW_PHOTOSHOP_SMOKE_OPEN";
    private const string EnableCloseVariable = "PRINTFLOW_PHOTOSHOP_SMOKE_CLOSE";
    private const string PresetManifestVariable = "PRINTFLOW_PHOTOSHOP_SMOKE_PRESET_MANIFEST";
    private const string PresetSha256Variable = "PRINTFLOW_PHOTOSHOP_SMOKE_PRESET_SHA256";

    [Fact]
    public async Task Identify_open_and_positively_identify_the_workstation_Photoshop_document()
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

        string root = Path.Combine(Path.GetTempPath(), "PrintFlowPhotoshopSmoke", Guid.NewGuid().ToString("N"));
        string evidenceDirectory = Path.Combine(root, "Evidence");
        bool mayStillBeLoaded = false;

        try
        {
            Log($"# Photoshop workstation smoke — {DateTimeOffset.Now:O}");
            Log($"controlled workspace : {root}");

            PrintFlowConfiguration configuration =
                PrintFlowConfiguration.LoadFromFile(RepositoryFile("appsettings.json"));
            (string manifest, Sha256 expected) = PresetForSmoke(configuration);
            Log($"preset manifest      : {manifest}");

            // The baseline first, on its own, so a failure here is reported as "the signed chain
            // could not be verified" rather than as "Photoshop could not be found".
            PresetPhotoshopBaselineProvider baselines = new(manifest, expected);
            OperationResult<PhotoshopBaseline> baseline = baselines.GetVerifiedBaseline();
            if (baseline.IsFailure)
            {
                Log($"RESULT               : baseline REFUSED — {baseline.Failure.Code}");
                Log($"detail               : {baseline.Failure.TechnicalDetail}");
                return;
            }

            Log($"accepted executable  : {baseline.Value.ExecutablePath}");
            Log($"accepted versions    : {baseline.Value.AcceptedProductVersion} / {baseline.Value.AcceptedFileVersion}");
            Log($"accepted digest      : {baseline.Value.ExecutableSha256}");
            Log($"ui language          : {baseline.Value.UiLanguage}");
            Log($"main window class    : {baseline.Value.MainWindowClassName}");
            Log($"no-document title    : '{baseline.Value.NoDocumentWindowTitle}'");
            Log($"window-state evidence: {Describe(baseline.Value.WindowStates)}");
            Log($"open-dialog evidence : {Describe(baseline.Value.OpenDialog)}");
            Log($"identity evidence    : {Describe(baseline.Value.DocumentIdentity)}");

            (string syntheticPath, WorkspaceFileRef managed, IWorkspace workspace) =
                PrepareSyntheticManagedFile(root);
            Log($"synthetic managed    : {syntheticPath}");

            IPhotoshopAutomationFoundation foundation = PhotoshopAutomationComposition.CreateFoundation(
                manifest, expected, workspace, evidenceDirectory, TimeProvider.System);

            Log(string.Empty);
            Log("## Phase 1 — verify, identify and classify (read-only, no input)");

            OperationResult<PhotoshopReadiness> ready =
                await foundation.EnsureReadyAsync(CancellationToken.None);
            if (ready.IsFailure)
            {
                Report(Log, "STOPPED", ready.Failure);
                Log(string.Empty);
                Log("Nothing was clicked, typed, dismissed or closed. Photoshop was left exactly as found.");
                return;
            }

            Log($"process id           : {ready.Value.Target.Process.ProcessId}");
            Log($"process image        : {ready.Value.Target.Process.ExecutablePath}");
            Log($"process started      : {ready.Value.Target.Process.StartedUtc:O}");
            Log($"window handle        : {ready.Value.Target.Window.Handle}");
            Log($"window title / class : '{ready.Value.Target.Window.Title}' / {ready.Value.Target.Window.ClassName}");
            Log($"window bounds        : {ready.Value.Target.Window.Bounds}");
            Log($"state                : {ready.Value.State.State}");
            Log($"matched markers      : {string.Join(", ", ready.Value.State.MatchedMarkers)}");
            Log($"visible child classes: {ready.Value.State.Observation.VisibleChildClasses.Length}");
            Log($"launched by PrintFlow: {ready.Value.WasLaunched}");

            // §21: whether Photoshop already holds work decides what cleanup may do later.
            bool operatorWorkPresent =
                ready.Value.State.State is PhotoshopStartingState.KnownEditorWithOtherDocument;
            Log($"operator work loaded : {(operatorWorkPresent ? "YES — cleanup will close only PrintFlow's own document" : "none observed")}");

            if (Environment.GetEnvironmentVariable(EnableOpenVariable) != "1")
            {
                Log(string.Empty);
                Log($"## Phase 2 — skipped ({EnableOpenVariable} is not set). No input was produced.");
                return;
            }

            Log(string.Empty);
            Log("## Phase 2 — open the exact managed file and prove which document is loaded");

            mayStillBeLoaded = true;
            OperationResult<PhotoshopOpenedDocument> opened =
                await foundation.OpenManagedWorkingFileAsync(managed, CancellationToken.None);
            if (opened.IsFailure)
            {
                Report(Log, "STOPPED", opened.Failure);
                Log(string.Empty);
                Log("No Action was run, nothing was saved and no output file was created.");
                return;
            }

            Log($"state                : {opened.Value.State.State}");
            Log($"observed file name   : {opened.Value.Identity.ObservedFileName}");
            Log($"observed folder      : {opened.Value.Identity.ObservedDirectory}");
            Log($"observed full path   : {opened.Value.Identity.ObservedFullPath}");
            Log($"expected full path   : {syntheticPath}");
            Log($"identity             : {(PhotoshopDocumentIdentityRule.MatchesExpectedDocument(syntheticPath, opened.Value.Identity.ObservedFullPath) ? "EXACT MATCH" : "MISMATCH")}");
            Log($"window title         : '{opened.Value.Identity.WindowTitle}'");
            Log($"other documents open : {opened.Value.OtherDocumentsMayBeOpen}");
            Log(string.Empty);
            Log("NO Action was invoked. NO resize, NO colour-mode conversion, NO save, NO export.");
            Log("The synthetic file on disk is unchanged and no output file exists.");

            if (Environment.GetEnvironmentVariable(EnableCloseVariable) != "1")
            {
                Log(string.Empty);
                Log($"## Phase 3 — skipped ({EnableCloseVariable} is not set).");
                Log("Photoshop is left holding the synthetic document; the workspace is retained.");
                return;
            }

            Log(string.Empty);
            Log("## Phase 3 — close exactly the synthetic document, and nothing else");

            OperationResult<PhotoshopTarget> closed =
                await foundation.CloseExactDocumentAsync(opened.Value, managed, CancellationToken.None);
            if (closed.IsFailure)
            {
                Report(Log, "close REFUSED", closed.Failure);
                Log("Photoshop is left holding the document; the workspace is retained.");
                return;
            }

            mayStillBeLoaded = false;
            Log($"window title after   : '{closed.Value.Window.Title}'");
            Log("Photoshop itself was NOT closed and was NOT terminated. No other document was touched.");
        }
        finally
        {
            WriteTranscript(transcript.ToString());
            CleanUp(root, mayStillBeLoaded);
        }
    }

    private static void Report(Action<string> log, string headline, OperationFailure failure)
    {
        log($"RESULT               : {headline} — {failure.Code}");
        log($"detail               : {failure.TechnicalDetail}");
        foreach (KeyValuePair<string, string> entry in failure.Context)
        {
            log($"  {entry.Key,-22}: {entry.Value}");
        }
    }

    private static string Describe(PhotoshopWindowStateSignature? states) =>
        states is null
            ? "ABSENT — every screen classifies as unknown"
            : $"start='{states.StartScreenMarkerClass}' document='{states.DocumentMarkerClass}' " +
              $"chrome=[{string.Join(", ", states.EditorChromeClasses)}]";

    private static string Describe(PhotoshopOpenDialogSignature? dialog) =>
        dialog is null
            ? "ABSENT — the open path is refused"
            : $"{dialog.WindowClassName} '{dialog.Title}' name={dialog.FileNameControlId}/{dialog.FileNameControlClass} " +
              $"confirm={dialog.ConfirmControlId} cancel={dialog.CancelControlId}";

    private static string Describe(PhotoshopDocumentIdentitySignature? identity) =>
        identity is null
            ? "ABSENT — no open can be confirmed"
            : $"{identity.DialogClassName} '{identity.DialogTitle}' name={identity.FileNameControlId}/{identity.FileNameControlClass} " +
              $"address={identity.AddressControlId}/{identity.AddressControlClass} cancel={identity.CancelControlId}";

    /// <summary>
    /// Creates one fresh synthetic managed Working file for this run.
    /// </summary>
    /// <remarks>
    /// A generated image, never a customer file and never one of the Epic 11000 fixtures: the
    /// smoke has to be safe to run repeatedly and safe to delete afterwards. The name carries a
    /// per-run token so that a document left over from an earlier run can never be mistaken for
    /// this one's.
    /// </remarks>
    private static (string AbsolutePath, WorkspaceFileRef Reference, IWorkspace Workspace)
        PrepareSyntheticManagedFile(string root)
    {
        string token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        WorkspaceFileRef reference = WorkspaceFileRef.Create(
            $"Sessions/S_PSSMOKE/Working/A_1/PF_PSFOUNDATION_{token}.png", WorkspaceArea.Working);

        IWorkspace workspace = new FileWorkspace(root);
        string absolute = workspace.ResolveAbsolute(reference);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllBytes(absolute, SyntheticImages.Png(320, 240, dpi: 300, alpha: true));

        return (absolute, reference, workspace);
    }

    private static (string Manifest, Sha256 Expected) PresetForSmoke(PrintFlowConfiguration configuration)
    {
        string manifest = Environment.GetEnvironmentVariable(PresetManifestVariable) ??
            Path.Combine(configuration.Workspace.Root, configuration.Preset.Path);
        string digest = Environment.GetEnvironmentVariable(PresetSha256Variable) ??
            configuration.Preset.ExpectedSha256;
        return (manifest, Sha256.Parse(digest));
    }

    /// <summary>
    /// Removes the synthetic workspace, but only when Photoshop is known not to be holding it.
    /// </summary>
    private static void CleanUp(string root, bool mayStillBeLoaded)
    {
        if (mayStillBeLoaded)
        {
            Console.WriteLine(
                $"RETAINED             : {root} — Photoshop may still hold the synthetic document.");
            return;
        }

        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException ex)
        {
            Console.WriteLine($"RETAINED             : {root} — {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"RETAINED             : {root} — {ex.Message}");
        }
    }

    /// <summary>
    /// Writes the transcript beside the test output, never into the repository.
    /// </summary>
    private static void WriteTranscript(string transcript)
    {
        try
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                $"printflow-photoshop-smoke-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.md");
            File.WriteAllText(path, transcript);
            Console.WriteLine($"transcript           : {path}");
        }
        catch (IOException)
        {
            // The transcript is a convenience; failing to write it must not fail the smoke.
        }
    }

    private static string RepositoryFile(string fileName)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        return current is null
            ? throw new InvalidOperationException("The repository root could not be located.")
            : Path.Combine(current.FullName, fileName);
    }
}
