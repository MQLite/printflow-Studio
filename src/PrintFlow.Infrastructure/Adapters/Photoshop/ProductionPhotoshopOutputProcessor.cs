using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>
/// The production Photoshop adapter (Epic 11400 Part A §4, §19).
/// </summary>
/// <remarks>
/// It implements two interfaces that mean very different things, and the difference is the whole
/// design of this slice.
/// <list type="bullet">
///   <item><see cref="IPhotoshopAutomationFoundation"/> is complete for Part A: it verifies the
///         accepted binary, attaches or launches, recognises the screen, opens exactly one
///         managed Working file, and proves by absolute path which document got loaded.</item>
///   <item><see cref="IPhotoshopOutputProcessor"/> — the workflow seam — is deliberately
///         <b>fail-closed</b>. A production TIFF requires proportional sizing, 300 ppi, the W1
///         Action, CMYK conversion and a TIFF Save As, none of which exists yet. Returning a
///         success from it would be a lie that a Revision would then be built on, so it returns
///         a structured refusal and produces no file (§15, §18, §19).</item>
/// </list>
/// <see cref="Mode"/> is <see cref="AdapterExecutionMode.Production"/>, so
/// <c>IEnvironmentGate</c> remains authoritative over every step this adapter would back. The
/// adapter neither consults nor bypasses the gate; it declares what it is and lets
/// <c>SessionService</c> apply the gate before calling.
/// </remarks>
public sealed class ProductionPhotoshopOutputProcessor : IPhotoshopOutputProcessor, IPhotoshopAutomationFoundation
{
    private readonly IPhotoshopBaselineProvider _baselines;
    private readonly IExternalAppWindowLocator _locator;
    private readonly IPhotoshopUiDriver _driver;
    private readonly IWorkspace _workspace;
    private readonly PhotoshopAutomationOptions _options;
    private readonly TimeProvider _clock;

    public ProductionPhotoshopOutputProcessor(
        IPhotoshopBaselineProvider baselines,
        IExternalAppWindowLocator locator,
        IPhotoshopUiDriver driver,
        IWorkspace workspace,
        PhotoshopAutomationOptions options,
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
    public string AdapterId => "photoshop-cc2019-production-v1";

    /// <inheritdoc />
    public AdapterExecutionMode Mode => AdapterExecutionMode.Production;

    // -----------------------------------------------------------------------------------
    // Workflow seam — fail-closed until W1 and TIFF exist
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Refuses to produce a production TIFF, because Part A cannot produce one honestly.
    /// </summary>
    /// <remarks>
    /// This is not a stub that someone forgot to finish; it is the §19 boundary written down.
    /// The foundation below can do real, useful work — and precisely because it can, the
    /// tempting shortcut would be to open the file, return an <c>AdapterOutput</c> naming the
    /// expected output path, and let the validation pipeline discover the file is missing. That
    /// would put a fabricated success into the one place a Revision is created from.
    ///
    /// So the refusal happens first, before the request is examined and before Photoshop is
    /// touched at all: no window is activated, no keystroke is sent, and no file is created.
    /// </remarks>
    public Task<OperationResult<AdapterOutput>> GenerateAsync(
        PhotoshopRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(OperationResult.Fail<AdapterOutput>(OperationFailure.Create(
            FailureCode.PreconditionNotMet,
            "Production Photoshop output is not implemented. Epic 11400 Part A establishes only that " +
            "the accepted Photoshop can be identified and handed the exact managed Working file; " +
            "proportional sizing, 300 ppi, the W1 Action, CMYK conversion and the TIFF Save As are " +
            "later slices. No Photoshop window was touched, no file was produced and no Revision may " +
            "be created.",
            isRetryable: false,
            context: new Dictionary<string, string>
            {
                ["adapterId"] = AdapterId,
                ["implementedScope"] = "identify, open and identify document only",
                ["inputSent"] = "false",
                ["w1ActionInvoked"] = "false",
                ["tiffWritten"] = "false",
            })));
    }

    // -----------------------------------------------------------------------------------
    // Part A foundation
    // -----------------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<OperationResult<PhotoshopReadiness>> EnsureReadyAsync(
        CancellationToken cancellationToken)
    {
        OperationResult<PhotoshopBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<PhotoshopReadiness>(baseline.Failure);
        }

