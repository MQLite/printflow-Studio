using System.Globalization;
using System.IO;
using System.Text;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
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
/// Five switches, in increasing order of consequence, each of which must be set explicitly:
/// <list type="bullet">
///   <item><c>PRINTFLOW_MEITU_SMOKE=1</c> runs the read-only half: verify the signed baseline,
///         identify the accepted process and window, and classify the state. This sends nothing
///         and changes nothing.</item>
///   <item><c>PRINTFLOW_MEITU_SMOKE_OPEN=1</c> additionally allows the open step, which does
///         produce input — and only ever through the guarded driver, only after the state has
///         been recognised as safe, and only with a synthetic file the smoke created itself.</item>
///   <item><c>PRINTFLOW_MEITU_SMOKE_ENHANCE=1</c> additionally allows the Enhancement action,
///         which is irreversible: it changes the document Meitu is holding. Gated separately
///         from the open for exactly that reason.</item>
///   <item><c>PRINTFLOW_MEITU_SMOKE_EXPORT=1</c> additionally allows the export, which writes a
///         real file — the first thing this smoke has ever been able to leave behind on disk.
///         It goes to the attempt's own controlled directory and nowhere else (Part B2B §7).</item>
///   <item><c>PRINTFLOW_MEITU_SMOKE_CLOSE=1</c> additionally allows the document to be closed
///         afterwards, which is what earns the permission to delete the synthetic workspace.
///         Without it the workspace is retained, because Meitu may still hold the file
///         (Part B2A §28).</item>
/// </list>
///
/// No phase closes, dismisses or answers anything on its own initiative, with one signed
/// exception added in Part B2B: Meitu's own post-save confirmation surface, which is dismissed
/// through its own signed close control because it disables the editor while it is up. The
/// modified-document save prompt is not that surface and is never answered — it carries none of
/// the signed markers, and a Meitu-owned modal still stops the smoke (Part B2A §19, Part B2B §24).
///
/// The working copy lives under the OS temp directory (§18). Neither <c>D:\PrintFlowStudio</c>
/// nor any of Documents, Desktop or Downloads is read, written, or navigated to.
/// </remarks>
public sealed class MeituWorkstationSmoke
{
    private const string EnableVariable = "PRINTFLOW_MEITU_SMOKE";
    private const string EnableOpenVariable = "PRINTFLOW_MEITU_SMOKE_OPEN";
    private const string EnableEnhanceVariable = "PRINTFLOW_MEITU_SMOKE_ENHANCE";
    private const string EnableExportVariable = "PRINTFLOW_MEITU_SMOKE_EXPORT";
    private const string EnableCloseVariable = "PRINTFLOW_MEITU_SMOKE_CLOSE";

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
        bool mayStillBeLoaded = false;

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

            // Read before Meitu is given the file, because both halves of what it is needed for
            // are only meaningful taken first: the dimensions the result is measured against,
            // and the digest that proves the input survived its own attempt (§17, §19).
            OperationResult<FileFacts> sourceFacts =
                await foundation.InspectManagedFileAsync(workingCopy, CancellationToken.None);
            if (sourceFacts.IsFailure)
            {
                Log($"RESULT               : the synthetic working copy could not be inspected — " +
                    $"{sourceFacts.Failure.Code}");
                return;
            }

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

            Log(string.Empty);
            Log("## Phase 1b — resolve the start-page card structurally (read-only, no input)");

            if (ready.Value.State.State == MeituStartingState.KnownWelcome)
            {
                DescribeCardTarget(ready.Value, baseline.Value, Log);
            }
            else
            {
                // The card only exists on the start page. Running the resolution against another
                // screen and printing its refusal would read like a defect rather than the
                // rule declining to find a control that is not there.
                Log($"skipped              : Meitu is on {ready.Value.State.State}, not the start page.");
            }

            if (Environment.GetEnvironmentVariable(EnableOpenVariable) != "1")
            {
                Log(string.Empty);
                Log($"## Phase 2 — skipped ({EnableOpenVariable} is not set). No input was produced.");
                return;
            }

            Log(string.Empty);
            Log("## Phase 2 — open the synthetic working copy (guarded input)");

