using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// The production Meitu adapter (Epic 11300 Part A §5).
/// </summary>
/// <remarks>
/// The controlled production seam now completes Enhancement and reviewed-content Background
/// Removal end to end. A success means a new managed output has been exported, settled,
/// inspected and validated while its Working input remained byte-for-byte unchanged; merely
/// opening a file or observing an operation finish is never a success boundary.
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
    private readonly IFileInspector _inspector;
    private readonly IMeituTransparencyInspector _transparencyInspector;
    private readonly IMeituOutputProbe _outputs;
    private readonly MeituAutomationOptions _options;
    private readonly TimeProvider _clock;

    public ProductionMeituProcessor(
        IMeituBaselineProvider baselines,
        IExternalAppWindowLocator locator,
        IMeituUiDriver driver,
        IWorkspace workspace,
        IFileInspector inspector,
        IMeituTransparencyInspector transparencyInspector,
        IMeituOutputProbe outputs,
        MeituAutomationOptions options,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(baselines);
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(transparencyInspector);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        _baselines = baselines;
        _locator = locator;
        _driver = driver;
        _workspace = workspace;
        _inspector = inspector;
        _transparencyInspector = transparencyInspector;
        _outputs = outputs;
        _options = options;
        _clock = clock;
    }

    /// <inheritdoc />
    public string AdapterId => "meitu-xiuxiu-production-v1";

    /// <inheritdoc />
    public AdapterExecutionMode Mode => AdapterExecutionMode.Production;

    /// <summary>
    /// Runs one Meitu operation end to end, and returns success only when a validated output
    /// file exists on the controlled path (Epic 11300 Part B2B §3, §27).
    /// </summary>
    /// <remarks>
    /// This is the first slice in which this method can succeed, and the boundary it enforces is
    /// worth stating as a list because every item on it was, at some point, something a shorter
    /// implementation would have been happy to call success: opening the file, clicking the
    /// module, Busy ending, the Save surface closing. None of them appears below as a terminal
    /// condition. What does is a file at the path this attempt named, which stopped changing,
    /// which was read to the end, and which inspects as a PNG no smaller than the working copy —
    /// with that working copy still byte-for-byte what PrintFlow handed over.
    ///
    /// Enhancement and Background Removal share only the proven open/export/stability/source
    /// machinery. Each keeps its own action/completion correlation and output rule: Enhancement
    /// accepts a non-shrinking PNG, while Background Removal requires a same-size PNG containing
    /// both real transparency and visible foreground.
    /// </remarks>
    public async Task<OperationResult<AdapterOutput>> ProcessAsync(
        MeituRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {

        if (!Enum.IsDefined(request.Operation))
        {
            return OperationResult.Fail<AdapterOutput>(OperationFailure.Create(
                FailureCode.AdapterUnavailable,
                $"The production Meitu adapter does not recognise operation '{request.Operation}'. " +
                "No file was produced and no Revision may be created.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["adapterId"] = AdapterId,
                    ["operation"] = request.Operation.ToString(),
                    ["implementedScope"] = "Enhancement and reviewed-content Background Removal",
                }));
        }

        // The refusal is deliberately first. Unspecified is the value the normal Session route
        // supplies until C2B adds reviewed-content/operator authority, and it must produce zero
        // Meitu interaction rather than becoming automatic selection anywhere below.
        if (request.Operation == MeituOperation.RemoveBackground &&
            request.BackgroundRemovalDecision !=
                BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent)
        {
            return OperationResult.Fail<AdapterOutput>(OperationFailure.Create(
                FailureCode.PreconditionNotMet,
                "PRODUCT DECISION REQUIRED: reviewed-content authority was not supplied for " +
                "Background Removal, so no Meitu input was produced.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["decision"] = request.BackgroundRemovalDecision.ToString(),
                    ["inputSent"] = "false",
                    ["adapterId"] = AdapterId,
                }));
        }

        OperationResult<Unit> references = ValidateRequestReferences(request);
        if (references.IsFailure)
        {
            return OperationResult.Fail<AdapterOutput>(references.Failure);
        }

        if (request.Operation == MeituOperation.RemoveBackground)
        {
            OperationResult<Unit> destination = MeituCutoutOutputRule.ValidateDestination(
                request.Input, request.ExpectedOutput);
            if (destination.IsFailure)
            {
                return OperationResult.Fail<AdapterOutput>(destination.Failure);
            }
        }

        DateTimeOffset startedAt = _clock.GetUtcNow();

        // Read before anything else touches it. This is both halves of §19's evidence — the
        // dimensions the result is measured against, and the digest that says the input survived
        // — and neither is worth anything taken after Meitu has had the file.
        OperationResult<FileFacts> sourceBefore = await InspectManagedFileAsync(request.Input, cancellationToken)
            .ConfigureAwait(false);
        if (sourceBefore.IsFailure)
        {
            return OperationResult.Fail<AdapterOutput>(sourceBefore.Failure);
        }

        OperationResult<MeituOpenedWorkingCopy> opened =
            await OpenWorkingCopyAsync(request.Input, request.Stop, cancellationToken).ConfigureAwait(false);
        if (opened.IsFailure)
        {
            return OperationResult.Fail<AdapterOutput>(opened.Failure);
        }

        return request.Operation == MeituOperation.Enhance
            ? await ProcessEnhancementAsync(
                request, opened.Value, sourceBefore.Value, startedAt, cancellationToken)
                .ConfigureAwait(false)
            : await ProcessBackgroundRemovalAsync(
                request, opened.Value, sourceBefore.Value, startedAt, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // PrintFlow has no signed Meitu Cancel-button policy in D1. Cancellation stops this
            // orchestration and, most importantly, licenses no delayed input. Meitu may still be
            // Busy or showing a result, so the next attempt must re-enter through EnsureReady
            // and prove a safe state rather than continuing this attempt.
            return OperationResult.Fail<AdapterOutput>(OperationFailure.Create(
                FailureCode.Cancelled,
                "PrintFlow orchestration was cancelled. No further Meitu input was produced; " +
                "Meitu may continue externally and its retained state is unknown.",
                isRetryable: true,
                context: new Dictionary<string, string>
                {
                    ["adapterId"] = AdapterId,
                    ["operation"] = request.Operation.ToString(),
                    ["retainedExternalState"] = "unknown",
                    ["meituCancelInvoked"] = "false",
                    ["forceTerminationInvoked"] = "false",
                },
                messageKey: "Failure_MeituInterrupted"));
        }
    }

    private async Task<OperationResult<AdapterOutput>> ProcessEnhancementAsync(
        MeituRequest request,
        MeituOpenedWorkingCopy opened,
        FileFacts sourceBefore,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        OperationResult<MeituEnhancementOutcome> enhanced =
            await EnhanceAsync(opened, request.Input, request.Stop, cancellationToken).ConfigureAwait(false);
        if (enhanced.IsFailure)
        {
            return OperationResult.Fail<AdapterOutput>(enhanced.Failure);
        }

        OperationResult<MeituExportedOutput> exported = await ExportEnhancedResultAsync(
            enhanced.Value, request.Input, sourceBefore, request.ExpectedOutput, request.Stop, cancellationToken)
            .ConfigureAwait(false);
        if (exported.IsFailure)
        {
            return OperationResult.Fail<AdapterOutput>(exported.Failure);
        }

        string cleanup = await ReturnToNeutralStateAsync(enhanced.Value.Target, cancellationToken)
            .ConfigureAwait(false);

        return OperationResult.Ok(new AdapterOutput(
            exported.Value.File,
            _clock.GetUtcNow() - startedAt,
            $"meitu:enhance; source {Describe(sourceBefore)}; output {Describe(exported.Value.Facts)}; " +
            $"format {exported.Value.Evidence.ConfirmedFormatValue}; " +
            $"settled after {exported.Value.ObservationsToSettle} observation(s); " +
            $"enhancement {(opened.Load.AutoStartedEnhancement ? "auto-started by Meitu and waited out" : "invoked by PrintFlow")}; " +
            $"cleanup {cleanup}"));
    }

    private async Task<OperationResult<AdapterOutput>> ProcessBackgroundRemovalAsync(
        MeituRequest request,
        MeituOpenedWorkingCopy opened,
        FileFacts sourceBefore,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        OperationResult<MeituBackgroundRemovalOutcome> removed = await RemoveBackgroundAsync(
            opened,
            request.Input,
            request.BackgroundRemovalDecision,
            request.Stop,
            cancellationToken).ConfigureAwait(false);
        if (removed.IsFailure)
        {
            return OperationResult.Fail<AdapterOutput>(removed.Failure);
        }

        OperationResult<MeituExportedOutput> exported = await ExportBackgroundRemovalResultAsync(
            removed.Value, request.Input, sourceBefore, request.ExpectedOutput, request.Stop, cancellationToken)
            .ConfigureAwait(false);
        if (exported.IsFailure)
        {
            return OperationResult.Fail<AdapterOutput>(exported.Failure);
        }

        string cleanup = await ReturnToNeutralStateAsync(removed.Value.Target, cancellationToken)
            .ConfigureAwait(false);
        MeituTransparencyFacts alpha = exported.Value.Transparency!;

        return OperationResult.Ok(new AdapterOutput(
            exported.Value.File,
            _clock.GetUtcNow() - startedAt,
            $"meitu:remove-background; decision {request.BackgroundRemovalDecision}; " +
            $"source {Describe(sourceBefore)}; output {Describe(exported.Value.Facts)}; " +
            $"format {exported.Value.Evidence.ConfirmedFormatValue}; " +
            $"transparent pixels {alpha.TransparentPixelCount}/{alpha.PixelCount}; " +
            $"visible pixels {alpha.VisiblePixelCount}/{alpha.PixelCount}; " +
            $"settled after {exported.Value.ObservationsToSettle} observation(s); cleanup {cleanup}"));
    }

    /// <summary>
    /// Checks everything about the request that can be decided before Meitu is involved
    /// (Epic 11300 Part B2B §4, §7, §19, §33).
    /// </summary>
    /// <remarks>
    /// The input-is-not-the-output check is the one that would be easy to leave out and
    /// expensive to omit. Until this slice the workflow named the working copy as its own
    /// expected output, because the fake adapter "processes in place" — and an in-place
    /// production export would mean Meitu writing over the file PrintFlow is in the middle of
    /// comparing against. §19 requires the enhanced result to be a new file, and this is where
    /// that stops depending on the caller having remembered.
    /// </remarks>
    private OperationResult<Unit> ValidateRequestReferences(MeituRequest request)
    {
        if (request.Input.Area != WorkspaceArea.Working)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.PreconditionNotMet,
                $"Meitu may only be given a Working copy; '{request.Input.RelativePath}' is in " +
                $"{request.Input.Area}. No Meitu window was touched.");
        }

        if (request.ExpectedOutput.Area != WorkspaceArea.Working)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.PreconditionNotMet,
                $"A Meitu result may only be written into the Working area; " +
                $"'{request.ExpectedOutput.RelativePath}' is in {request.ExpectedOutput.Area}. Approved " +
                "and Rejected are reached by promotion after review, never by an external application.");
        }

        if (string.Equals(
                request.ExpectedOutput.RelativePath, request.Input.RelativePath, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail<Unit>(
                FailureCode.PreconditionNotMet,
                $"The expected output '{request.ExpectedOutput.RelativePath}' is the working copy itself. " +
                "The result must be a new file: exporting over the input would destroy the bytes " +
                "the attempt is validated against. Nothing was invoked.");
        }

        return OperationResult.Ok();
    }

    /// <inheritdoc />
    public async Task<OperationResult<FileFacts>> InspectManagedFileAsync(
        WorkspaceFileRef file, CancellationToken cancellationToken) =>
        await _inspector.InspectAsync(_workspace.ResolveAbsolute(file), cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<OperationResult<MeituExportedOutput>> ExportEnhancedResultAsync(
        MeituEnhancementOutcome enhancement,
        WorkspaceFileRef workingCopy,
        FileFacts workingCopyFactsBefore,
        WorkspaceFileRef output,
        IAutomationStopSignal stop,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(enhancement);
        ArgumentNullException.ThrowIfNull(workingCopyFactsBefore);

        return await ExportValidatedResultAsync(
            MeituOperation.Enhance,
            enhancement.Target,
            enhancement.ObservedDocumentIdentity,
            workingCopy,
            workingCopyFactsBefore,
            output,
            stop,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<MeituExportedOutput>> ExportBackgroundRemovalResultAsync(
        MeituBackgroundRemovalOutcome backgroundRemoval,
        WorkspaceFileRef workingCopy,
        FileFacts workingCopyFactsBefore,
        WorkspaceFileRef output,
        IAutomationStopSignal stop,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backgroundRemoval);
        ArgumentNullException.ThrowIfNull(workingCopyFactsBefore);

        if (backgroundRemoval.ModeDecision !=
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent)
        {
            return OperationResult.Fail<MeituExportedOutput>(
                FailureCode.PreconditionNotMet,
                "The completed Background Removal observation carries no reviewed-content " +
                "authority. Nothing was exported.");
        }

        // Busy and completion are non-nullable observations on the outcome. Requiring the
        // operation-specific phases again prevents a manually fabricated/stale completion
        // record from becoming export authority.
        OperationResult<MeituBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure || baseline.Value.BackgroundRemoval is not { } signature)
        {
            return baseline.IsFailure
                ? OperationResult.Fail<MeituExportedOutput>(baseline.Failure)
                : OperationResult.Fail<MeituExportedOutput>(
                    FailureCode.EnvironmentNotVerified,
                    "The verified preset carries no Background Removal signature. Nothing was exported.");
        }

        if (MeituBackgroundRemovalRule.Classify(signature, backgroundRemoval.Busy.Observation) !=
                MeituBackgroundRemovalPhase.Busy ||
            MeituBackgroundRemovalRule.Classify(signature, backgroundRemoval.Completion.Observation) !=
                MeituBackgroundRemovalPhase.Complete ||
            !string.Equals(
                backgroundRemoval.IdentityAfterCompletion.Observation.ObservedDocumentIdentity,
                backgroundRemoval.ObservedDocumentIdentity,
                StringComparison.Ordinal))
        {
            return OperationResult.Fail<MeituExportedOutput>(
                FailureCode.MeituUnknownState,
                "Background Removal export requires current-run Busy, positive completion and exact " +
                "post-completion identity correlation. Nothing was exported.");
        }

        OperationResult<Unit> destination = MeituCutoutOutputRule.ValidateDestination(workingCopy, output);
        if (destination.IsFailure)
        {
            return OperationResult.Fail<MeituExportedOutput>(destination.Failure);
        }

        return await ExportValidatedResultAsync(
            MeituOperation.RemoveBackground,
            backgroundRemoval.Target,
            backgroundRemoval.ObservedDocumentIdentity,
            workingCopy,
            workingCopyFactsBefore,
            output,
            stop,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The one signed Save → 另存为 → controlled dialog → stability → inspection route shared
    /// by both operations. Only the final output rule varies.
    /// </summary>
    private async Task<OperationResult<MeituExportedOutput>> ExportValidatedResultAsync(
        MeituOperation operation,
        MeituTarget target,
        string observedDocumentIdentity,
        WorkspaceFileRef workingCopy,
        FileFacts workingCopyFactsBefore,
        WorkspaceFileRef output,
        IAutomationStopSignal stop,
        CancellationToken cancellationToken)
    {

        // Restated rather than inherited, exactly as the enhancement route restates it. This
        // method resolves two references to real paths and drives an application to write to one
        // of them; "the caller already checked" is the reasoning that lets the second way in
        // through unchecked (§16).
        if (workingCopy.Area != WorkspaceArea.Working || output.Area != WorkspaceArea.Working)
        {
            return OperationResult.Fail<MeituExportedOutput>(
                FailureCode.PreconditionNotMet,
                $"An export runs between two Working references; '{workingCopy.RelativePath}' is in " +
                $"{workingCopy.Area} and '{output.RelativePath}' is in {output.Area}. Nothing was invoked.");
        }

        if (string.Equals(output.RelativePath, workingCopy.RelativePath, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail<MeituExportedOutput>(
                FailureCode.PreconditionNotMet,
                "The export destination is the working copy itself. Nothing was invoked.");
        }

        string destination = _workspace.ResolveAbsolute(output);

        // §33. The attempt directory is fresh, so anything already sitting on this exact path is
        // something PrintFlow cannot account for — and the one thing it must not do with a file
        // it cannot account for is write over it. There is no collision-suffix rule here on
        // purpose: uniqueness comes from the attempt directory, and quietly renaming the output
        // would hide the fact that an attempt's own workspace was not what it expected.
        if (File.Exists(destination))
        {
            return OperationResult.Fail<MeituExportedOutput>(OperationFailure.Create(
                FailureCode.PreconditionNotMet,
                $"'{output.RelativePath}' already exists inside this attempt's own working directory. " +
                "PrintFlow will not overwrite a file it did not create in this attempt, and will not " +
                "silently write somewhere else instead. Nothing was invoked.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["expectedOutput"] = output.RelativePath,
                    ["exportInvoked"] = "false",
                }));
        }

        OperationResult<MeituExportEvidence> exported = await _driver.ExportResultAsync(
            target,
            workingCopy.FileName,
            observedDocumentIdentity,
            destination,
            stop,
            cancellationToken).ConfigureAwait(false);

        if (exported.IsFailure)
        {
            return Capture<MeituExportedOutput>(target, exported.Failure, "export-failed");
        }

        // §13, §14. From here the screen is irrelevant: the file system is the authority on
        // whether anything was produced, and the Save surface having closed is not evidence.
        OperationResult<int> settled = await AwaitSettledOutputAsync(destination, cancellationToken)
            .ConfigureAwait(false);
        if (settled.IsFailure)
        {
            return OperationResult.Fail<MeituExportedOutput>(settled.Failure);
        }

        OperationResult<FileFacts> outputFacts = await InspectManagedFileAsync(output, cancellationToken)
            .ConfigureAwait(false);
        if (outputFacts.IsFailure)
        {
            return OperationResult.Fail<MeituExportedOutput>(outputFacts.Failure);
        }

        MeituTransparencyFacts? transparency = null;
        OperationResult<Unit> valid;
        if (operation == MeituOperation.RemoveBackground)
        {
            OperationResult<MeituTransparencyFacts> alpha = await _transparencyInspector
                .InspectAsync(destination, cancellationToken).ConfigureAwait(false);
            if (alpha.IsFailure)
            {
                return OperationResult.Fail<MeituExportedOutput>(alpha.Failure);
            }

            transparency = alpha.Value;
            valid = MeituCutoutOutputRule.Validate(
                workingCopyFactsBefore, outputFacts.Value, transparency);
        }
        else
        {
            valid = MeituEnhancementOutputRule.Validate(
                workingCopyFactsBefore, outputFacts.Value, ImageFormat.Png);
        }

        if (valid.IsFailure)
        {
            return OperationResult.Fail<MeituExportedOutput>(valid.Failure);
        }

        OperationResult<FileFacts> sourceAfter = await InspectManagedFileAsync(workingCopy, cancellationToken)
            .ConfigureAwait(false);
        if (sourceAfter.IsFailure)
        {
            return OperationResult.Fail<MeituExportedOutput>(sourceAfter.Failure);
        }

        OperationResult<Unit> unchanged = MeituEnhancementOutputRule.ConfirmSourceUnchanged(
            workingCopyFactsBefore, sourceAfter.Value, workingCopy.FileName);

        if (unchanged.IsFailure)
        {
            return OperationResult.Fail<MeituExportedOutput>(unchanged.Failure);
        }

        // §16. A validated output now exists on the controlled path. Recording the phase here —
        // after every check that could still have refused it, and before the caller can create
        // a Revision from it — is what makes "a late Stop cannot erase a validated success" a
        // fact about the reported phase rather than a hope about timing.
        stop.ReportPhase(ExternalOperationPhase.OutputValidated);

        return OperationResult.Ok(new MeituExportedOutput(
                output, outputFacts.Value, exported.Value, settled.Value, transparency));
    }

    /// <summary>
    /// Waits for the controlled output to appear and stop changing (Epic 11300 Part B2B §14, §15).
    /// </summary>
    /// <remarks>
    /// Cancellation stops the wait and deliberately leaves whatever is on disk alone. Meitu may
    /// still be writing it, and deleting a file another process holds is not a cleanup PrintFlow
    /// can prove is safe; an unreferenced file in an attempt's own working directory is handled
    /// by the workspace's existing quarantine policy, which does not delete either (§34).
    /// </remarks>
    private async Task<OperationResult<int>> AwaitSettledOutputAsync(
        string destination, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + _options.OutputStabilityTimeout;
        List<MeituOutputObservation> observations = [];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            observations.Add(_outputs.Probe(destination));
            if (MeituOutputStabilityRule.IsSettled(observations))
            {
                return OperationResult.Ok(observations.Count);
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                MeituOutputObservation last = observations[^1];
                return OperationResult.Fail<int>(OperationFailure.Create(
                    last.Exists ? FailureCode.OutputUnreadable : FailureCode.OutputMissing,
                    $"The controlled output did not settle within " +
                    $"{_options.OutputStabilityTimeout.TotalSeconds:0} s: " +
                    $"{MeituOutputStabilityRule.DescribeUnsettled(observations)}. The export was invoked " +
                    "once and was not repeated; no Revision may be created.",
                    isRetryable: true,
                    context: new Dictionary<string, string>
                    {
                        ["observations"] = observations.Count.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        ["exists"] = last.Exists ? "true" : "false",
                        ["byteLength"] = last.ByteLength.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        ["readable"] = last.CanOpenForRead ? "true" : "false",
                    }));
            }

            await Task.Delay(_options.OutputPollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Tidies Meitu after a successful export, and reports rather than fails
    /// (Epic 11300 Part B2B §24, §26).
    /// </summary>
    /// <remarks>
    /// Returns a description instead of a result because there is no caller-visible decision to
    /// make: the output already exists and is already validated, so the only question is what to
    /// write down. What the caller must not be able to do is treat a tidy-up problem as a
    /// processing problem, and a method that cannot fail cannot be misread that way.
    ///
    /// The order matters. Meitu's post-save surface disables the editor while it is up, so the
    /// document cannot be closed until it is dismissed — and the close is what makes the next
    /// attempt's open a clean one. Closing after an export was observed to reach the empty editor
    /// directly: Meitu no longer considers the document modified, so the 温馨提示 prompt that
    /// Part B2A recorded does not appear. If it appears anyway, the close route classifies it as
    /// a blocking modal and stops without touching it, and that is what gets reported here.
    /// </remarks>
    private async Task<string> ReturnToNeutralStateAsync(
        MeituTarget target, CancellationToken cancellationToken)
    {
        try
        {
            OperationResult<bool> dismissed = await _driver
                .DismissExportResultSurfaceAsync(target, cancellationToken).ConfigureAwait(false);
            if (dismissed.IsFailure)
            {
                return $"WARNING: Meitu's save-confirmation surface could not be dismissed " +
                       $"({dismissed.Failure.Code}). The output is valid; the next attempt must not " +
                       "proceed until an operator has cleared it.";
            }

            OperationResult<MeituTarget> closed = await _driver
                .CloseDocumentAsync(target, cancellationToken).ConfigureAwait(false);

            return closed.IsSuccess
                ? "the editor was returned to its signed empty state"
                : $"WARNING: the document is still loaded ({closed.Failure.Code}: " +
                  $"{closed.Failure.TechnicalDetail}). The output is valid; Meitu is not in a state the " +
                  "next attempt may blindly proceed against.";
        }
        catch (OperationCanceledException)
        {
            // §34: cancellation after the output exists must not discard it. The result is
            // already validated; only the tidying is abandoned.
            return "WARNING: cleanup was cancelled. The output is valid; the document may still be loaded.";
        }
    }

    private static string Describe(FileFacts facts) =>
        $"{facts.Format} {facts.PixelWidth}x{facts.PixelHeight} {facts.ByteLength} bytes sha256={facts.Sha256}";

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
        WorkspaceFileRef workingCopy, IAutomationStopSignal stop, CancellationToken cancellationToken)
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
        if (baseline.Value.DocumentIdentity is null)
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

        // Before identity, and this order is required rather than convenient. Meitu can begin
        // enhancing the moment the document appears, and while it is computing the editor is
        // disabled and the Save surface the identity probe needs cannot be raised — so probing
        // first would fail on a run that is proceeding perfectly normally. Watching first also
        // captures the one fact that cannot be recovered later: whether the work now running
        // started after *this* open (§20, §21, §22).
        OperationResult<MeituLoadObservation> load = await _driver
            .ObserveLoadedDocumentAsync(opened.Value, workingCopy.FileName, stop, cancellationToken)
            .ConfigureAwait(false);
        if (load.IsFailure)
        {
            return Capture<MeituOpenedWorkingCopy>(opened.Value, load.Failure, "open-unsettled");
        }

        OperationResult<MeituStateSnapshot> confirmed = await _driver
            .ConfirmWorkingCopyIdentityAsync(opened.Value, workingCopy.FileName, cancellationToken)
            .ConfigureAwait(false);

        if (confirmed.IsFailure)
        {
            return Capture<MeituOpenedWorkingCopy>(opened.Value, confirmed.Failure, "open-unconfirmed");
        }

        // The identity probe is what makes the observation above attributable. Busy was seen
        // between this open and this probe, and the probe says the editor is holding exactly the
        // file PrintFlow handed over — so an enhancement Meitu started by itself is provably an
        // enhancement of this working copy. §20 asks for the strongest safe route available;
        // this is it, and if it fails nothing further happens.
        return OperationResult.Ok(new MeituOpenedWorkingCopy(opened.Value, confirmed.Value, load.Value));
    }

    /// <inheritdoc />
    public async Task<OperationResult<MeituEnhancementOutcome>> EnhanceAsync(
        MeituOpenedWorkingCopy opened, WorkspaceFileRef workingCopy, IAutomationStopSignal stop,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(opened);

        // The same boundary as the open path, restated rather than inherited. Enhancement is
        // the first irreversible thing this adapter does to a document, and "the caller already
        // checked" is exactly the reasoning that lets a Source or Approved reference through
        // once someone adds a second way in (§16).
        if (workingCopy.Area != WorkspaceArea.Working)
        {
            return OperationResult.Fail<MeituEnhancementOutcome>(
                FailureCode.PreconditionNotMet,
                $"Meitu may only be asked to enhance a Working copy; '{workingCopy.RelativePath}' is in " +
                $"{workingCopy.Area}. No Enhancement action was invoked.");
        }

        // Meitu already did it. The open watched Busy start and finish over this document and
        // the identity probe then confirmed the document, so the work is this attempt's work —
        // and invoking the module now would not run it again but toggle it off, discarding the
        // result. Nothing is invoked (§20, §21).
        if (opened.Load is { AutoStartedEnhancement: true, Busy: { } busy, Completion: { } completion })
        {
            OperationResult<MeituStateSnapshot> after = await _driver
                .ConfirmWorkingCopyIdentityAsync(opened.Target, workingCopy.FileName, cancellationToken)
                .ConfigureAwait(false);
            if (after.IsFailure)
            {
                return Capture<MeituEnhancementOutcome>(
                    opened.Target, after.Failure, "auto-enhancement-unconfirmed");
            }

            if (after.Value.Observation.ObservedDocumentIdentity is not { Length: > 0 } identity)
            {
                return OperationResult.Fail<MeituEnhancementOutcome>(
                    FailureCode.MeituUnknownState,
                    "The identity probe reported success without a document-derived value, so an " +
                    "Enhancement Meitu started by itself cannot be attributed to this working copy. " +
                    "Nothing was exported.");
            }

            return OperationResult.Ok(new MeituEnhancementOutcome(
                opened.Target, identity, opened.State, busy, completion, after.Value));
        }

        // The state the open path confirmed is a fact about the past. Nothing is invoked on the
        // strength of it: RunEnhancementAsync re-probes identity, re-acquires the editor and
        // re-verifies the target before it produces any input (§5, §6, §10).
        //
        // The stale-module case reaches here too, and correctly. A completion panel left over
        // from the previous document is not an enhancement of this one, so the run proceeds to
        // the pre-invoke guard — which refuses, naming the reason that actually applies: the
        // module is selected, so invoking it would deselect it rather than start work (§21).
        OperationResult<MeituEnhancementOutcome> run = await _driver
            .RunEnhancementAsync(opened.Target, workingCopy.FileName, stop, cancellationToken)
            .ConfigureAwait(false);

        return run.IsFailure
            ? Capture<MeituEnhancementOutcome>(opened.Target, run.Failure, "enhancement-failed")
            : run;
    }

    /// <inheritdoc />
    public async Task<OperationResult<MeituBackgroundRemovalOutcome>> RemoveBackgroundAsync(
        MeituOpenedWorkingCopy opened,
        WorkspaceFileRef workingCopy,
        BackgroundRemovalDecision modeDecision,
        IAutomationStopSignal stop,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(opened);

        if (workingCopy.Area != WorkspaceArea.Working)
        {
            return OperationResult.Fail<MeituBackgroundRemovalOutcome>(
                FailureCode.PreconditionNotMet,
                $"Meitu may only remove the background from a Working copy; " +
                $"'{workingCopy.RelativePath}' is in {workingCopy.Area}. No input was produced.");
        }

        OperationResult<MeituBackgroundRemovalOutcome> run = await _driver
            .RunBackgroundRemovalAsync(
                opened.Target,
                workingCopy.FileName,
                modeDecision,
                stop,
                cancellationToken)
            .ConfigureAwait(false);

        return run.IsFailure
            ? Capture<MeituBackgroundRemovalOutcome>(
                opened.Target, run.Failure, "background-removal-c1-failed")
            : run;
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> DismissExportResultSurfaceAsync(
        MeituTarget target, CancellationToken cancellationToken) =>
        _driver.DismissExportResultSurfaceAsync(target, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<MeituTarget>> CloseDocumentAsync(
        MeituTarget target, CancellationToken cancellationToken) =>
        _driver.CloseDocumentAsync(target, cancellationToken);

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