        OperationResult<Unit> identity = VerifyExecutableIdentity(baseline.Value);
        if (identity.IsFailure)
        {
            return OperationResult.Fail<PhotoshopReadiness>(identity.Failure);
        }

        OperationResult<IReadOnlyList<ExternalProcessRef>> running =
            _locator.FindProcessesByExecutable(baseline.Value.ExecutablePath);
        if (running.IsFailure)
        {
            return OperationResult.Fail<PhotoshopReadiness>(Translate(running.Failure));
        }

        if (running.Value.Count > 1)
        {
            // Which of several instances is "the" Photoshop is not a question PrintFlow may
            // answer by picking one — least of all when the wrong choice means driving an
            // instance holding someone else's work.
            return OperationResult.Fail<PhotoshopReadiness>(OperationFailure.Create(
                FailureCode.PhotoshopUnknownState,
                $"{running.Value.Count} processes are running from '{baseline.Value.ExecutablePath}'. " +
                "PrintFlow will not choose between them.",
                isRetryable: true,
                context: new Dictionary<string, string>
                {
                    ["candidates"] = running.Value.Count.ToString(CultureInfo.InvariantCulture),
                    ["inputSent"] = "false",
                }));
        }

        return running.Value.Count == 1
            ? await ReachSafeStateAsync(running.Value[0], launched: false, _options.AttachTimeout, cancellationToken)
                .ConfigureAwait(false)
            : await LaunchAsync(baseline.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<PhotoshopOpenedDocument>> OpenManagedWorkingFileAsync(
        WorkspaceFileRef workingFile, CancellationToken cancellationToken)
    {

        // The managed-area boundary, checked before anything is resolved to a path and long
        // before Photoshop is asked to open anything. A Source, Approved or Rejected reference is
        // refused here, so there is no route by which the customer's original could be handed to
        // an external application (§8).
        if (workingFile.Area != WorkspaceArea.Working)
        {
            return OperationResult.Fail<PhotoshopOpenedDocument>(OperationFailure.Create(
                FailureCode.PreconditionNotMet,
                $"Photoshop may only be given a Working copy; '{workingFile.RelativePath}' is in " +
                $"{workingFile.Area}. The customer original, the source snapshot and approved or " +
                "rejected outputs are never opened by an external application.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["area"] = workingFile.Area.ToString(),
                    ["inputSent"] = "false",
                }));
        }

        OperationResult<PhotoshopReadiness> ready =
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            return OperationResult.Fail<PhotoshopOpenedDocument>(ready.Failure);
        }

        // The only path join in this adapter, and it goes through the workspace — which is the
        // single component allowed to turn a managed reference into an absolute path. There is
        // no overload here taking a string, so no caller can name a destination the workspace
        // does not own (§8).
        string absolutePath = _workspace.ResolveAbsolute(workingFile);
        if (!File.Exists(absolutePath))
        {
            return OperationResult.Fail<PhotoshopOpenedDocument>(
                FailureCode.OutputMissing,
                $"The managed Working file '{workingFile.RelativePath}' does not exist on disk. " +
                "Nothing was sent to Photoshop.");
        }

        // Read before the open, because it cannot be read after: whether Photoshop was already
        // holding documents decides what cleanup is permitted to do later (§21).
        bool otherDocumentsOpen =
            ready.Value.State.State is PhotoshopStartingState.KnownEditorWithOtherDocument
                or PhotoshopStartingState.KnownEditorWithExpectedDocument;