            // From this point onward even a refused result may follow a successful Open. Retain
            // the workspace until an operator or the calling diagnostic has positively closed
            // the image; deleting a file while Meitu holds it is not a safe cleanup strategy.
            mayStillBeLoaded = true;
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
            Log($"expected file        : {workingCopy.FileName}");
            Log($"observed identity    : {opened.Value.State.Observation.ObservedDocumentIdentity}");
            if (Environment.GetEnvironmentVariable(EnableEnhanceVariable) != "1")
            {
                Log(string.Empty);
                Log($"## Phase 3 — skipped ({EnableEnhanceVariable} is not set). No Enhancement was invoked.");
                return;
            }

            Log(string.Empty);
            Log("## Phase 3 — guarded Enhancement (Epic 11300 Part B2A §27)");
            Log("sequence             : identity → re-acquire editor → structural target → final guard");
            Log("                       → one invoke → Busy → positive completion → identity again");

            DescribeEnhancementTarget(opened.Value.Target, baseline.Value, Log);

            OperationResult<MeituEnhancementOutcome> enhanced = await foundation.EnhanceAsync(
                opened.Value, workingCopy, CancellationToken.None);

            if (enhanced.IsFailure)
            {
                Log($"RESULT               : ENHANCEMENT STOPPED — {enhanced.Failure.Code}");
                Log($"detail               : {enhanced.Failure.TechnicalDetail}");
                foreach (KeyValuePair<string, string> entry in enhanced.Failure.Context)
                {
                    Log($"  {entry.Key,-20}: {entry.Value}");
                }

                Log(string.Empty);
                Log("No output was exported and no Revision was created.");
                return;
            }

            Log($"identity before      : {enhanced.Value.IdentityBeforeEnhancement.State} " +
                $"({enhanced.Value.ObservedDocumentIdentity})");
            Log($"busy observed        : {enhanced.Value.Busy.State}");
            Log($"completion observed  : {enhanced.Value.Completion.State}");
            Log($"identity after       : {enhanced.Value.IdentityAfterEnhancement.State}");
            Log($"editor re-verified   : '{enhanced.Value.Target.Window.Title}' " +
                $"(process {enhanced.Value.Target.Process.ProcessId})");
            Log($"enhancement origin   : " +
                (opened.Value.Load.AutoStartedEnhancement
                    ? "Meitu auto-started it on this load; PrintFlow waited it out and invoked nothing"
                    : "PrintFlow invoked the signed control once"));

            if (Environment.GetEnvironmentVariable(EnableExportVariable) != "1")
            {
                Log(string.Empty);
                Log($"## Phase 4 — skipped ({EnableExportVariable} is not set).");
                Log("No export, no Save, no Save As, no transformed output, no Revision.");
                return;
            }

            Log(string.Empty);
            Log("## Phase 4 — export to the controlled attempt path (Epic 11300 Part B2B §35)");

            WorkspaceFileRef output = OutputBesideWorkingCopy(workingCopy);
            Log($"controlled output    : {output.RelativePath}");
            Log($"source facts         : {Describe(sourceFacts.Value)}");

            OperationResult<MeituExportedOutput> exported = await foundation.ExportEnhancedResultAsync(
                enhanced.Value, workingCopy, sourceFacts.Value, output, CancellationToken.None);

            if (exported.IsFailure)
            {
                Log($"RESULT               : EXPORT STOPPED — {exported.Failure.Code}");
                Log($"detail               : {exported.Failure.TechnicalDetail}");
                foreach (KeyValuePair<string, string> entry in exported.Failure.Context)
                {
                    Log($"  {entry.Key,-20}: {entry.Value}");
                }

                Log(string.Empty);
                Log("No validated output exists, so no Revision may be created.");
                return;
            }

