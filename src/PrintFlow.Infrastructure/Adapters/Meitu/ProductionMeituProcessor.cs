using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// The production Meitu adapter (Epic 11300 Part A §5).
/// </summary>
/// <remarks>
/// This slice implements the <i>foundation</i> only: identify or launch the accepted Meitu,
/// confirm a recognised safe state, and hand over a PrintFlow-created working copy. Enhancement
/// and background removal are Part B.
///
/// <see cref="ProcessAsync"/> — the workflow seam — therefore always fails, and that is the
/// design rather than an omission. Returning a success from it would create a Revision on the
/// strength of having opened a file, which §24 forbids in the plainest terms: "opened
/// successfully" is not "processing succeeded". The foundation is reached through
/// <see cref="IMeituAutomationFoundation"/> instead, which no workflow code can see.
///
/// <see cref="Mode"/> is <see cref="AdapterExecutionMode.Production"/>, so
/// <c>IEnvironmentGate</c> remains authoritative over every step this adapter would back. The
/// adapter neither consults nor bypasses the gate; it simply declares what it is and lets
/// <c>SessionService</c> apply the gate before calling (§22).
/// </remarks>
public sealed class ProductionMeituProcessor : IMeituProcessor, IMeituAutomationFoundation
{
    private readonly IMeituBaselineProvider _baselines;
    private readonly IExternalAppWindowLocator _locator;
    private readonly IMeituUiDriver _driver;
    private readonly IWorkspace _workspace;
    private readonly MeituAutomationOptions _options;
    private readonly TimeProvider _clock;

    public ProductionMeituProcessor(
        IMeituBaselineProvider baselines,
        IExternalAppWindowLocator locator,
        IMeituUiDriver driver,
        IWorkspace workspace,
        MeituAutomationOptions options,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(baselines);
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        _baselines = baselines;
        _locator = locator;
        _driver = driver;
        _workspace = workspace;
        _options = options;
        _clock = clock;
    }

    /// <inheritdoc />
    public string AdapterId => "meitu-xiuxiu-production-v1";

    /// <inheritdoc />
    public AdapterExecutionMode Mode => AdapterExecutionMode.Production;

    /// <summary>
    /// Always fails: no Meitu operation is automated in Part A.
    /// </summary>
    /// <remarks>
    /// Written as an unconditional refusal rather than left unimplemented so that wiring this
    /// adapter into a workflow — deliberately or by accident — produces a structured, logged,
    /// operator-readable failure instead of a <c>NotImplementedException</c> in front of
    /// someone trying to work.
    /// </remarks>
    public Task<OperationResult<AdapterOutput>> ProcessAsync(
        MeituRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(OperationResult.Fail<AdapterOutput>(OperationFailure.Create(
            FailureCode.AdapterUnavailable,
            $"The production Meitu adapter implements the Epic 11300 Part A safety foundation only; " +
            $"'{request.Operation}' automation is Part B. No file was produced and no Revision may be created.",
            isRetryable: false,
            context: new Dictionary<string, string>
            {
                ["adapterId"] = AdapterId,
                ["operation"] = request.Operation.ToString(),
                ["implementedScope"] = "launch, identify, safe-state check, open working copy",
            })));
    }