        OperationResult<PhotoshopTarget> opened = await _driver
            .OpenManagedDocumentAsync(ready.Value.Target, absolutePath, cancellationToken)
            .ConfigureAwait(false);
        if (opened.IsFailure)
        {
            return Capture<PhotoshopOpenedDocument>(ready.Value.Target, opened.Failure, "open-failed");
        }

        return await ConfirmOpenedDocumentAsync(
            opened.Value, workingFile, absolutePath, otherDocumentsOpen, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for the expected document to appear, then proves by absolute path that it is the
    /// one loaded.
    /// </summary>
    /// <remarks>
    /// Two stages, and neither is redundant. The title poll is what tolerates Photoshop taking
    /// time to load a file without accepting a weaker claim — it can only ever say "a document
    /// with this name is now in front". The identity probe is what turns that into a statement
    /// about a file on disk. A run that got the first and not the second is a refusal, because
    /// that is exactly the "expected A, loaded B" case §12 exists to stop.
    /// </remarks>
    private async Task<OperationResult<PhotoshopOpenedDocument>> ConfirmOpenedDocumentAsync(
        PhotoshopTarget target,
        WorkspaceFileRef workingFile,
        string absolutePath,
        bool otherDocumentsOpen,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + _options.OpenConfirmationTimeout;
        PhotoshopStateSnapshot? last = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResult<PhotoshopStateSnapshot> state = await _driver
                .InspectStateAsync(target, workingFile.FileName, cancellationToken)
                .ConfigureAwait(false);
            if (state.IsFailure)
            {
                return OperationResult.Fail<PhotoshopOpenedDocument>(state.Failure);
            }

            last = state.Value;

            // The title now names the expected file. That is permission to run the probe, not a
            // conclusion: the classifier reports KnownEditorWithOtherDocument until a probe has
            // supplied a path, which is precisely the distinction being relied on here.
            if (state.Value.State is PhotoshopStartingState.KnownEditorWithOtherDocument
                or PhotoshopStartingState.KnownEditorWithExpectedDocument &&
                TitleNamesExpected(state.Value, workingFile.FileName))
            {
                return await ProveIdentityAsync(
                    target, workingFile, absolutePath, otherDocumentsOpen, state.Value, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (state.Value.State is PhotoshopStartingState.KnownModal)
            {
                return Capture<PhotoshopOpenedDocument>(target, OperationFailure.Create(
                    FailureCode.PhotoshopBlockingDialog,
                    "A dialog owned by Photoshop is blocking its window after the open was confirmed. " +
                    "PrintFlow does not dismiss dialogs it did not raise; the operator must resolve it.",
                    isRetryable: false,
                    context: Context(state.Value)), "open-blocked");
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                break;
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }

        return Capture<PhotoshopOpenedDocument>(target, OperationFailure.Create(
            FailureCode.PhotoshopDocumentIdentityUnconfirmed,
            $"'{workingFile.FileName}' was handed to Photoshop, but Photoshop never came to show it " +
            $"within {_options.OpenConfirmationTimeout.TotalSeconds:0}s. PrintFlow will not claim the " +
            "right file is open.",
            isRetryable: true,
            context: Context(last!)), "open-unconfirmed");
    }

    /// <summary>Runs the read-only identity probe and compares its answer to the expected file.</summary>
    private async Task<OperationResult<PhotoshopOpenedDocument>> ProveIdentityAsync(
        PhotoshopTarget target,
        WorkspaceFileRef workingFile,
        string absolutePath,
        bool otherDocumentsOpen,
        PhotoshopStateSnapshot titleState,
        CancellationToken cancellationToken)
    {
        OperationResult<PhotoshopDocumentIdentity> identity = await _driver
            .ProbeDocumentIdentityAsync(target, cancellationToken).ConfigureAwait(false);
        if (identity.IsFailure)
        {
            return Capture<PhotoshopOpenedDocument>(target, identity.Failure, "identity-unreadable");
        }

        if (!PhotoshopDocumentIdentityRule.MatchesExpectedDocument(
                absolutePath, identity.Value.ObservedFullPath))
        {
            // The refusal case that matters most: Photoshop is holding a real document, its name
            // may even match, and it is not the file this attempt handed over. Nothing continues
            // from here (§12).
            return Capture<PhotoshopOpenedDocument>(target, OperationFailure.Create(
                FailureCode.PhotoshopDocumentIdentityUnconfirmed,
                "Photoshop is holding a different document from the managed Working file PrintFlow " +
                "handed over. Nothing further was done to it.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["expectedDocument"] = absolutePath,
                    ["observedDocument"] = identity.Value.ObservedFullPath,
                    ["windowTitle"] = identity.Value.WindowTitle,
                    ["w1ActionInvoked"] = "false",
                    ["tiffWritten"] = "false",
                }), "identity-mismatch");
        }

        // Re-observed with the probe's answer in hand, so the recorded state is the one the
        // identity actually supports rather than the weaker one seen a moment earlier.
        OperationResult<PhotoshopStateSnapshot> confirmed = await _driver
            .InspectStateAsync(target, workingFile.FileName, cancellationToken).ConfigureAwait(false);
        if (confirmed.IsFailure)
        {
            return OperationResult.Fail<PhotoshopOpenedDocument>(confirmed.Failure);
        }

        PhotoshopStateSnapshot state = confirmed.Value with
        {
            State = PhotoshopStartingState.KnownEditorWithExpectedDocument,
            Observation = confirmed.Value.Observation with
            {
                ObservedDocumentFullPath = identity.Value.ObservedFullPath,
            },
        };

        _ = titleState;
        return OperationResult.Ok(new PhotoshopOpenedDocument(
            target, state, identity.Value, otherDocumentsOpen));
    }

    /// <inheritdoc />
    public async Task<OperationResult<PhotoshopTarget>> CloseExactDocumentAsync(
        PhotoshopOpenedDocument opened, WorkspaceFileRef workingFile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(opened);

        // Restated rather than inherited from the open path. "The caller already checked" is
        // exactly the reasoning that lets a non-managed reference through once someone adds a
        // second way in (§8).
        if (workingFile.Area != WorkspaceArea.Working)
        {
            return OperationResult.Fail<PhotoshopTarget>(
                FailureCode.PreconditionNotMet,
                $"Photoshop may only be asked to close a Working copy; '{workingFile.RelativePath}' " +
                $"is in {workingFile.Area}. Nothing was closed.");
        }

        string absolutePath = _workspace.ResolveAbsolute(workingFile);
        return await _driver.CloseExactDocumentAsync(opened.Target, absolutePath, cancellationToken)
            .ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------------------
    // Attach, launch, and reaching a recognised state
    // -----------------------------------------------------------------------------------

    private async Task<OperationResult<PhotoshopReadiness>> LaunchAsync(
        PhotoshopBaseline baseline, CancellationToken cancellationToken)
    {
        OperationResult<ExternalProcessRef> launched = _locator.Launch(baseline.ExecutablePath);
        if (launched.IsFailure)
        {
            return OperationResult.Fail<PhotoshopReadiness>(Translate(launched.Failure));
        }

        // Re-read from the live process rather than trusting the launch: the path PrintFlow asked
        // for and the path that is actually running are different facts, and only the second one
        // identifies what is about to be driven.
        if (!string.Equals(
                Path.GetFullPath(launched.Value.ExecutablePath),
                Path.GetFullPath(baseline.ExecutablePath),
                StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail<PhotoshopReadiness>(OperationFailure.Create(
                FailureCode.PhotoshopNotInstalled,
                $"The process that started reports '{launched.Value.ExecutablePath}', not the accepted " +
                $"'{baseline.ExecutablePath}'. No input was produced.",
                isRetryable: false,
                context: new Dictionary<string, string> { ["inputSent"] = "false" }));
        }

        return await ReachSafeStateAsync(
            launched.Value, launched: true, _options.LaunchTimeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Polls until the process presents one identifiable window in a recognised safe state.</summary>
    private async Task<OperationResult<PhotoshopReadiness>> ReachSafeStateAsync(
        ExternalProcessRef process, bool launched, TimeSpan timeout, CancellationToken cancellationToken)
    {
        OperationResult<PhotoshopBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<PhotoshopReadiness>(baseline.Failure);
        }

        DateTimeOffset deadline = _clock.GetUtcNow() + timeout;
        PhotoshopStateSnapshot? last = null;
        OperationFailure? lastFailure = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResult<PhotoshopTarget> target = IdentifyWindow(process, baseline.Value);
            if (target.IsSuccess)
            {
                OperationResult<PhotoshopStateSnapshot> state = await _driver
                    .InspectStateAsync(target.Value, expectedDocumentFileName: null, cancellationToken)
                    .ConfigureAwait(false);

                if (state.IsSuccess)
                {
                    last = state.Value;
                    if (state.Value.IsSafeStartingState)
                    {
                        return OperationResult.Ok(
                            new PhotoshopReadiness(target.Value, state.Value, launched));
                    }

                    // A modal will not clear by waiting, and PrintFlow will not clear it either.
                    if (state.Value.State is PhotoshopStartingState.KnownModal)
                    {
                        return Capture<PhotoshopReadiness>(
                            target.Value, UnsafeState(state.Value, launched), "blocking-dialog");
                    }
                }
                else
                {
                    lastFailure = state.Failure;
                }
            }
            else
            {
                lastFailure = target.Failure;
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                break;
            }

            await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }

        if (last is not null)
        {
            return OperationResult.Fail<PhotoshopReadiness>(UnsafeState(last, launched));
        }

        return OperationResult.Fail<PhotoshopReadiness>(lastFailure ?? OperationFailure.Create(
            launched ? FailureCode.PhotoshopLaunchFailed : FailureCode.PhotoshopWindowNotFound,
            $"Photoshop process {process.ProcessId} never presented an identifiable window within " +
            $"{timeout.TotalSeconds:0}s. No input was produced.",
            isRetryable: true,
            context: new Dictionary<string, string> { ["inputSent"] = "false" }));
    }

    /// <summary>
    /// Picks the one accepted top-level window, or refuses.
    /// </summary>
    /// <remarks>
    /// Ownership by the verified process plus the signed window class. Photoshop keeps dozens of
    /// windows in its process — layer palettes, CEF hosts, IME stubs, an OLE DDE server — and
    /// only one of them is the application frame. Requiring exactly one match means a second
    /// candidate is a refusal rather than a coin toss (§6).
    /// </remarks>
    private OperationResult<PhotoshopTarget> IdentifyWindow(
        ExternalProcessRef process, PhotoshopBaseline baseline)
    {
        OperationResult<IReadOnlyList<ExternalWindowRef>> windows = _locator.FindTopLevelWindows(process);
        if (windows.IsFailure)
        {
            return OperationResult.Fail<PhotoshopTarget>(Translate(windows.Failure));
        }

        List<ExternalWindowRef> candidates =
        [
            .. windows.Value.Where(window =>
                string.Equals(window.ClassName, baseline.MainWindowClassName, StringComparison.Ordinal) &&
                window.OwningProcessId == process.ProcessId &&
                window.IsVisible),
        ];

        if (candidates.Count == 0)
        {
            return OperationResult.Fail<PhotoshopTarget>(OperationFailure.Create(
                FailureCode.PhotoshopWindowNotFound,
                $"Process {process.ProcessId} owns no visible top-level window of class " +
                $"'{baseline.MainWindowClassName}', so there is no target any input could be " +
                "addressed to.",
                isRetryable: true,
                context: new Dictionary<string, string> { ["inputSent"] = "false" }));
        }

        if (candidates.Count > 1)
        {
            return OperationResult.Fail<PhotoshopTarget>(OperationFailure.Create(
                FailureCode.PhotoshopUnknownState,
                $"Process {process.ProcessId} owns {candidates.Count} visible windows of class " +
                $"'{baseline.MainWindowClassName}'. PrintFlow will not choose between them.",
                isRetryable: true,
                context: new Dictionary<string, string> { ["inputSent"] = "false" }));
        }

        return OperationResult.Ok(new PhotoshopTarget(process, candidates[0]));
    }

    // -----------------------------------------------------------------------------------
    // Executable identity
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Confirms the accepted executable is present, is the accepted version, and is the accepted
    /// binary (Epic 11400 Part A §5).
    /// </summary>
    /// <remarks>
    /// All three, and in that order. A path match alone would accept any binary sitting at the
    /// accepted location. A version match alone would accept a rebuilt binary reporting the same
    /// version. Hashing is what makes "this is the Photoshop Epic 11000 signed off" a checked
    /// fact — and the version is still checked alongside it, because when they disagree the
    /// version is what tells an operator which upgrade happened.
    ///
    /// A local installation that differs is a failure here, never a reason to accept it: the
    /// accepted baseline is not updated to match whatever is installed.
    /// </remarks>
    private static OperationResult<Unit> VerifyExecutableIdentity(PhotoshopBaseline baseline)
    {
        if (!File.Exists(baseline.ExecutablePath))
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.PhotoshopNotInstalled,
                $"The accepted Photoshop executable is not present at '{baseline.ExecutablePath}'.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["expectedPath"] = baseline.ExecutablePath,
                    ["excludedInstallations"] = baseline.ExcludedInstallations.IsDefaultOrEmpty
                        ? "(none recorded)"
                        : string.Join(" | ", baseline.ExcludedInstallations),
                    ["inputSent"] = "false",
                }));
        }

        FileVersionInfo version;
        try
        {
            version = FileVersionInfo.GetVersionInfo(baseline.ExecutablePath);
        }
        catch (FileNotFoundException ex)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.PhotoshopNotInstalled,
                $"The accepted Photoshop executable could not be read: {ex.Message}");
        }

        string actualProductVersion = version.ProductVersion ?? "(unreadable)";
        string actualFileVersion = version.FileVersion ?? "(unreadable)";

        if (!string.Equals(actualProductVersion, baseline.AcceptedProductVersion, StringComparison.Ordinal) ||
            !string.Equals(actualFileVersion, baseline.AcceptedFileVersion, StringComparison.Ordinal))
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.PhotoshopNotInstalled,
                $"The binary at '{baseline.ExecutablePath}' reports version " +
                $"'{actualProductVersion}' / '{actualFileVersion}', not the accepted " +
                $"'{baseline.AcceptedProductVersion}' / '{baseline.AcceptedFileVersion}'. A Photoshop " +
                "upgrade requires preset revalidation before automation resumes.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["expectedProductVersion"] = baseline.AcceptedProductVersion,
                    ["actualProductVersion"] = actualProductVersion,
                    ["expectedFileVersion"] = baseline.AcceptedFileVersion,
                    ["actualFileVersion"] = actualFileVersion,
                    ["inputSent"] = "false",
                }));
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
                FailureCode.PhotoshopNotInstalled,
                $"The accepted Photoshop executable could not be read: {ex.Message}");
        }

        return actual.Equals(baseline.ExecutableSha256)
            ? OperationResult.Ok()
            : OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.PhotoshopNotInstalled,
                $"The binary at '{baseline.ExecutablePath}' hashes to {actual}, not the accepted " +
                $"{baseline.ExecutableSha256}. A Photoshop upgrade requires preset revalidation before " +
                "automation resumes.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["expectedSha256"] = baseline.ExecutableSha256.ToString(),
                    ["actualSha256"] = actual.ToString(),
                    ["inputSent"] = "false",
                }));
    }

    // -----------------------------------------------------------------------------------
    // Failure shaping
    // -----------------------------------------------------------------------------------

    private static bool TitleNamesExpected(PhotoshopStateSnapshot state, string expectedFileName) =>
        state.Observation.WindowTitle.StartsWith(expectedFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Attaches a window capture to a failure, without letting the capture replace it.</summary>
    private OperationResult<T> Capture<T>(PhotoshopTarget target, OperationFailure failure, string reason)
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

    private static Dictionary<string, string> Context(PhotoshopStateSnapshot snapshot)
    {
        Dictionary<string, string> context = new(StringComparer.Ordinal)
        {
            ["state"] = snapshot.State.ToString(),
            ["matchedMarkers"] = string.Join(", ", snapshot.MatchedMarkers),

            // A count, never the classes themselves and never any window text. It separates
            // "PrintFlow could not read the window" from "PrintFlow read it and did not
            // recognise it" without writing anything that could describe a customer's file.
            ["visibleClassCount"] = snapshot.Observation.VisibleChildClasses.IsDefaultOrEmpty
                ? "0"
                : snapshot.Observation.VisibleChildClasses.Length.ToString(CultureInfo.InvariantCulture),
            ["inputSent"] = "false",
        };

        if (!snapshot.Observation.OwnedDialogTitles.IsDefaultOrEmpty &&
            snapshot.Observation.OwnedDialogTitles.Length > 0)
        {
            context["dialogTitles"] = string.Join(" | ", snapshot.Observation.OwnedDialogTitles);
        }

        return context;
    }

    private static OperationFailure UnsafeState(PhotoshopStateSnapshot snapshot, bool launched)
    {
        Dictionary<string, string> context = Context(snapshot);
        context["launchedByPrintFlow"] = launched ? "true" : "false";

        return snapshot.State switch
        {
            PhotoshopStartingState.KnownModal => OperationFailure.Create(
                FailureCode.PhotoshopBlockingDialog,
                "A dialog owned by Photoshop is blocking its window. PrintFlow does not dismiss dialogs " +
                "it cannot identify; the operator must resolve it.",
                isRetryable: true,
                context: context),

            PhotoshopStartingState.Busy => OperationFailure.Create(
                FailureCode.PhotoshopUnknownState,
                "Photoshop is computing. PrintFlow will not begin a new operation over an in-progress one.",
                isRetryable: true,
                context: context),

            _ => OperationFailure.Create(
                FailureCode.PhotoshopUnknownState,
                "Photoshop is not on a screen PrintFlow positively recognises. Nothing was clicked, " +
                "dismissed or closed; the operator must return Photoshop to a recognised state.",
                isRetryable: true,
                context: context),
        };
    }

    private static OperationFailure Translate(OperationFailure failure)
    {
        FailureCode translated = failure.Code switch
        {
            FailureCode.MeituNotInstalled => FailureCode.PhotoshopNotInstalled,
            FailureCode.MeituLaunchFailed => FailureCode.PhotoshopLaunchFailed,
            FailureCode.MeituWindowNotFound => FailureCode.PhotoshopWindowNotFound,
            FailureCode.MeituTargetLost => FailureCode.PhotoshopTargetLost,
            FailureCode.MeituUnknownState => FailureCode.PhotoshopUnknownState,
            FailureCode.MeituBlockingDialog => FailureCode.PhotoshopBlockingDialog,
            FailureCode.MeituOpenInputFailed => FailureCode.PhotoshopOpenInputFailed,
            _ => failure.Code,
        };

        return translated == failure.Code
            ? failure
            : failure with { Code = translated, MessageKey = $"Failure_{translated}" };
    }
}