            FileFacts facts = exported.Value.Facts;
            Log($"requested base name  : {exported.Value.Evidence.RequestedBaseName}");
            Log($"confirmed format     : {exported.Value.Evidence.ConfirmedFormatValue}");
            Log($"destination dialog   : '{exported.Value.Evidence.DestinationDialogTitle}'");
            Log($"observations to settle: {exported.Value.ObservationsToSettle}");
            Log($"output relative path : {exported.Value.File.RelativePath}");
            Log($"output facts         : {Describe(facts)}");
            Log($"output DPI           : {facts.DpiX?.ToString("0.##", CultureInfo.InvariantCulture) ?? "(unread)"}" +
                $" x {facts.DpiY?.ToString("0.##", CultureInfo.InvariantCulture) ?? "(unread)"}");
            Log($"output colour/alpha  : {facts.ColourMode} / {facts.HasAlpha?.ToString() ?? "(unread)"}");
            Log($"output SHA-256       : {facts.Sha256}");
            Log("source unchanged     : verified byte-for-byte by the export route (§19)");

            Log(string.Empty);
            Log("STOP. A validated file exists on the controlled path. No Revision was created: " +
                "this smoke composes no session, no repository and no workflow engine.");

            if (Environment.GetEnvironmentVariable(EnableCloseVariable) != "1")
            {
                Log(string.Empty);
                Log($"## Phase 5 — skipped ({EnableCloseVariable} is not set).");
                Log("The document stays loaded, so the synthetic workspace is retained (§28).");
                return;
            }

            Log(string.Empty);
            Log("## Phase 5 — release the working file (§28)");

            // Meitu's save-confirmation surface disables the editor while it is up, so the close
            // control's own guard would see a blocking modal and stop. Dismissing it first is
            // what §24 means by returning the editor to a neutral state, and it is the only
            // Meitu-owned surface PrintFlow dismisses — through its own signed close control,
            // identified by markers, never by window shape.
            OperationResult<bool> dismissed =
                await foundation.DismissExportResultSurfaceAsync(enhanced.Value.Target, CancellationToken.None);
            Log($"result surface       : {(dismissed.IsFailure ? "NOT DISMISSED — " + dismissed.Failure.Code : dismissed.Value ? "dismissed through its signed close control" : "none was showing")}");

            OperationResult<MeituTarget> closed = await foundation.CloseDocumentAsync(
                enhanced.Value.Target, CancellationToken.None);

            if (closed.IsFailure)
            {
                Log($"close                : NOT CLOSED — {closed.Failure.Code}");
                Log($"detail               : {closed.Failure.TechnicalDetail}");
                Log("The synthetic workspace is retained; Meitu may still hold the file.");
                return;
            }

            // Only now has the permission to delete been earned. Everywhere else in this smoke,
            // `mayStillBeLoaded` stays true and the workspace survives (§28).
            mayStillBeLoaded = false;
            Log("close                : the editor reached the signed empty state; the file is released.");

        }
        finally
        {
            WriteTranscript(transcript.ToString());
            CleanUp(root, evidenceDirectory, mayStillBeLoaded);
        }
    }

    /// <summary>
    /// Reports which control the structural rule resolves the signed marker to, without
    /// invoking it.
    /// </summary>
    /// <remarks>
    /// This is the fact Part A could not state and Part B1 exists to establish, so the smoke
    /// prints it before anything is invoked rather than inferring it afterwards from whether the
    /// open worked. It builds its own read-only driver over the same real seams the foundation
    /// uses; <c>FindKnownElement</c> produces no input.
    /// </remarks>
    private static void DescribeCardTarget(
        MeituReadiness ready, MeituBaseline baseline, Action<string> log)
    {
        if (baseline.StartPageCard is not { } shape)
        {
            log("card shape           : NOT SIGNED — the verified chain vouches for no card structure.");
            return;
        }

        log($"signed card shape    : {shape.LabelControlType}/{shape.LabelClassName}" +
            $"'{shape.LabelAutomationIdSuffix}' → {shape.CardControlType}/{shape.CardClassName}" +
            $"'{shape.CardAutomationIdSuffix}' requiring {shape.RequiredCardPattern}");

        Win32ExternalAppWindowLocator locator = new();
        UiaElementProvider elements = new();
        GuardedMeituUiDriver driver = new(
            locator, elements, new Win32ScopedInputSink(locator), new NullEvidenceSink(),
            new FixedBaselineProvider(baseline), new MeituAutomationOptions(), TimeProvider.System);

        OperationResult<UiElementRef> card = driver.FindKnownElement(
            ready.Target, KnownMeituElement.WelcomeOpenEntry);

        if (card.IsFailure)
        {
            log($"resolved card        : REFUSED — {card.Failure.Code}");
            log($"detail               : {card.Failure.TechnicalDetail}");
            foreach (KeyValuePair<string, string> entry in card.Failure.Context)
            {
                log($"  {entry.Key,-20}: {entry.Value}");
            }

            return;
        }

        OperationResult<UiElementIdentity> identity = elements.Describe(card.Value);
        log(identity.IsSuccess
            ? $"resolved card        : {identity.Value}"
            : $"resolved card        : located, but unreadable — {identity.Failure.Code}");

        if (identity.IsSuccess)
        {
            log($"  patterns           : {string.Join(", ", identity.Value.SupportedPatterns)}");
            log($"  bounds / enabled   : {identity.Value.Bounds} / {identity.Value.IsEnabled}");
        }
    }

    /// <summary>
    /// Reports which control the structural rule resolves the signed Enhancement marker to,
    /// without invoking it (Epic 11300 Part B2A §7).
    /// </summary>
    /// <remarks>
    /// Printed before the action rather than inferred afterwards from whether it worked, and for
    /// the same reason the start-page card is: "PrintFlow invoked the right control" is a claim
    /// about structure, and the structure is what the transcript has to record.
    /// </remarks>
    private static void DescribeEnhancementTarget(
        MeituTarget target, MeituBaseline baseline, Action<string> log)
    {
        if (baseline.Enhancement is not { } enhancement)
        {
            log("enhancement evidence : NOT SIGNED — the verified chain vouches for no Enhancement route.");
            return;
        }

        MeituOwnedControlShape shape = enhancement.ActionControl;
        log($"signed action shape  : {shape.MarkerControlType}/{shape.MarkerClassName}" +
            $"'{shape.MarkerAutomationIdSuffix}' → {shape.OwnerControlType}/{shape.OwnerClassName}" +
            $"'{shape.OwnerAutomationIdSuffix}' at depth {shape.OwnerAncestorDepth}, " +
            $"requiring {shape.RequiredOwnerPattern}");
        log($"signed busy markers  : {string.Join(", ", enhancement.Busy.RequiredMarkers)} " +
            $"(at least {enhancement.Busy.MinimumRequiredMarkers})");
        log($"signed completion    : {string.Join(", ", enhancement.Completion.RequiredMarkers)} " +
            $"(at least {enhancement.Completion.MinimumRequiredMarkers}, " +
            $"busy absent: {enhancement.Completion.RequiresBusyAbsent})");

        Win32ExternalAppWindowLocator locator = new();
        UiaElementProvider elements = new();
        GuardedMeituUiDriver driver = new(
            locator, elements, new Win32ScopedInputSink(locator), new NullEvidenceSink(),
            new FixedBaselineProvider(baseline), new MeituAutomationOptions(), TimeProvider.System);

        OperationResult<UiElementRef> action = driver.FindKnownElement(
            target, KnownMeituElement.EditorEnhancementAction);

        if (action.IsFailure)
        {
            log($"resolved action      : REFUSED — {action.Failure.Code}");
            log($"detail               : {action.Failure.TechnicalDetail}");
            foreach (KeyValuePair<string, string> entry in action.Failure.Context)
            {
                log($"  {entry.Key,-20}: {entry.Value}");
            }

            return;
        }

        OperationResult<UiElementIdentity> identity = elements.Describe(action.Value);
        log(identity.IsSuccess
            ? $"resolved action      : {identity.Value}"
            : $"resolved action      : located, but unreadable — {identity.Failure.Code}");

        if (identity.IsSuccess)
        {
            log($"  patterns           : {string.Join(", ", identity.Value.SupportedPatterns)}");
            log($"  bounds / enabled   : {identity.Value.Bounds} / {identity.Value.IsEnabled}");
        }
    }

    /// <summary>A baseline provider over an already-verified baseline, for the smoke's own driver.</summary>
    private sealed class FixedBaselineProvider(MeituBaseline baseline) : IMeituBaselineProvider
    {
        public OperationResult<MeituBaseline> GetVerifiedBaseline() => OperationResult.Ok(baseline);
    }

    /// <summary>An evidence sink for the read-only diagnostic, which captures nothing.</summary>
    private sealed class NullEvidenceSink : IAutomationEvidenceSink
    {
        public OperationResult<EvidenceRef> CaptureWindow(ExternalWindowRef window, string reason) =>
            OperationResult.Fail<EvidenceRef>(
                FailureCode.WorkspaceError, "The read-only diagnostic captures no evidence.");
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

        // What the main window is showing now. After the card has been invoked this is the
        // question §7 asks — did Meitu raise a picker, change to an editor, or do neither? — and
        // it is answered by looking rather than by assuming which of them happened.
        OperationResult<ExternalWindowRef> main = locator.Refresh(ready.Target.Window.Handle);
        if (main.IsSuccess)
        {
            log($"main window now      : '{main.Value.Title}' class='{main.Value.ClassName}' " +
                $"enabled={main.Value.IsEnabled} bounds={main.Value.Bounds}");
        }

        OperationResult<IReadOnlyList<string>> texts =
            elements.ReadTextSnapshot(ready.Target.Window.Handle, 400);
        if (texts.IsSuccess)
        {
            string[] distinct = [.. texts.Value.Distinct(StringComparer.Ordinal).Take(60)];
            log($"visible names ({texts.Value.Count,3})  : {string.Join(" | ", distinct)}");
            log($"signed welcome markers still visible: " +
                $"{baseline.WelcomeMarkers.Count(m => texts.Value.Any(t => t.Contains(m, StringComparison.Ordinal)))}" +
                $" of {baseline.WelcomeMarkers.Length}");
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
        string token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        WorkspaceFileRef reference = WorkspaceFileRef.Create(
            $"Sessions/S_SMOKE/Working/A_1/PF_IDENTITY_FINAL_{token}.png", WorkspaceArea.Working);

        IWorkspace workspace = new FileWorkspace(root);
        string absolute = workspace.ResolveAbsolute(reference);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllBytes(absolute, SyntheticImages.Png(320, 240, dpi: 300, alpha: true));

        return (absolute, reference, workspace);
    }

    /// <summary>
    /// One real Enhancement through the workflow seam itself (Epic 11300 Part B2B §35, §37).
    /// </summary>
    /// <remarks>
    /// The phased smoke above proves each step separately, which is what makes a failure
    /// diagnosable. This proves the thing a workflow would actually call: one
    /// <c>IMeituProcessor.ProcessAsync</c>, returning a validated <c>AdapterOutput</c> or a
    /// structured failure, with every step in between hidden behind the seam exactly as
    /// <c>SessionService</c> would see it.
    ///
    /// It composes no session, no repository and no workflow engine, so no attempt row and no
    /// Revision is reachable from here, and <c>IEnvironmentGate</c>'s refusal of Production
    /// adapters in the application composition is untouched. §37 asks for the seam to be proved
    /// without enabling Production broadly; this is the whole of what that permits.
    ///
    /// Gated on its own variable, and on the export variable too, because it exports.
    /// </remarks>
    [Fact]
    public async Task Run_one_Enhancement_through_the_production_adapter_seam()
    {
        if (Environment.GetEnvironmentVariable(EnableSeamVariable) != "1" ||
            Environment.GetEnvironmentVariable(EnableExportVariable) != "1")
        {
            return;
        }

        StringBuilder transcript = new();
        void Log(string line)
        {
            transcript.AppendLine(line);
            Console.WriteLine(line);
        }

        string root = Path.Combine(Path.GetTempPath(), "PrintFlowMeituSeam", Guid.NewGuid().ToString("N"));
        string evidenceDirectory = Path.Combine(root, "Evidence");

        try
        {
            Log($"# Meitu production-seam smoke — {DateTimeOffset.Now:O}");

            PrintFlowConfiguration configuration =
                PrintFlowConfiguration.LoadFromFile(RepositoryFile("appsettings.json"));
            string manifest = Path.Combine(configuration.Workspace.Root, configuration.Preset.Path);
            Sha256 expected = Sha256.Parse(configuration.Preset.ExpectedSha256);

            (string workingCopyPath, WorkspaceFileRef workingCopy, IWorkspace workspace) =
                PrepareSyntheticWorkingCopy(root);
            WorkspaceFileRef output = OutputBesideWorkingCopy(workingCopy);

            Log($"working copy         : {workingCopy.RelativePath}");
            Log($"expected output      : {output.RelativePath}");
            Log($"on disk              : {workingCopyPath}");

            IMeituProcessor adapter = MeituAutomationComposition.CreateProductionProcessor(
                manifest, expected, workspace, evidenceDirectory, TimeProvider.System);

            Log($"adapter id           : {adapter.AdapterId}");
            Log($"adapter mode         : {adapter.Mode}");

            OperationResult<AdapterOutput> result = await adapter.ProcessAsync(
                new MeituRequest(
                    workingCopy, MeituOperation.Enhance, ParentOf(workingCopy), output),
                CancellationToken.None);

            if (result.IsFailure)
            {
                Log($"RESULT               : REFUSED — {result.Failure.Code}");
                Log($"detail               : {result.Failure.TechnicalDetail}");
                foreach (KeyValuePair<string, string> entry in result.Failure.Context)
                {
                    Log($"  {entry.Key,-20}: {entry.Value}");
                }

                Log(string.Empty);
                Log("No AdapterOutput was produced, so no Revision could be created from this run.");
                return;
            }

            Log($"RESULT               : SUCCESS");
            Log($"produced file        : {result.Value.ProducedFile.RelativePath}");
            Log($"elapsed              : {result.Value.Elapsed.TotalSeconds:0.0} s");
            Log($"adapter notes        : {result.Value.AdapterNotes}");
            Log(string.Empty);
            Log("This is an AdapterOutput and nothing more. No session, repository or workflow " +
                "engine is composed here, so no attempt row and no Revision exists.");

            // Background removal, through the same seam, on the same machine.
            OperationResult<AdapterOutput> cutout = await adapter.ProcessAsync(
                new MeituRequest(
                    workingCopy, MeituOperation.RemoveBackground, ParentOf(workingCopy),
                    SiblingOf(workingCopy, "unused_CUTOUT.png")),
                CancellationToken.None);

            Log(string.Empty);
            Log($"background removal   : {(cutout.IsFailure ? "REFUSED — " + cutout.Failure.Code : "UNEXPECTED SUCCESS")}");
            if (cutout.IsFailure)
            {
                Log($"detail               : {cutout.Failure.TechnicalDetail}");
            }
        }
        finally
        {
            WriteTranscript(transcript.ToString());
            Console.WriteLine($"controlled workspace retained until Meitu releases the image: {root}");
        }
    }

    private const string EnableSeamVariable = "PRINTFLOW_MEITU_SMOKE_SEAM";

    private static WorkspaceDirRef ParentOf(WorkspaceFileRef file)
    {
        string path = file.RelativePath;
        int slash = path.LastIndexOf('/');
        return WorkspaceDirRef.Create(slash < 0 ? path : path[..slash]);
    }

    private static WorkspaceFileRef SiblingOf(WorkspaceFileRef file, string fileName)
    {
        string path = file.RelativePath;
        int slash = path.LastIndexOf('/');
        string directory = slash < 0 ? string.Empty : path[..(slash + 1)];
        return WorkspaceFileRef.Create(directory + fileName, file.Area);
    }

    /// <summary>
    /// The controlled output this attempt must produce, beside its own working copy.
    /// </summary>
    /// <remarks>
    /// Built from the preset's enhanced pattern applied to the working copy's stem, which is
    /// what <c>SessionService</c> does with the operator's output name. The smoke has no session
    /// to take a name from, so it derives one the same way rather than inventing a convention —
    /// the point of the exercise is that the file lands where PrintFlow's naming authority says,
    /// not merely somewhere PrintFlow chose.
    /// </remarks>
    private static WorkspaceFileRef OutputBesideWorkingCopy(WorkspaceFileRef workingCopy)
    {
        string relative = workingCopy.RelativePath;
        int slash = relative.LastIndexOf('/');
        string directory = slash < 0 ? string.Empty : relative[..(slash + 1)];
        string stem = Path.GetFileNameWithoutExtension(workingCopy.FileName);

        string name = OutputFileNaming.BuildProposedFileName(
            NamingArtifactKind.Enhanced, OutputName.Sanitise(stem), NamingPatternSet.DesignDefault);

        return WorkspaceFileRef.Create(directory + name, WorkspaceArea.Working);
    }

    private static string Describe(FileFacts facts) =>
        $"{facts.Format} {facts.PixelWidth}x{facts.PixelHeight}, {facts.ByteLength} bytes";

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

    private static void CleanUp(string root, string evidenceDirectory, bool mayStillBeLoaded)
    {
        if (Directory.Exists(evidenceDirectory))
        {
            Console.WriteLine(
                $"evidence captures in {evidenceDirectory}: " +
                $"{Directory.EnumerateFiles(evidenceDirectory).Count()} (deleted below, never committed)");

            Directory.Delete(evidenceDirectory, recursive: true);
        }

        if (mayStillBeLoaded)
        {
            Console.WriteLine(
                $"controlled workspace retained until Meitu releases the synthetic image: {root}");
            return;
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
