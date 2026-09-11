using System.Globalization;
using System.IO;
using System.Text;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
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
    private const string EnableBackgroundDiscoveryVariable = "PRINTFLOW_MEITU_SMOKE_BACKGROUND_DISCOVERY";
    private const string EnableBackgroundActionVariable = "PRINTFLOW_MEITU_SMOKE_BACKGROUND_ACTION";
    private const string EnableBackgroundRouteVariable = "PRINTFLOW_MEITU_SMOKE_BACKGROUND";
    private const string SyntheticSourceVariable = "PRINTFLOW_MEITU_SMOKE_SYNTHETIC_SOURCE";
    private const string RetainedWorkingFileVariable = "PRINTFLOW_MEITU_SMOKE_RETAINED_FILE";
    private const string PresetManifestVariable = "PRINTFLOW_MEITU_SMOKE_PRESET_MANIFEST";
    private const string PresetSha256Variable = "PRINTFLOW_MEITU_SMOKE_PRESET_SHA256";
    private const string EnableExportVariable = "PRINTFLOW_MEITU_SMOKE_EXPORT";
    private const string EnableCloseVariable = "PRINTFLOW_MEITU_SMOKE_CLOSE";

    /// <summary>
    /// Reads the Busy screen while an operation is genuinely in flight, so the actionable owner
    /// of the 取消 marker can be established structurally (Epic 11300 Part D2A §6).
    /// </summary>
    /// <remarks>
    /// Read-only throughout, and separate from every switch above precisely because it is: it
    /// invokes the operation being observed (which the enhance/background switches already
    /// gate) and then only <i>looks</i>. It never invokes the cancel it is discovering — §6
    /// forbids clicking until the structure is proven, and this run is what produces the proof.
    /// </remarks>
    private const string EnableCancelDiscoveryVariable = "PRINTFLOW_MEITU_SMOKE_CANCEL_DISCOVERY";

    /// <summary>
    /// Which operation the Busy-cancel discovery and the supervised Stop drive:
    /// <c>enhance</c> or <c>background</c>.
    /// </summary>
    private const string CancelOperationVariable = "PRINTFLOW_MEITU_SMOKE_CANCEL_OPERATION";

    /// <summary>
    /// Allows the supervised live Stop: one guarded invocation of the resolved, signed cancel
    /// control while the operation is positively Busy (Epic 11300 Part D2A §33).
    /// </summary>
    private const string EnableStopVariable = "PRINTFLOW_MEITU_SMOKE_STOP";

    /// <summary>
    /// Allows the supervised live Take Over: the operation is started and then abandoned with
    /// zero further PrintFlow input (Epic 11300 Part D2A §34).
    /// </summary>
    private const string EnableTakeOverVariable = "PRINTFLOW_MEITU_SMOKE_TAKEOVER";

    [Fact]
    public async Task Locate_identify_and_classify_the_workstation_Meitu()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            // Inert by design; see the class remarks.
            return;
        }

        await using WorkstationAutomationLeaseScope automationLease =
            await WorkstationAutomationLeaseScope.AcquireDefaultAsync();

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
            (string manifest, Sha256 expected) = PresetForSmoke(configuration);
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
                await foundation.OpenWorkingCopyAsync(workingCopy, InertAutomationStopSignal.Instance, CancellationToken.None);

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

            if (Environment.GetEnvironmentVariable(EnableBackgroundDiscoveryVariable) == "1")
            {
                Log(string.Empty);
                Log("## Phase C1 discovery — Background Removal candidates (read-only, no input)");
                DescribeBackgroundRemovalCandidates(opened.Value.Target, Log);
            }

            if (Environment.GetEnvironmentVariable(EnableBackgroundActionVariable) == "1")
            {
                Log(string.Empty);
                Log("## Phase C1 supervised discovery action — provisional structural target");
                await ExerciseBackgroundRemovalDiscoveryAsync(
                    opened.Value.Target, baseline.Value, workingCopy.FileName, Log);
            }

            if (Environment.GetEnvironmentVariable(EnableBackgroundRouteVariable) == "1")
            {
                Log(string.Empty);
                Log("## Phase C1 — guarded Background Removal (no export)");
                Log("mode authority       : reviewed synthetic content → 自动选择");
                OperationResult<MeituBackgroundRemovalOutcome> removed =
                    await foundation.RemoveBackgroundAsync(
                        opened.Value,
                        workingCopy,
                        BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
                        InertAutomationStopSignal.Instance,
                        CancellationToken.None);
                if (removed.IsFailure)
                {
                    Log($"RESULT               : BACKGROUND REMOVAL STOPPED — {removed.Failure.Code}");
                    Log($"detail               : {removed.Failure.TechnicalDetail}");
                    foreach (KeyValuePair<string, string> entry in removed.Failure.Context)
                    {
                        Log($"  {entry.Key,-20}: {entry.Value}");
                    }

                    Log("No export, AdapterOutput or Revision was produced.");
                    return;
                }

                Log($"identity before      : {removed.Value.IdentityBeforeAction.State} " +
                    $"({removed.Value.ObservedDocumentIdentity})");
                Log($"automatic mode       : {removed.Value.ObservedAutomaticModeName}");
                Log($"busy observed        : {removed.Value.Busy.State}");
                Log($"completion observed  : " +
                    $"{MeituBackgroundRemovalRule.Classify(baseline.Value.BackgroundRemoval!, removed.Value.Completion.Observation)} " +
                    "(positive signed controls; Busy absent)");
                Log($"identity after       : {removed.Value.IdentityAfterCompletion.State}");
                Log("STOP                 : no export, AdapterOutput or Revision");
                return;
            }

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
                opened.Value, workingCopy, InertAutomationStopSignal.Instance, CancellationToken.None);

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
                enhanced.Value, workingCopy, sourceFacts.Value, output,
                InertAutomationStopSignal.Instance, CancellationToken.None);

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
    /// Continues C1 discovery against a retained synthetic document when an earlier read-only
    /// open intentionally left Meitu in an editor state that the clean-start smoke will not
    /// reinterpret. Exact Save-surface identity is still the first input-producing step.
    /// </summary>
    [Fact]
    public async Task Discover_background_removal_on_a_retained_synthetic_document()
    {
        string? expectedWorkingCopyFileName =
            Environment.GetEnvironmentVariable(RetainedWorkingFileVariable);
        bool exerciseAction = Environment.GetEnvironmentVariable(EnableBackgroundActionVariable) == "1";
        bool inspectCompletion =
            Environment.GetEnvironmentVariable(EnableBackgroundDiscoveryVariable) == "1";
        if ((!exerciseAction && !inspectCompletion) ||
            string.IsNullOrWhiteSpace(expectedWorkingCopyFileName))
        {
            return;
        }

        await using WorkstationAutomationLeaseScope automationLease =
            await WorkstationAutomationLeaseScope.AcquireDefaultAsync();

        StringBuilder transcript = new();
        void Log(string line)
        {
            transcript.AppendLine(line);
            Console.WriteLine(line);
        }

        try
        {
            PrintFlowConfiguration configuration =
                PrintFlowConfiguration.LoadFromFile(RepositoryFile("appsettings.json"));
            (string manifest, Sha256 expected) = PresetForSmoke(configuration);
            PresetMeituBaselineProvider baselines = new(
                manifest, expected);
            OperationResult<MeituBaseline> baseline = baselines.GetVerifiedBaseline();
            if (baseline.IsFailure)
            {
                Log($"baseline             : REFUSED — {baseline.Failure.Code}");
                return;
            }

            Win32ExternalAppWindowLocator locator = new();
            OperationResult<IReadOnlyList<ExternalProcessRef>> processes =
                locator.FindProcessesByExecutable(baseline.Value.ExecutablePath);
            if (processes.IsFailure || processes.Value.Count != 1)
            {
                Log($"process              : REFUSED — {(processes.IsFailure ? processes.Failure.Code.ToString() : $"{processes.Value.Count} candidates")}");
                return;
            }

            ExternalProcessRef process = processes.Value[0];
            OperationResult<IReadOnlyList<ExternalWindowRef>> windows = locator.FindTopLevelWindows(process);
            if (windows.IsFailure)
            {
                Log($"window               : REFUSED — {windows.Failure.Code}");
                return;
            }

            ExternalWindowRef[] candidates =
            [
                .. windows.Value.Where(window =>
                    window.IsVisible && window.IsEnabled &&
                    !string.Equals(window.Title, baseline.Value.WelcomeWindowTitle, StringComparison.Ordinal) &&
                    baseline.Value.AcceptedWindowTitles.Contains(window.Title, StringComparer.Ordinal))
            ];
            if (candidates.Length != 1)
            {
                Log($"window               : REFUSED — {candidates.Length} accepted candidates");
                return;
            }

            Log($"retained expected file: {expectedWorkingCopyFileName}");
            Log($"process / window     : {process.ProcessId} / {candidates[0].Handle}");
            MeituTarget target = new(process, candidates[0]);
            if (exerciseAction)
            {
                await ExerciseBackgroundRemovalDiscoveryAsync(
                    target, baseline.Value, expectedWorkingCopyFileName, Log);
            }
            else
            {
                UiaElementProvider elements = new();
                GuardedMeituUiDriver driver = new(
                    locator, elements, new Win32ScopedInputSink(locator), new NullEvidenceSink(),
                    new FixedBaselineProvider(baseline.Value), new MeituAutomationOptions(), TimeProvider.System);

                OperationResult<MeituStateSnapshot> identity =
                    await driver.ConfirmWorkingCopyIdentityAsync(
                        target, expectedWorkingCopyFileName, CancellationToken.None);
                if (identity.IsFailure)
                {
                    Log($"post-completion identity: REFUSED — {identity.Failure.Code}");
                    Log($"detail               : {identity.Failure.TechnicalDetail}");
                    DescribeBackgroundRemovalCandidates(target, Log);

                    OperationResult<PrintFlow.Domain.Results.Unit> returned =
                        await InvokeProvisionalPageButtonAsync(
                            target, "调整", driver, locator, elements);
                    if (returned.IsFailure)
                    {
                        Log($"editor return       : REFUSED — {returned.Failure.Code}");
                        return;
                    }

                    Log("editor return       : 调整 invoked once through exact PageButton structure");
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                    identity = await driver.ConfirmWorkingCopyIdentityAsync(
                        target, expectedWorkingCopyFileName, CancellationToken.None);
                    if (identity.IsFailure)
                    {
                        Log($"identity after return: REFUSED — {identity.Failure.Code}");
                        return;
                    }
                }

                Log($"post-completion identity: {identity.Value.State} " +
                    $"({identity.Value.Observation.ObservedDocumentIdentity})");
                DescribeBackgroundRemovalCandidates(target, Log);

                if (Environment.GetEnvironmentVariable(EnableCloseVariable) == "1")
                {
                    OperationResult<MeituTarget> closed = await driver.CloseDocumentAsync(
                        target, CancellationToken.None);
                    if (closed.IsFailure)
                    {
                        Log($"close                : STOPPED — {closed.Failure.Code}");
                        Log($"detail               : {closed.Failure.TechnicalDetail}");
                        Log("Any modified-document prompt is intentionally left for the operator.");
                        return;
                    }

                    Log("close                : signed empty-editor state confirmed");
                }

                Log("STOP                 : no export, AdapterOutput or Revision");
            }
        }
        finally
        {
            WriteTranscript(transcript.ToString());
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
            $"Sessions/S_SMOKE/Working/A_1/PF_BACKGROUND_C1_{token}.png", WorkspaceArea.Working);

        IWorkspace workspace = new FileWorkspace(root);
        string absolute = workspace.ResolveAbsolute(reference);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);

        string? syntheticSource = Environment.GetEnvironmentVariable(SyntheticSourceVariable);
        if (string.IsNullOrWhiteSpace(syntheticSource))
        {
            File.WriteAllBytes(absolute, SyntheticImages.Png(320, 240, dpi: 300, alpha: true));
        }
        else
        {
            byte[] pixels = SyntheticImages.ReadBgra(syntheticSource, out int width, out int height);
            File.WriteAllBytes(absolute, SyntheticImages.OpaqueRgbPng(
                width,
                height,
                (x, y) =>
                {
                    int offset = ((y * width) + x) * 4;
                    return (pixels[offset + 2], pixels[offset + 1], pixels[offset]);
                }));
        }

        return (absolute, reference, workspace);
    }

    /// <summary>
    /// Records every live control associated with the candidate Background Removal terms and
    /// its first six control-view ancestors. This is discovery evidence only and invokes
    /// nothing; the production action must use a separately signed fixed-depth rule.
    /// </summary>
    private static void DescribeBackgroundRemovalCandidates(MeituTarget target, Action<string> log)
    {
        string[] terms =
        [
            "智能抠图", "抠图", "AI换背景", "自动选择", "局部抠图", "手动修补",
            "反选", "移除背景", "调整", "智能识别中", "返回结果中", "图片合成中", "取消"
        ];
        UiaElementProvider elements = new();
        OperationResult<IReadOnlyList<UiElementRef>> found = elements.FindAll(
            target.Window.Handle, new UiElementQuery(UiControlKind.Any));

        if (found.IsFailure)
        {
            log($"candidate walk       : REFUSED — {found.Failure.Code}");
            log($"detail               : {found.Failure.TechnicalDetail}");
            return;
        }

        List<UiElementRef> candidates = [];
        foreach (UiElementRef candidate in found.Value)
        {
            OperationResult<UiElementIdentity> identity = elements.Describe(candidate);
            if (identity.IsSuccess && terms.Any(term =>
                    identity.Value.Name.Contains(term, StringComparison.Ordinal)))
            {
                candidates.Add(candidate);
            }
        }

        log($"candidate markers      : {candidates.Count}");
        for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            UiElementRef current = candidates[candidateIndex];
            for (int depth = 0; depth <= 6; depth++)
            {
                OperationResult<UiElementIdentity> identity = elements.Describe(current);
                if (identity.IsFailure)
                {
                    log($"  [{candidateIndex}] depth {depth}: UNREADABLE — {identity.Failure.Code}");
                    break;
                }

                UiElementIdentity value = identity.Value;
                string displayedValue = "(unsupported)";
                if (value.Supports(UiPatternKind.Value))
                {
                    OperationResult<string> currentValue = elements.GetValue(current);
                    displayedValue = currentValue.IsSuccess
                        ? $"'{currentValue.Value}'"
                        : $"(unreadable: {currentValue.Failure.Code})";
                }
                log($"  [{candidateIndex}] depth {depth}: {value}; patterns=" +
                    $"{(value.SupportedPatterns.IsDefaultOrEmpty ? "(none)" : string.Join(", ", value.SupportedPatterns))}; " +
                    $"enabled={value.IsEnabled}; offscreen={value.IsOffscreen}; bounds={value.Bounds}; " +
                    $"value={displayedValue}");

                OperationResult<UiElementRef> parent = elements.GetParent(current);
                if (parent.IsFailure)
                {
                    break;
                }

                current = parent.Value;
            }
        }
    }

    /// <summary>
    /// Enters the live 抠图 feature once through a provisional exact structural control and then
    /// polls UIA read-only. This diagnostic exists to discover semantics and evidence before a
    /// final immutable preset is signed; it is never reachable unless its explicit smoke switch
    /// is set.
    /// </summary>
    private static async Task ExerciseBackgroundRemovalDiscoveryAsync(
        MeituTarget target,
        MeituBaseline baseline,
        string expectedWorkingCopyFileName,
        Action<string> log)
    {
        Win32ExternalAppWindowLocator locator = new();
        UiaElementProvider elements = new();
        GuardedMeituUiDriver driver = new(
            locator, elements, new Win32ScopedInputSink(locator), new NullEvidenceSink(),
            new FixedBaselineProvider(baseline), new MeituAutomationOptions(), TimeProvider.System);

        OperationResult<MeituStateSnapshot> identity = await driver.ConfirmWorkingCopyIdentityAsync(
            target, expectedWorkingCopyFileName, CancellationToken.None);
        if (identity.IsFailure)
        {
            log($"final identity        : REFUSED — {identity.Failure.Code}");
            log($"detail               : {identity.Failure.TechnicalDetail}");
            return;
        }

        string observedIdentity = identity.Value.Observation.ObservedDocumentIdentity ?? "(missing)";
        log($"final identity        : {identity.Value.State} ({observedIdentity})");

        OperationResult<MeituTarget> activated = await driver.ActivateAsync(target, CancellationToken.None);
        if (activated.IsFailure)
        {
            log($"editor reacquisition : REFUSED — {activated.Failure.Code}");
            return;
        }

        OperationResult<IReadOnlyList<ExternalWindowRef>> dialogs =
            locator.FindOwnedDialogs(activated.Value.Process, activated.Value.Window);
        if (dialogs.IsFailure || dialogs.Value.Count != 0)
        {
            log($"blocking modal       : {(dialogs.IsFailure ? dialogs.Failure.Code.ToString() : string.Join(" | ", dialogs.Value.Select(d => d.Title)))}");
            log("input sent           : false");
            return;
        }

        MeituControlSignature provisional = new(
            Name: "抠图",
            AutomationIdContains: string.Empty,
            ControlTypeName: "CheckBox",
            ClassName: "PageButton",
            RequiredPattern: UiPatternKind.Invoke);

        OperationResult<IReadOnlyList<UiElementRef>> found = elements.FindAll(
            activated.Value.Window.Handle, new UiElementQuery(UiControlKind.Any, Name: provisional.Name));
        if (found.IsFailure)
        {
            log($"provisional target   : REFUSED — {found.Failure.Code}");
            return;
        }

        List<UiElementRef> refs = [];
        List<UiElementIdentity> identities = [];
        foreach (UiElementRef candidate in found.Value)
        {
            OperationResult<UiElementIdentity> described = elements.Describe(candidate);
            if (described.IsSuccess)
            {
                refs.Add(candidate);
                identities.Add(described.Value);
            }
        }

        OperationResult<int> selected = MeituCardTargetRule.SelectSignedControl(
            provisional, activated.Value.Process.ProcessId, identities);
        if (selected.IsFailure)
        {
            log($"provisional target   : REFUSED — {selected.Failure.Code}");
            log($"detail               : {selected.Failure.TechnicalDetail}");
            return;
        }

        UiElementIdentity targetIdentity = identities[selected.Value];
        log($"provisional target   : {targetIdentity}");
        log($"patterns / state     : {string.Join(", ", targetIdentity.SupportedPatterns)}; " +
            $"enabled={targetIdentity.IsEnabled}; offscreen={targetIdentity.IsOffscreen}");

        OperationResult<ExternalWindowRef> refreshed = locator.Refresh(activated.Value.Window.Handle);
        OperationResult<ForegroundIdentity> foreground = locator.ReadForeground();
        if (refreshed.IsFailure || foreground.IsFailure ||
            foreground.Value.Handle != activated.Value.Window.Handle ||
            foreground.Value.ProcessId != activated.Value.Process.ProcessId)
        {
            log("foreground guard     : REFUSED");
            log("input sent           : false");
            return;
        }

        OperationResult<IReadOnlyList<string>> before =
            elements.ReadTextSnapshot(activated.Value.Window.Handle, 500);
        HashSet<string> beforeNames = before.IsSuccess
            ? [.. before.Value]
            : [];

        OperationResult<PrintFlow.Domain.Results.Unit> invoked = elements.Invoke(refs[selected.Value]);
        if (invoked.IsFailure)
        {
            log($"single invocation    : REFUSED — {invoked.Failure.Code}");
            return;
        }

        log("single invocation    : sent once");
        DateTimeOffset started = DateTimeOffset.UtcNow;
        string? lastReading = null;
        string[] relevantTerms =
        [
            "抠图", "背景", "人物", "人像", "商品", "物品", "通用", "自动", "手动",
            "智能", "选择", "处理中", "正在", "完成", "调整", "边缘", "涂抹", "擦除",
            "透明", "替换", "更换", "重置", "撤销", "保存"
        ];

        for (int sample = 0; sample < 200; sample++)
        {
            OperationResult<IReadOnlyList<string>> snapshot =
                elements.ReadTextSnapshot(activated.Value.Window.Handle, 500);
            OperationResult<IReadOnlyList<UiElementRef>> progress = elements.FindAll(
                activated.Value.Window.Handle, new UiElementQuery(UiControlKind.ProgressBar));

            if (snapshot.IsFailure)
            {
                log($"poll                 : REFUSED — {snapshot.Failure.Code}");
                break;
            }

            string[] relevant =
            [
                .. snapshot.Value
                    .Where(name => relevantTerms.Any(term => name.Contains(term, StringComparison.Ordinal)) ||
                        !beforeNames.Contains(name))
                    .Distinct(StringComparer.Ordinal)
            ];
            string reading = $"texts=[{string.Join(" | ", relevant)}]; progressBars=" +
                $"{(progress.IsSuccess ? progress.Value.Count : -1)}";
            if (!string.Equals(reading, lastReading, StringComparison.Ordinal))
            {
                double elapsed = (DateTimeOffset.UtcNow - started).TotalSeconds;
                log($"  t+{elapsed,5:0.00}s           : {reading}");
                lastReading = reading;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        log("observation stopped  : read-only polling ended; no second input, export or Revision");
        DescribeBackgroundRemovalCandidates(activated.Value, log);
    }

    private static async Task<OperationResult<PrintFlow.Domain.Results.Unit>>
        InvokeProvisionalPageButtonAsync(
            MeituTarget target,
            string exactName,
            GuardedMeituUiDriver driver,
            Win32ExternalAppWindowLocator locator,
            UiaElementProvider elements)
    {
        OperationResult<MeituTarget> activated = await driver.ActivateAsync(target, CancellationToken.None);
        if (activated.IsFailure)
        {
            return OperationResult.Fail<PrintFlow.Domain.Results.Unit>(activated.Failure);
        }

        OperationResult<IReadOnlyList<ExternalWindowRef>> dialogs =
            locator.FindOwnedDialogs(activated.Value.Process, activated.Value.Window);
        if (dialogs.IsFailure)
        {
            return OperationResult.Fail<PrintFlow.Domain.Results.Unit>(dialogs.Failure);
        }

        if (dialogs.Value.Count != 0)
        {
            return OperationResult.Fail<PrintFlow.Domain.Results.Unit>(
                OperationFailure.Create(
                    FailureCode.MeituBlockingDialog,
                    "A Meitu-owned modal blocks the provisional page-button action. Nothing was invoked.",
                    isRetryable: true));
        }

        MeituControlSignature provisional = new(
            exactName, string.Empty, "CheckBox", "PageButton", UiPatternKind.Invoke);
        OperationResult<IReadOnlyList<UiElementRef>> found = elements.FindAll(
            activated.Value.Window.Handle, new UiElementQuery(UiControlKind.Any, Name: exactName));
        if (found.IsFailure)
        {
            return OperationResult.Fail<PrintFlow.Domain.Results.Unit>(found.Failure);
        }

        List<UiElementRef> refs = [];
        List<UiElementIdentity> identities = [];
        foreach (UiElementRef candidate in found.Value)
        {
            OperationResult<UiElementIdentity> described = elements.Describe(candidate);
            if (described.IsSuccess)
            {
                refs.Add(candidate);
                identities.Add(described.Value);
            }
        }

        OperationResult<int> selected = MeituCardTargetRule.SelectSignedControl(
            provisional, activated.Value.Process.ProcessId, identities);
        if (selected.IsFailure)
        {
            return OperationResult.Fail<PrintFlow.Domain.Results.Unit>(selected.Failure);
        }

        OperationResult<ExternalWindowRef> refreshed = locator.Refresh(activated.Value.Window.Handle);
        if (refreshed.IsFailure)
        {
            return OperationResult.Fail<PrintFlow.Domain.Results.Unit>(refreshed.Failure);
        }

        OperationResult<ForegroundIdentity> foreground = locator.ReadForeground();
        if (foreground.IsFailure)
        {
            return OperationResult.Fail<PrintFlow.Domain.Results.Unit>(foreground.Failure);
        }

        if (foreground.Value.Handle != activated.Value.Window.Handle ||
            foreground.Value.ProcessId != activated.Value.Process.ProcessId)
        {
            return OperationResult.Fail<PrintFlow.Domain.Results.Unit>(
                OperationFailure.Create(
                    FailureCode.MeituTargetLost,
                    "The verified Meitu editor lost foreground before the provisional page-button action. " +
                    "Nothing was invoked.",
                    isRetryable: true,
                    context: new Dictionary<string, string> { ["inputSent"] = "false" }));
        }

        return elements.Invoke(refs[selected.Value]);
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

        await using WorkstationAutomationLeaseScope automationLease =
            await WorkstationAutomationLeaseScope.AcquireDefaultAsync();

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
            (string manifest, Sha256 expected) = PresetForSmoke(configuration);

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
                    workingCopy, MeituOperation.Enhance, BackgroundRemovalDecision.Unspecified,
                    ParentOf(workingCopy), output),
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
                    workingCopy, MeituOperation.RemoveBackground,
                    BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
                    ParentOf(workingCopy),
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
    private const string EnableBackgroundSeamVariable = "PRINTFLOW_MEITU_SMOKE_BACKGROUND_SEAM";

    /// <summary>
    /// C2A's real controlled-seam proof. It uses a fresh opaque RGB synthetic source, carries
    /// reviewed-content authority on the request, and reaches no Session, attempt or Revision.
    /// </summary>
    [Fact]
    public async Task Run_one_Background_Removal_through_the_production_adapter_seam()
    {
        if (Environment.GetEnvironmentVariable(EnableBackgroundSeamVariable) != "1" ||
            Environment.GetEnvironmentVariable(EnableExportVariable) != "1")
        {
            return;
        }

        await using WorkstationAutomationLeaseScope automationLease =
            await WorkstationAutomationLeaseScope.AcquireDefaultAsync();

        StringBuilder transcript = new();
        void Log(string line)
        {
            transcript.AppendLine(line);
            Console.WriteLine(line);
        }

        string root = Path.Combine(
            Path.GetTempPath(), "PrintFlowMeituCutoutSeam", Guid.NewGuid().ToString("N"));
        string evidenceDirectory = Path.Combine(root, "Evidence");
        bool mayStillBeLoaded = true;

        try
        {
            Log($"# Meitu Background Removal production-seam smoke — {DateTimeOffset.Now:O}");

            PrintFlowConfiguration configuration =
                PrintFlowConfiguration.LoadFromFile(RepositoryFile("appsettings.json"));
            (string manifest, Sha256 expected) = PresetForSmoke(configuration);
            (string workingPath, WorkspaceFileRef workingCopy, IWorkspace workspace) =
                PrepareOpaqueBackgroundRemovalWorkingCopy(root);
            WorkspaceFileRef output = SiblingOf(
                workingCopy,
                $"{Path.GetFileNameWithoutExtension(workingCopy.FileName)}_CUTOUT.png");
            string outputPath = workspace.ResolveAbsolute(output);

            WicFileInspector fileInspector = new();
            FileFacts sourceBefore = (await fileInspector.InspectAsync(
                workingPath, CancellationToken.None)).Value;
            string sourcePixelFormat = SyntheticImages.FormatOf(workingPath).ToString();

            Log($"working copy          : {workingCopy.RelativePath}");
            Log($"expected output       : {output.RelativePath}");
            Log($"decision              : {BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent}");
            Log($"source dimensions     : {sourceBefore.PixelWidth}x{sourceBefore.PixelHeight}");
            Log($"source pixel format   : {sourcePixelFormat}");
            Log($"source SHA-256        : {sourceBefore.Sha256}");

            IMeituProcessor adapter = MeituAutomationComposition.CreateProductionProcessor(
                manifest, expected, workspace, evidenceDirectory, TimeProvider.System);
            OperationResult<AdapterOutput> result = await adapter.ProcessAsync(
                new MeituRequest(
                    workingCopy,
                    MeituOperation.RemoveBackground,
                    BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
                    ParentOf(workingCopy),
                    output),
                CancellationToken.None);

            if (result.IsFailure)
            {
                Log($"RESULT                : REFUSED — {result.Failure.Code}");
                Log($"detail                : {result.Failure.TechnicalDetail}");
                foreach (KeyValuePair<string, string> entry in result.Failure.Context)
                {
                    Log($"  {entry.Key,-20}: {entry.Value}");
                }

                Log("No AdapterOutput and no Revision were produced.");
                return;
            }

            FileFacts outputFacts = (await fileInspector.InspectAsync(
                outputPath, CancellationToken.None)).Value;
            FileFacts sourceAfter = (await fileInspector.InspectAsync(
                workingPath, CancellationToken.None)).Value;
            MeituTransparencyFacts alpha = (await new WicMeituTransparencyInspector().InspectAsync(
                outputPath, CancellationToken.None)).Value;
            string outputPixelFormat = SyntheticImages.FormatOf(outputPath).ToString();

            Log("RESULT                : SUCCESS");
            Log($"AdapterOutput         : {result.Value.ProducedFile.RelativePath}");
            Log($"output dimensions     : {outputFacts.PixelWidth}x{outputFacts.PixelHeight}");
            Log($"output pixel format   : {outputPixelFormat}");
            Log($"HasAlpha              : {outputFacts.HasAlpha}");
            Log($"transparent pixels    : {alpha.TransparentPixelCount}/{alpha.PixelCount} " +
                $"({alpha.HasTransparentPixels})");
            Log($"visible pixels        : {alpha.VisiblePixelCount}/{alpha.PixelCount} " +
                $"({alpha.HasVisiblePixels})");
            Log($"file size             : {outputFacts.ByteLength}");
            Log($"output SHA-256        : {outputFacts.Sha256}");
            Log($"source unchanged      : {sourceBefore.Sha256.Equals(sourceAfter.Sha256)}");
            Log($"adapter notes         : {result.Value.AdapterNotes}");
            Log("No session, repository or workflow engine was composed; no Revision exists.");

            mayStillBeLoaded = result.Value.AdapterNotes?.Contains(
                "the editor was returned to its signed empty state", StringComparison.Ordinal) != true;
        }
        finally
        {
            WriteTranscript(transcript.ToString());
            CleanUp(root, evidenceDirectory, mayStillBeLoaded);
        }
    }

    /// <summary>
    /// Establishes, live and read-only, what the 取消 affordance actually is while an operation
    /// is Busy (Epic 11300 Part D2A §6, §7).
    /// </summary>
    /// <remarks>
    /// The observation cannot be taken any other way. The cancel affordance exists only while
    /// the operation is running, so a static walk of the editor never sees it — which is why
    /// this drives one real operation and then watches, rather than inspecting a screenshot or
    /// reasoning from the Enhancement evidence's Busy marker list.
    /// <para>
    /// It invokes nothing it discovers. §6 is explicit that the cancel must not be clicked until
    /// it is structurally proven, and the whole purpose of this run is to produce that proof; a
    /// discovery pass that also clicked would have proven nothing and cancelled something. The
    /// supervised Stop that does invoke it is a separate switch and a separate run (§33).
    /// </para>
    /// <para>
    /// It polls concurrently with the operation rather than after it. Both the Enhancement and
    /// the Background Removal routes block until completion, and by the time they return the
    /// affordance has already gone — the Enhancement evidence records 取消 disappearing
    /// together with the progress text. The concurrent read is <c>FindAll</c>/<c>Describe</c>
    /// only: it produces no input, so it cannot disturb the run it is watching.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Discover_the_busy_cancel_control_while_an_operation_is_running()
    {
        if (Environment.GetEnvironmentVariable(EnableCancelDiscoveryVariable) != "1")
        {
            return;
        }

        await using WorkstationAutomationLeaseScope automationLease =
            await WorkstationAutomationLeaseScope.AcquireDefaultAsync();

        StringBuilder transcript = new();
        void Log(string line)
        {
            transcript.AppendLine(line);
            Console.WriteLine(line);
        }

        string root = Path.Combine(Path.GetTempPath(), "PrintFlowMeituSmoke", Guid.NewGuid().ToString("N"));
        string evidenceDirectory = Path.Combine(root, "Evidence");
        bool mayStillBeLoaded = false;
        bool background = string.Equals(
            Environment.GetEnvironmentVariable(CancelOperationVariable), "background", StringComparison.OrdinalIgnoreCase);

        try
        {
            Log($"# Meitu Busy-cancel discovery — {DateTimeOffset.Now:O}");
            Log($"operation            : {(background ? "Background Removal (抠图)" : "Enhancement (AI变清晰)")}");
            Log("mode                 : READ-ONLY discovery. The cancel affordance is never invoked here.");

            PrintFlowConfiguration configuration =
                PrintFlowConfiguration.LoadFromFile(RepositoryFile("appsettings.json"));
            (string manifest, Sha256 expected) = PresetForSmoke(configuration);
            PresetMeituBaselineProvider baselines = new(manifest, expected);
            OperationResult<MeituBaseline> baseline = baselines.GetVerifiedBaseline();
            if (baseline.IsFailure)
            {
                Log($"baseline             : REFUSED — {baseline.Failure.Code}");
                return;
            }

            (string workingCopyPath, WorkspaceFileRef workingCopy, IWorkspace workspace) = background
                ? PrepareOpaqueBackgroundRemovalWorkingCopy(root)
                : PrepareSyntheticWorkingCopy(root);
            Log($"synthetic working copy: {workingCopyPath}");

            IMeituAutomationFoundation foundation = MeituAutomationComposition.CreateFoundation(
                manifest, expected, workspace, evidenceDirectory, TimeProvider.System);

            OperationResult<MeituReadiness> ready = await foundation.EnsureReadyAsync(CancellationToken.None);
            if (ready.IsFailure)
            {
                Log($"readiness            : STOPPED — {ready.Failure.Code}");
                Log($"detail               : {ready.Failure.TechnicalDetail}");
                return;
            }

            Log($"state                : {ready.Value.State.State}");

            Log(string.Empty);
            Log("## Busy observation (read-only, concurrent with the whole run)");
            Log("The watcher starts before the open, because Meitu keeps the selected module across");
            Log("document loads and can therefore begin work with no PrintFlow input at all — an");
            Log("auto-started run shows the same 取消 affordance and must be observed too.");

            using CancellationTokenSource watching = new();
            Task<int> watcher = Task.Run(
                () => WatchBusyCancelAsync(ready.Value.Target, Log, watching.Token), watching.Token);

            mayStillBeLoaded = true;
            OperationResult<MeituOpenedWorkingCopy> opened =
                await foundation.OpenWorkingCopyAsync(workingCopy, InertAutomationStopSignal.Instance, CancellationToken.None);

            OperationResult<PrintFlow.Domain.Results.Unit> ran;
            if (opened.IsFailure)
            {
                Log($"open                 : REFUSED — {opened.Failure.Code}");
                Log($"detail               : {opened.Failure.TechnicalDetail}");
                ran = OperationResult.Fail<PrintFlow.Domain.Results.Unit>(opened.Failure);
            }
            else
            {
                Log($"opened identity      : {opened.Value.State.Observation.ObservedDocumentIdentity}");
                ran = background
                    ? Discard(await foundation.RemoveBackgroundAsync(
                        opened.Value,
                        workingCopy,
                        BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
                        InertAutomationStopSignal.Instance,
                        CancellationToken.None))
                    : Discard(await foundation.EnhanceAsync(
                        opened.Value, workingCopy, InertAutomationStopSignal.Instance,
                        CancellationToken.None));
            }

            await watching.CancelAsync();
            int samples = await watcher;

            Log(string.Empty);
            Log($"operation outcome    : {(ran.IsSuccess ? "completed" : $"STOPPED — {ran.Failure.Code}")}");
            if (ran.IsFailure)
            {
                Log($"detail               : {ran.Failure.TechnicalDetail}");
            }

            Log($"busy samples with 取消: {samples}");
            Log("STOP                 : nothing was cancelled, exported or closed by this run.");
        }
        finally
        {
            WriteTranscript(transcript.ToString());
            CleanUp(root, evidenceDirectory, mayStillBeLoaded);
        }
    }

    /// <summary>
    /// Polls, read-only, for every element named exactly 取消 beneath the verified window and
    /// records its control-view ancestry (Epic 11300 Part D2A §6).
    /// </summary>
    /// <remarks>
    /// Exact-name matching rather than the substring sweep the C1 discovery used. The question
    /// here is narrower and the looser match would answer a different one: §8 forbids resolving
    /// a cancel by substring, so the discovery that will justify the signed rule matches the way
    /// the rule will.
    /// <para>
    /// Ancestry is recorded to depth 4, which is one level deeper than the ModuleButton walk
    /// needs, so the evidence shows what is <i>above</i> the actionable owner as well — the
    /// thing that distinguishes a cancel belonging to the operation's own progress panel from
    /// one belonging to some other surface.
    /// </para>
    /// </remarks>
    private static async Task<int> WatchBusyCancelAsync(
        MeituTarget target, Action<string> log, CancellationToken cancellationToken)
    {
        UiaElementProvider elements = new();
        HashSet<string> reported = new(StringComparer.Ordinal);
        int samples = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            OperationResult<IReadOnlyList<UiElementRef>> found = elements.FindAll(
                target.Window.Handle, new UiElementQuery(UiControlKind.Any, Name: "取消"));

            if (found.IsSuccess && found.Value.Count > 0)
            {
                samples++;
                for (int index = 0; index < found.Value.Count; index++)
                {
                    List<string> ancestry = [];
                    UiElementRef current = found.Value[index];
                    for (int depth = 0; depth <= 4; depth++)
                    {
                        OperationResult<UiElementIdentity> identity = elements.Describe(current);
                        if (identity.IsFailure)
                        {
                            ancestry.Add($"depth {depth}: UNREADABLE — {identity.Failure.Code}");
                            break;
                        }

                        UiElementIdentity value = identity.Value;
                        ancestry.Add(
                            $"depth {depth}: {value}; patterns=" +
                            $"{(value.SupportedPatterns.IsDefaultOrEmpty ? "(none)" : string.Join(", ", value.SupportedPatterns))}; " +
                            $"enabled={value.IsEnabled}; offscreen={value.IsOffscreen}; bounds={value.Bounds}");

                        OperationResult<UiElementRef> parent = elements.GetParent(current);
                        if (parent.IsFailure)
                        {
                            break;
                        }

                        current = parent.Value;
                    }

                    string key = string.Join("\n", ancestry);
                    if (reported.Add(key))
                    {
                        log($"  cancel candidate [{index}] — first seen at sample {samples}");
                        foreach (string line in ancestry)
                        {
                            log($"    {line}");
                        }
                    }
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return samples;
    }

    /// <summary>
    /// The supervised live Stop and the supervised live Take Over
    /// (Epic 11300 Part D2A §33, §34).
    /// </summary>
    /// <remarks>
    /// One method for both because the arrangement is identical and only the requested mode
    /// differs — which is itself the thing being demonstrated: the operator's two controls reach
    /// the same seam and the difference in what happens is entirely the policy's doing.
    /// <list type="bullet">
    ///   <item><b>Stop</b> (§33): the operation is driven to positively observed Busy, one
    ///   guarded invocation of the exact signed cancel is produced, and Busy is watched for
    ///   exit. No export, no Revision, and Meitu is never killed.</item>
    ///   <item><b>Take Over</b> (§34): the operation is started and then abandoned with
    ///   <i>zero</i> further PrintFlow input. Meitu is left running and the operator owns it;
    ///   the smoke does not require anyone to finish the image.</item>
    /// </list>
    /// <para>
    /// The stop is requested through the real <see cref="AutomationRunRegistry"/> rather than by
    /// setting a flag directly, so what is exercised is the production channel an operator's
    /// button press actually takes. It is requested from a watcher task once the adapter reports
    /// Busy, which is how it arrives while the run is in flight rather than before it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Supervised_live_stop_or_take_over_of_a_running_operation()
    {
        bool stop = Environment.GetEnvironmentVariable(EnableStopVariable) == "1";
        bool takeOver = Environment.GetEnvironmentVariable(EnableTakeOverVariable) == "1";
        if (!stop && !takeOver)
        {
            return;
        }

        await using WorkstationAutomationLeaseScope automationLease =
            await WorkstationAutomationLeaseScope.AcquireDefaultAsync();

        AutomationStopMode mode = stop ? AutomationStopMode.StopOperation : AutomationStopMode.TakeOver;
        bool background = string.Equals(
            Environment.GetEnvironmentVariable(CancelOperationVariable), "background",
            StringComparison.OrdinalIgnoreCase);

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
            Log($"# Meitu supervised {mode} smoke — {DateTimeOffset.Now:O}");
            Log($"operation            : {(background ? "Background Removal (抠图)" : "Enhancement (AI变清晰)")}");
            Log($"mode                 : {mode}");
            Log(mode == AutomationStopMode.TakeOver
                ? "expectation          : ZERO further PrintFlow input after the request. Nothing cancelled."
                : "expectation          : exactly ONE guarded invocation of the signed cancel. Nothing killed.");

            PrintFlowConfiguration configuration =
                PrintFlowConfiguration.LoadFromFile(RepositoryFile("appsettings.json"));
            (string manifest, Sha256 expected) = PresetForSmoke(configuration);
            PresetMeituBaselineProvider baselines = new(manifest, expected);
            OperationResult<MeituBaseline> baseline = baselines.GetVerifiedBaseline();
            if (baseline.IsFailure)
            {
                Log($"baseline             : REFUSED — {baseline.Failure.Code}");
                return;
            }

            Log($"signed cancel        : {(baseline.Value.BusyCancel is { } signed ? $"{signed.Control.ControlTypeName}/{signed.Control.ClassName} id~'{signed.Control.AutomationIdContains}' name='{signed.Control.Name}'" : "NOT SIGNED")}");
            if (baseline.Value.BusyCancel is null && stop)
            {
                Log("STOP                 : no signed cancel evidence, so a live Stop would invoke nothing.");
                return;
            }

            (string workingCopyPath, WorkspaceFileRef workingCopy, IWorkspace workspace) = background
                ? PrepareOpaqueBackgroundRemovalWorkingCopy(root)
                : PrepareSyntheticWorkingCopy(root);
            Log($"synthetic working copy: {workingCopyPath}");

            IMeituAutomationFoundation foundation = MeituAutomationComposition.CreateFoundation(
                manifest, expected, workspace, evidenceDirectory, TimeProvider.System);

            OperationResult<MeituReadiness> ready = await foundation.EnsureReadyAsync(CancellationToken.None);
            if (ready.IsFailure)
            {
                Log($"readiness            : STOPPED — {ready.Failure.Code}");
                Log($"detail               : {ready.Failure.TechnicalDetail}");
                return;
            }

            Log($"state                : {ready.Value.State.State}");

            // The real production channel: the registry the SessionService owns, the signal the
            // adapter reads, and RequestStop as the only way in.
            AutomationRunRegistry runs = new();
            SessionId session = SessionId.From(Guid.CreateVersion7());
            IAutomationStopSignal signal = runs.Begin(
                session,
                AttemptId.From(Guid.CreateVersion7()),
                background ? StepKind.BackgroundRemoval : StepKind.Enhancement,
                drivesExternalApplication: true);

            mayStillBeLoaded = true;
            OperationResult<MeituOpenedWorkingCopy> opened =
                await foundation.OpenWorkingCopyAsync(workingCopy, signal, CancellationToken.None);
            if (opened.IsFailure)
            {
                Log($"open                 : REFUSED — {opened.Failure.Code}");
                Log($"detail               : {opened.Failure.TechnicalDetail}");
                return;
            }

            Log($"opened identity      : {opened.Value.State.Observation.ObservedDocumentIdentity}");
            Log(string.Empty);
            Log("## The request, made once the operation is positively Busy");

            using CancellationTokenSource watching = new();
            Task requester = Task.Run(
                async () =>
                {
                    while (!watching.IsCancellationRequested)
                    {
                        if (signal.Phase == ExternalOperationPhase.Busy)
                        {
                            OperationResult<PrintFlow.Domain.Results.Unit> requested =
                                runs.RequestStop(session, mode);
                            Log($"request              : {(requested.IsSuccess ? "accepted" : $"REFUSED — {requested.Failure.TechnicalDetail}")}");
                            return;
                        }

                        try
                        {
                            // Tight, because the shorter of the two operations has a Busy window
                            // of roughly a second and a half and the request has to land inside
                            // it. A slower poll does not produce a wrong answer — PrintFlow
                            // refuses when it can no longer establish what is running — but it
                            // produces a refusal that says nothing about whether Stop works.
                            await Task.Delay(TimeSpan.FromMilliseconds(25), watching.Token)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                    }
                },
                watching.Token);

            OperationResult<PrintFlow.Domain.Results.Unit> ran = background
                ? Discard(await foundation.RemoveBackgroundAsync(
                    opened.Value,
                    workingCopy,
                    BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
                    signal,
                    CancellationToken.None))
                : Discard(await foundation.EnhanceAsync(
                    opened.Value, workingCopy, signal, CancellationToken.None));

            await watching.CancelAsync();
            await requester;

            Log(string.Empty);
            Log($"phase reached        : {signal.Phase}");
            Log($"signed cancel invoked: {(signal.OperationCancelWasInvoked ? "YES — exactly once" : "no")}");
            Log($"left Busy after cancel: {(signal.OperationLeftBusyAfterCancel ? "YES" : "no")}");
            Log($"outcome              : {(ran.IsSuccess ? "the operation completed before the request landed" : ran.Failure.Code.ToString())}");
            if (ran.IsFailure)
            {
                Log($"detail               : {ran.Failure.TechnicalDetail}");
                foreach (KeyValuePair<string, string> entry in ran.Failure.Context)
                {
                    Log($"  {entry.Key,-24}: {entry.Value}");
                }
            }

            Log($"retained external    : {AutomationStopPolicy.RetainedFor(signal.Phase, signal.OperationLeftBusyAfterCancel)}");
            Log("STOP                 : no export, no AdapterOutput, no Revision, no process termination.");
            Log("Meitu was left for the operator; PrintFlow closed nothing and killed nothing.");
        }
        finally
        {
            WriteTranscript(transcript.ToString());
            CleanUp(root, evidenceDirectory, mayStillBeLoaded);
        }
    }

    /// <summary>Erases an outcome's payload, keeping only whether it succeeded and why not.</summary>
    private static OperationResult<PrintFlow.Domain.Results.Unit> Discard<T>(OperationResult<T> result) =>
        result.IsSuccess
            ? OperationResult.Ok()
            : OperationResult.Fail<PrintFlow.Domain.Results.Unit>(result.Failure);

    private static WorkspaceDirRef ParentOf(WorkspaceFileRef file)
    {
        string path = file.RelativePath;
        int slash = path.LastIndexOf('/');
        return WorkspaceDirRef.Create(slash < 0 ? path : path[..slash]);
    }

    private static (string Manifest, Sha256 Expected) PresetForSmoke(PrintFlowConfiguration configuration)
    {
        string manifest = Environment.GetEnvironmentVariable(PresetManifestVariable) ??
            Path.Combine(configuration.Workspace.Root, configuration.Preset.Path);
        string digest = Environment.GetEnvironmentVariable(PresetSha256Variable) ??
            configuration.Preset.ExpectedSha256;
        return (manifest, Sha256.Parse(digest));
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

        OperationResult<string> name = OutputFileNaming.BuildProposedFileName(
            NamingArtifactKind.Enhanced, OutputName.Sanitise(stem), NamingPatternSet.DesignDefault);
        name.IsSuccess.ShouldBeTrue(name.IsFailure ? name.Failure.ToString() : string.Empty);

        return WorkspaceFileRef.Create(directory + name.Value, WorkspaceArea.Working);
    }

    private static (string AbsolutePath, WorkspaceFileRef Reference, IWorkspace Workspace)
        PrepareOpaqueBackgroundRemovalWorkingCopy(string root)
    {
        string token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        WorkspaceFileRef reference = WorkspaceFileRef.Create(
            $"Sessions/S_SMOKE/Working/A_1/PF_BACKGROUND_C2A_{token}.png",
            WorkspaceArea.Working);
        IWorkspace workspace = new FileWorkspace(root);
        string absolute = workspace.ResolveAbsolute(reference);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);

        File.WriteAllBytes(
            absolute,
            SyntheticImages.OpaqueRgbPng(
                480,
                360,
                (x, y) =>
                {
                    bool foreground = x is > 140 and < 340 && y is > 55 and < 330;
                    return foreground
                        ? ((byte)30, (byte)75, (byte)175)
                        : ((byte)240, (byte)240, (byte)240);
                }));

        return (absolute, reference, workspace);
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