    /// <inheritdoc />
    public async Task<OperationResult<MeituReadiness>> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        OperationResult<MeituBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<MeituReadiness>(baseline.Failure);
        }

        OperationResult<Unit> identity = VerifyExecutableIdentity(baseline.Value);
        if (identity.IsFailure)
        {
            return OperationResult.Fail<MeituReadiness>(identity.Failure);
        }

        OperationResult<IReadOnlyList<ExternalProcessRef>> running =
            _locator.FindProcessesByExecutable(baseline.Value.ExecutablePath);
        if (running.IsFailure)
        {
            return OperationResult.Fail<MeituReadiness>(running.Failure);
        }

        if (running.Value.Count > 1)
        {
            // Which of several instances is "the" Meitu is not a question PrintFlow may answer
            // by picking one. The operator resolves it (§15).
            return OperationResult.Fail<MeituReadiness>(
                FailureCode.MeituUnknownState,
                $"{running.Value.Count} processes are running from '{baseline.Value.ExecutablePath}'. " +
                "PrintFlow will not choose between them.");
        }

        return running.Value.Count == 1
            ? await AttachAsync(running.Value[0], cancellationToken).ConfigureAwait(false)
            : await LaunchAsync(baseline.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<MeituOpenedWorkingCopy>> OpenWorkingCopyAsync(
        WorkspaceFileRef workingCopy, CancellationToken cancellationToken)
    {
        // The working-copy boundary, checked before anything is resolved to a path and long
        // before Meitu is asked to open anything. A Source, Approved or Rejected reference is
        // refused here, so there is no route by which the customer's original could be handed
        // to an external application (§16).
        if (workingCopy.Area != WorkspaceArea.Working)
        {
            return OperationResult.Fail<MeituOpenedWorkingCopy>(
                FailureCode.PreconditionNotMet,
                $"Meitu may only be given a Working copy; '{workingCopy.RelativePath}' is in " +
                $"{workingCopy.Area}. The customer original, the source snapshot and approved " +
                "outputs are never opened by an external application.");
        }

        OperationResult<MeituBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<MeituOpenedWorkingCopy>(baseline.Failure);
        }

        OperationResult<MeituReadiness> ready = await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            return OperationResult.Fail<MeituOpenedWorkingCopy>(ready.Failure);
        }

        string absolutePath = _workspace.ResolveAbsolute(workingCopy);
        if (!File.Exists(absolutePath))
        {
            return OperationResult.Fail<MeituOpenedWorkingCopy>(
                FailureCode.OutputMissing,
                $"The working copy '{workingCopy.RelativePath}' does not exist on disk.");
        }

        OperationResult<MeituTarget> activated =
            await _driver.ActivateAsync(ready.Value.Target, cancellationToken).ConfigureAwait(false);
        if (activated.IsFailure)
        {
            return Capture<MeituOpenedWorkingCopy>(ready.Value.Target, activated.Failure, "activate-failed");
        }

        OperationResult<MeituTarget> opened = await _driver
            .OpenWorkingCopyAsync(activated.Value, absolutePath, cancellationToken)
            .ConfigureAwait(false);
        if (opened.IsFailure)
        {
            return Capture<MeituOpenedWorkingCopy>(activated.Value, opened.Failure, "open-failed");
        }

        // Confirmation is against the window the driver ended on, not the one it started from.
        // Meitu's editor is a separate top-level window (Part B1 §7), so polling the start page
        // would look at a screen the file was never going to appear on and time out for a reason
        // that has nothing to do with whether the open worked.
        //
        // A dialog that closed is not proof the file loaded either. Confirmation is a positive
        // observation of the expected name on the signed editor screen (MVP design §11.5, §13).
        // Whether confirmation is even possible is decided before waiting for it. If the
        // verified chain carries no signature for "the editor is showing the document PrintFlow
        // handed over", polling for that state is polling for something unreachable, and
        // 30 seconds of it would report a timeout as though the open had been slow rather than
        // as what it is: PrintFlow has no signed way to tell which document is loaded (§10, §14).
        if (baseline.Value.EditorWithWorkingCopy is null)
        {
            return Capture<MeituOpenedWorkingCopy>(
                opened.Value,
                OperationFailure.Create(
                    FailureCode.MeituUnknownState,
                    $"'{workingCopy.FileName}' was handed to Meitu, but the verified evidence chain carries no " +
                    "signature that identifies which document the editor is showing, so PrintFlow cannot " +
                    "confirm the right file is open and will not claim that it is.",
                    isRetryable: false,
                    context: new Dictionary<string, string>
                    {
                        ["expectedFile"] = workingCopy.FileName,
                        ["windowTitle"] = opened.Value.Window.Title,
                        ["missingEvidence"] = "editor-with-working-copy",
                    }),
                "open-unconfirmable");
        }

        OperationResult<MeituStateSnapshot> confirmed = await ConfirmStateAsync(
            opened.Value,
            workingCopy.FileName,
            _options.OpenConfirmationTimeout,
            state => state.State == MeituStartingState.KnownEditorWithExpectedWorkingCopy,
            cancellationToken).ConfigureAwait(false);

        if (confirmed.IsFailure)
        {
            return Capture<MeituOpenedWorkingCopy>(opened.Value, confirmed.Failure, "open-unconfirmed");
        }

        return OperationResult.Ok(new MeituOpenedWorkingCopy(opened.Value, confirmed.Value));
    }

    /// <summary>Reuses an already-running instance, inspecting it exactly once (§15).</summary>
    private async Task<OperationResult<MeituReadiness>> AttachAsync(
        ExternalProcessRef process, CancellationToken cancellationToken)
    {
        OperationResult<MeituTarget> target =
            await WaitForWindowAsync(process, _options.AttachTimeout, cancellationToken).ConfigureAwait(false);
        if (target.IsFailure)
        {
            return OperationResult.Fail<MeituReadiness>(target.Failure);
        }

        OperationResult<MeituStateSnapshot> state = await _driver
            .InspectStateAsync(target.Value, expectedWorkingCopyFileName: null, cancellationToken)
            .ConfigureAwait(false);
        if (state.IsFailure)
        {
            return OperationResult.Fail<MeituReadiness>(state.Failure);
        }

        // No polling and no nudging here. An operator's Meitu that is mid-edit will not become
        // safe by being watched, and it must not be closed to make it so — the operator decides.
        if (!state.Value.IsSafeStartingState)
        {
            return Capture<MeituReadiness>(
                target.Value, UnsafeState(state.Value, launched: false), ReasonFor(state.Value.State));
        }

        return OperationResult.Ok(new MeituReadiness(target.Value, state.Value, WasLaunched: false));
    }

    /// <summary>Starts the accepted executable and waits for a recognised safe state (§14).</summary>
    private async Task<OperationResult<MeituReadiness>> LaunchAsync(
        MeituBaseline baseline, CancellationToken cancellationToken)
    {
        OperationResult<ExternalProcessRef> started = _locator.Launch(baseline.ExecutablePath);
        if (started.IsFailure)
        {
            return OperationResult.Fail<MeituReadiness>(started.Failure);
        }

        OperationResult<MeituTarget> target = await WaitForWindowAsync(
            started.Value, _options.LaunchTimeout, cancellationToken).ConfigureAwait(false);
        if (target.IsFailure)
        {
            return OperationResult.Fail<MeituReadiness>(target.Failure);
        }

        // Three separate conditions, exactly as §14 requires: the process exists (checked while
        // waiting), a top-level window belongs to it (above), and the state is recognised and
        // safe (below). A fixed sleep satisfies none of them and is used for none of them.
        OperationResult<MeituStateSnapshot> state = await PollForStateAsync(
            target.Value,
            expectedWorkingCopyFileName: null,
            _options.LaunchTimeout,
            snapshot => snapshot.IsSafeStartingState || IsTerminal(snapshot.State),
            cancellationToken).ConfigureAwait(false);

        if (state.IsFailure)
        {
            return OperationResult.Fail<MeituReadiness>(state.Failure);
        }

        if (!state.Value.IsSafeStartingState)
        {
            return Capture<MeituReadiness>(
                target.Value, UnsafeState(state.Value, launched: true), ReasonFor(state.Value.State));
        }

        return OperationResult.Ok(new MeituReadiness(target.Value, state.Value, WasLaunched: true));
    }

    /// <summary>Polls for a top-level window belonging to <paramref name="process"/>.</summary>
    private async Task<OperationResult<MeituTarget>> WaitForWindowAsync(
        ExternalProcessRef process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + timeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_locator.IsAlive(process))
            {
                return OperationResult.Fail<MeituTarget>(
                    FailureCode.MeituLaunchFailed,
                    $"Meitu process {process.ProcessId} exited before presenting a window.");
            }

            OperationResult<IReadOnlyList<ExternalWindowRef>> windows = _locator.FindTopLevelWindows(process);
            if (windows.IsSuccess && windows.Value.Count > 0)
            {
                return OperationResult.Ok(new MeituTarget(process, windows.Value[0]));
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Fail<MeituTarget>(
                    FailureCode.MeituWindowNotFound,
                    $"No top-level window belonging to Meitu process {process.ProcessId} appeared within " +
                    $"{timeout.TotalSeconds:0} s.");
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Polls until <paramref name="isDecided"/> holds, and <b>fails</b> if it never does.
    /// </summary>
    /// <remarks>
    /// The difference from <see cref="PollForStateAsync"/> is the whole point of this method
    /// existing. That one hands back the last state it saw when the timeout expires, which is
    /// right for the launch path — it evaluates safety itself afterwards and wants the state to
    /// report. Used for confirmation it is a hole: an open that never produced the expected
    /// screen came back as a success carrying <c>Unknown</c>, which is precisely the
    /// "opened successfully means processing succeeded" conflation §13 forbids.
    ///
    /// Two methods with names that say which is which, rather than one with a flag, because the
    /// flag is the thing that gets forgotten.
    /// </remarks>
    private async Task<OperationResult<MeituStateSnapshot>> ConfirmStateAsync(
        MeituTarget target,
        string? expectedWorkingCopyFileName,
        TimeSpan timeout,
        Func<MeituStateSnapshot, bool> isDecided,
        CancellationToken cancellationToken)
    {
        OperationResult<MeituStateSnapshot> polled = await PollForStateAsync(
            target, expectedWorkingCopyFileName, timeout, isDecided, cancellationToken).ConfigureAwait(false);

        if (polled.IsFailure)
        {
            return polled;
        }

        return isDecided(polled.Value)
            ? polled
            : OperationResult.Fail<MeituStateSnapshot>(OperationFailure.Create(
                FailureCode.MeituUnknownState,
                $"Meitu did not reach the expected state within {timeout.TotalSeconds:0} s; it is on " +
                $"'{polled.Value.State}'. The file was handed over but PrintFlow has not seen it loaded, so " +
                "nothing is claimed about it.",
                isRetryable: true,
                context: new Dictionary<string, string>
                {
                    ["lastState"] = polled.Value.State.ToString(),
                    ["windowTitle"] = polled.Value.Observation.WindowTitle,
                    ["expectedFile"] = expectedWorkingCopyFileName ?? "(none)",
                }));
    }

    /// <summary>
    /// Polls the classified state until <paramref name="isDecided"/> or the timeout, returning
    /// the last state seen either way.
    /// </summary>
    /// <remarks>
    /// A timeout here is a success carrying an undecided state, so every caller must inspect
    /// what it got back. Callers that need "decided or nothing" should use
    /// <see cref="ConfirmStateAsync"/> instead.
    /// </remarks>
    private async Task<OperationResult<MeituStateSnapshot>> PollForStateAsync(
        MeituTarget target,
        string? expectedWorkingCopyFileName,
        TimeSpan timeout,
        Func<MeituStateSnapshot, bool> isDecided,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + timeout;
        MeituStateSnapshot? last = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResult<MeituStateSnapshot> snapshot = await _driver
                .InspectStateAsync(target, expectedWorkingCopyFileName, cancellationToken)
                .ConfigureAwait(false);
            if (snapshot.IsFailure)
            {
                return snapshot;
            }

            last = snapshot.Value;
            if (isDecided(snapshot.Value))
            {
                return OperationResult.Ok(snapshot.Value);
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return OperationResult.Ok(last);
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Confirms the accepted executable is present and is the accepted binary (§7).
    /// </summary>
    /// <remarks>
    /// A path match alone would accept any binary that happened to be sitting at the accepted
    /// location. Hashing is what makes "this is the Meitu Epic 11000 signed off" a checked fact
    /// rather than an assumption about a directory name.
    /// </remarks>
    private static OperationResult<Unit> VerifyExecutableIdentity(MeituBaseline baseline)
    {
        if (!File.Exists(baseline.ExecutablePath))
        {
            return OperationResult.Fail<Unit>(
                FailureCode.MeituNotInstalled,
                $"The accepted Meitu executable is not present at '{baseline.ExecutablePath}'.");
        }

        Sha256 actual;
        try
        {
            using FileStream stream = new(
                baseline.ExecutablePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1 << 20, useAsync: false);
            actual = Sha256.FromBytes(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.MeituNotInstalled,
                $"The accepted Meitu executable could not be read: {ex.Message}");
        }

        return actual.Equals(baseline.ExecutableSha256)
            ? OperationResult.Ok()
            : OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.MeituNotInstalled,
                $"The binary at '{baseline.ExecutablePath}' hashes to {actual}, not the accepted " +
                $"{baseline.ExecutableSha256}. A Meitu upgrade requires preset revalidation before automation resumes.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["expectedSha256"] = baseline.ExecutableSha256.ToString(),
                    ["actualSha256"] = actual.ToString(),
                }));
    }

    /// <summary>Attaches a window capture to a failure, without letting the capture replace it.</summary>
    private OperationResult<T> Capture<T>(MeituTarget target, OperationFailure failure, string reason)
    {
        OperationResult<EvidenceRef> evidence = _driver.CaptureEvidence(target, reason);
        if (evidence.IsFailure)
        {
            // The original failure is what the operator needs; a failed screenshot must not
            // become the reported problem.
            return OperationResult.Fail<T>(failure);
        }

        Dictionary<string, string> context = new(failure.Context, StringComparer.Ordinal)
        {
            ["evidencePath"] = evidence.Value.AbsolutePath,
        };

        return OperationResult.Fail<T>(failure with { Context = context });
    }

    private static OperationFailure UnsafeState(MeituStateSnapshot snapshot, bool launched)
    {
        Dictionary<string, string> context = new(StringComparer.Ordinal)
        {
            ["state"] = snapshot.State.ToString(),
            ["windowTitle"] = snapshot.Observation.WindowTitle,
            ["matchedMarkers"] = string.Join(", ", snapshot.MatchedMarkers),
            ["launchedByPrintFlow"] = launched ? "true" : "false",

            // How much the automation tree yielded, as a count only. It separates "PrintFlow
            // could not read the window" from "PrintFlow read it and did not recognise it",
            // which are different problems with different fixes — and a count says so without
            // writing any of the window's actual text, which could name a customer's file
            // (§20, §31).
            ["visibleTextCount"] = snapshot.Observation.VisibleTexts.IsDefaultOrEmpty
                ? "0"
                : snapshot.Observation.VisibleTexts.Length.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
        };

        if (!snapshot.Observation.OwnedDialogTitles.IsDefaultOrEmpty)
        {
            context["dialogTitles"] = string.Join(" | ", snapshot.Observation.OwnedDialogTitles);
        }

        return snapshot.State switch
        {
            MeituStartingState.KnownModal => OperationFailure.Create(
                FailureCode.MeituBlockingDialog,
                "A dialog owned by Meitu is blocking its window. PrintFlow does not dismiss dialogs it " +
                "cannot identify; the operator must resolve it.",
                isRetryable: true,
                context: context),

            MeituStartingState.Busy => OperationFailure.Create(
                FailureCode.MeituUnknownState,
                "Meitu is computing. PrintFlow will not begin a new operation over an in-progress one.",
                isRetryable: true,
                context: context),

            _ => OperationFailure.Create(
                FailureCode.MeituUnknownState,
                "Meitu is not on a screen PrintFlow positively recognises. Nothing was clicked, dismissed " +
                "or closed; the operator must return Meitu to a clean start page.",
                isRetryable: true,
                context: context),
        };
    }

    /// <summary>States that will not improve by waiting, so polling should stop early.</summary>
    private static bool IsTerminal(MeituStartingState state) =>
        state is MeituStartingState.KnownModal;

    private static string ReasonFor(MeituStartingState state) => state switch
    {
        MeituStartingState.KnownModal => "blocking-dialog",
        MeituStartingState.Busy => "busy",
        _ => "unknown-state",
    };
}
