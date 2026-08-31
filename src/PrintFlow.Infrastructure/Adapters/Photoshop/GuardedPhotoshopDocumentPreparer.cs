using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>
/// Reuses every Part A process/window/document guard around one fixed in-memory Image Size call,
/// then validates Photoshop's factual read-back and the untouched backing file.
/// </summary>
internal sealed class GuardedPhotoshopDocumentPreparer
{
    private const double ResolutionTolerancePpi = 0.0000001;
    private const double PhysicalRepresentationEpsilonMm = 0.000001;

    private readonly IPhotoshopBaselineProvider _baselines;
    private readonly IExternalAppWindowLocator _locator;
    private readonly IPhotoshopUiDriver _driver;
    private readonly IPhotoshopPreparationNativeBridge _native;

    internal GuardedPhotoshopDocumentPreparer(
        IPhotoshopBaselineProvider baselines,
        IExternalAppWindowLocator locator,
        IPhotoshopUiDriver driver,
        IPhotoshopPreparationNativeBridge native)
    {
        _baselines = baselines ?? throw new ArgumentNullException(nameof(baselines));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    internal async Task<OperationResult<PhotoshopPreparedDocument>> PrepareDocumentAsync(
        PhotoshopOpenedDocument opened,
        PhotoshopPreparation preparation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(opened);
        ArgumentNullException.ThrowIfNull(preparation);

        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return CancelledBeforeMutation(opened.Identity.ObservedFullPath);
            }

            OperationResult<PreparedOperation> operation = BuildOperation(preparation);
            if (operation.IsFailure)
            {
                return OperationResult.Fail<PhotoshopPreparedDocument>(operation.Failure);
            }

            OperationResult<PhotoshopBaseline> baseline = _baselines.GetVerifiedBaseline();
            if (baseline.IsFailure)
            {
                return OperationResult.Fail<PhotoshopPreparedDocument>(baseline.Failure);
            }

            OperationResult<PhotoshopTarget> guarded = await VerifyExactMutationTargetAsync(
                opened, baseline.Value, cancellationToken).ConfigureAwait(false);
            if (guarded.IsFailure)
            {
                return OperationResult.Fail<PhotoshopPreparedDocument>(guarded.Failure);
            }

            string backingPath = Path.GetFullPath(opened.Identity.ObservedFullPath);
            if (!Path.IsPathFullyQualified(backingPath) || !File.Exists(backingPath))
            {
                return OperationResult.Fail<PhotoshopPreparedDocument>(OperationFailure.Create(
                    FailureCode.PreconditionNotMet,
                    "The exact managed Working backing file is no longer present. Nothing was changed.",
                    context: NoMutationContext(backingPath)));
            }

            OperationResult<Sha256> hashBefore = Hash(backingPath);
            if (hashBefore.IsFailure)
            {
                return OperationResult.Fail<PhotoshopPreparedDocument>(hashBefore.Failure);
            }

            if (!hashBefore.Value.Equals(preparation.SourceSha256))
            {
                return OperationResult.Fail<PhotoshopPreparedDocument>(OperationFailure.Create(
                    FailureCode.RevisionIntegrityMismatch,
                    "The managed Working bytes no longer match the immutable preparation source. " +
                    "Nothing was changed.",
                    context: new Dictionary<string, string>(NoMutationContext(backingPath))
                    {
                        ["expectedSha256"] = preparation.SourceSha256.ToString(),
                        ["actualSha256"] = hashBefore.Value.ToString(),
                    }));
            }

            OperationResult<ImmutableArray<string>> filesBefore = SnapshotDirectory(backingPath);
            if (filesBefore.IsFailure)
            {
                return OperationResult.Fail<PhotoshopPreparedDocument>(filesBefore.Failure);
            }

            // This is the final cancellation observation before the synchronous, non-interruptible
            // native call. No Photoshop call occurs after a cancellation already observed here.
            if (cancellationToken.IsCancellationRequested)
            {
                return CancelledBeforeMutation(backingPath, hashBefore.Value);
            }

            PhotoshopNativePreparationCommand command = new(
                backingPath,
                preparation.SourcePixelWidth,
                preparation.SourcePixelHeight,
                operation.Value.CommandedEdge,
                operation.Value.EdgeMillimetres,
                preparation.ProductionDpi,
                operation.Value.NativeMethod);

            OperationResult<PhotoshopNativePreparationOutcome> native =
                _native.ApplyOnce(command, baseline.Value);

            // The native call is synchronous. Once it has been entered, cancellation cannot be a
            // rollback claim; finish every safe factual check using an uncancelled observation path.
            bool cancellationAfterCall = cancellationToken.IsCancellationRequested;
            OperationResult<Sha256> hashAfter = Hash(backingPath);
            OperationResult<ImmutableArray<string>> filesAfter = SnapshotDirectory(backingPath);

            if (hashAfter.IsFailure)
            {
                return RetainedFailure(hashAfter.Failure, cancellationAfterCall);
            }

            if (filesAfter.IsFailure)
            {
                return RetainedFailure(filesAfter.Failure, cancellationAfterCall);
            }

            if (!hashBefore.Value.Equals(hashAfter.Value))
            {
                return RetainedFailure(OperationFailure.Create(
                    FailureCode.OutputValidationFailed,
                    "Photoshop preparation changed the managed Working backing bytes. No save was " +
                    "authorised and preparation cannot be accepted.",
                    context: new Dictionary<string, string>
                    {
                        ["sha256Before"] = hashBefore.Value.ToString(),
                        ["sha256After"] = hashAfter.Value.ToString(),
                    }), cancellationAfterCall);
            }

            if (!filesBefore.Value.SequenceEqual(filesAfter.Value, StringComparer.OrdinalIgnoreCase))
            {
                return RetainedFailure(OperationFailure.Create(
                    FailureCode.OutputValidationFailed,
                    "A file appeared or disappeared beside the managed Working document during the " +
                    "in-memory preparation. No output is accepted.",
                    context: new Dictionary<string, string>
                    {
                        ["filesBefore"] = filesBefore.Value.Length.ToString(CultureInfo.InvariantCulture),
                        ["filesAfter"] = filesAfter.Value.Length.ToString(CultureInfo.InvariantCulture),
                    }), cancellationAfterCall);
            }

            OperationResult<PhotoshopTarget> afterGuard = await VerifyExactMutationTargetAsync(
                opened with { Target = guarded.Value }, baseline.Value, CancellationToken.None)
                .ConfigureAwait(false);
            if (afterGuard.IsFailure)
            {
                return RetainedFailure(afterGuard.Failure, cancellationAfterCall);
            }

            if (native.IsFailure)
            {
                return RetainedFailure(native.Failure, cancellationAfterCall);
            }

            PhotoshopNativePreparationOutcome observed = native.Value;
            if (!observed.Succeeded || !observed.MutationInvoked ||
                observed.Before is null || observed.After is null)
            {
                OperationFailure failure = OperationFailure.Create(
                    observed.MutationInvoked
                        ? FailureCode.PhotoshopUnknownState
                        : FailureCode.PreconditionNotMet,
                    observed.FailureDetail ??
                    "Photoshop did not return a complete factual preparation result.",
                    isRetryable: false,
                    context: new Dictionary<string, string>
                    {
                        ["mutationInvoked"] = observed.MutationInvoked ? "true" : "false",
                        ["automaticRetry"] = "false",
                    });
                return observed.MutationInvoked
                    ? RetainedFailure(failure, cancellationAfterCall)
                    : OperationResult.Fail<PhotoshopPreparedDocument>(failure);
            }

            OperationResult<Unit> validation = ValidateReadBack(
                opened,
                preparation,
                operation.Value,
                observed.Before,
                observed.After);
            if (validation.IsFailure)
            {
                return RetainedFailure(validation.Failure, cancellationAfterCall);
            }

            PhotoshopPreparedDocument result = new(
                observed.Before,
                observed.After,
                operation.Value.Direction,
                preparation.ResizePolicy,
                operation.Value.CommandedEdge,
                opened.OtherDocumentsMayBeOpen || observed.Before.DocumentCount > 1,
                hashAfter.Value);

            if (cancellationAfterCall)
            {
                return OperationResult.Fail<PhotoshopPreparedDocument>(OperationFailure.Create(
                    FailureCode.Cancelled,
                    "Cancellation was requested after Photoshop's synchronous preparation began. " +
                    "The factual read-back completed, the backing file is unchanged, and the prepared " +
                    "in-memory document may be retained. Nothing was saved and no later operation ran.",
                    context: ResultContext(result)));
            }

            return OperationResult.Ok(result);
        }
        catch (OperationCanceledException)
        {
            return CancelledBeforeMutation(opened.Identity.ObservedFullPath);
        }
    }

    internal async Task<OperationResult<PhotoshopTarget>> VerifyExactMutationTargetAsync(
        PhotoshopOpenedDocument opened,
        PhotoshopBaseline baseline,
        CancellationToken cancellationToken,
        bool probeDocumentIdentity = true)
    {
        OperationResult<Unit> executable = PhotoshopExecutableIdentityRule.Verify(baseline);
        if (executable.IsFailure)
        {
            return OperationResult.Fail<PhotoshopTarget>(executable.Failure);
        }

        OperationResult<IReadOnlyList<ExternalProcessRef>> processes =
            _locator.FindProcessesByExecutable(baseline.ExecutablePath);
        if (processes.IsFailure)
        {
            return OperationResult.Fail<PhotoshopTarget>(AsPhotoshop(processes.Failure));
        }

        if (processes.Value.Count != 1)
        {
            return OperationResult.Fail<PhotoshopTarget>(OperationFailure.Create(
                FailureCode.PhotoshopUnknownState,
                $"Expected exactly one accepted Photoshop process before preparation; observed " +
                $"{processes.Value.Count}. Nothing further was sent.",
                isRetryable: true,
                context: new Dictionary<string, string>
                {
                    ["processCandidates"] = processes.Value.Count.ToString(CultureInfo.InvariantCulture),
                    ["mutationInvoked"] = "false",
                }));
        }

        ExternalProcessRef process = processes.Value[0];
        if (process.ProcessId != opened.Target.Process.ProcessId ||
            process.StartedUtc != opened.Target.Process.StartedUtc ||
            !string.Equals(
                Path.GetFullPath(process.ExecutablePath),
                Path.GetFullPath(opened.Target.Process.ExecutablePath),
                StringComparison.OrdinalIgnoreCase) ||
            !_locator.IsAlive(opened.Target.Process))
        {
            return OperationResult.Fail<PhotoshopTarget>(TargetLost(
                "The accepted Photoshop PID/start-time identity no longer matches the process that " +
                "opened the managed document."));
        }

        OperationResult<IReadOnlyList<ExternalWindowRef>> windows =
            _locator.FindTopLevelWindows(process);
        if (windows.IsFailure)
        {
            return OperationResult.Fail<PhotoshopTarget>(AsPhotoshop(windows.Failure));
        }

        List<ExternalWindowRef> accepted =
        [
            .. windows.Value.Where(w =>
                w.OwningProcessId == process.ProcessId &&
                w.IsVisible &&
                string.Equals(w.ClassName, baseline.MainWindowClassName, StringComparison.Ordinal)),
        ];
        if (accepted.Count != 1 || accepted[0].Handle != opened.Target.Window.Handle)
        {
            return OperationResult.Fail<PhotoshopTarget>(TargetLost(
                "The exact accepted Photoshop main window ownership/class could not be re-established."));
        }

        PhotoshopTarget refreshed = new(process, accepted[0]);
        OperationResult<IReadOnlyList<ExternalWindowRef>> dialogs =
            _locator.FindOwnedDialogs(process, accepted[0]);
        if (dialogs.IsFailure)
        {
            return OperationResult.Fail<PhotoshopTarget>(AsPhotoshop(dialogs.Failure));
        }

        int titledVisibleDialogs = dialogs.Value.Count(d =>
            d.IsVisible && !string.IsNullOrWhiteSpace(d.Title));
        if (!accepted[0].IsEnabled || titledVisibleDialogs > 0)
        {
            return OperationResult.Fail<PhotoshopTarget>(OperationFailure.Create(
                FailureCode.PhotoshopBlockingDialog,
                "A dialog owned by Photoshop is blocking preparation. It was not dismissed and no " +
                "later input was produced.",
                context: new Dictionary<string, string>
                {
                    ["ownedDialogCount"] = titledVisibleDialogs.ToString(CultureInfo.InvariantCulture),
                    ["mutationInvoked"] = "false",
                }));
        }

        string expectedPath = Path.GetFullPath(opened.Identity.ObservedFullPath);
        string expectedName = Path.GetFileName(expectedPath);
        OperationResult<PhotoshopStateSnapshot> state = await _driver
            .InspectStateAsync(refreshed, expectedName, cancellationToken).ConfigureAwait(false);
        if (state.IsFailure)
        {
            return OperationResult.Fail<PhotoshopTarget>(state.Failure);
        }

        if (state.Value.State is PhotoshopStartingState.KnownModal)
        {
            return OperationResult.Fail<PhotoshopTarget>(OperationFailure.Create(
                FailureCode.PhotoshopBlockingDialog,
                "Photoshop reported a blocking modal. It was not dismissed and preparation stopped.",
                context: new Dictionary<string, string> { ["mutationInvoked"] = "false" }));
        }

        if (state.Value.State is not (PhotoshopStartingState.KnownEditorWithExpectedDocument or
                                     PhotoshopStartingState.KnownEditorWithOtherDocument) ||
            !state.Value.Observation.WindowTitle.StartsWith(expectedName, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail<PhotoshopTarget>(OperationFailure.Create(
                FailureCode.PhotoshopUnknownState,
                "Photoshop is not showing the expected managed document in a recognised editor state. " +
                "Nothing further was sent.",
                isRetryable: true,
                context: new Dictionary<string, string>
                {
                    ["state"] = state.Value.State.ToString(),
                    ["mutationInvoked"] = "false",
                }));
        }

        if (!probeDocumentIdentity)
        {
            // After CMYK + spot-channel creation Photoshop's Save As surface may propose PSD
            // instead of the original PNG, so that Part A UI probe no longer represents the
            // loaded document's original fullName. The fixed native W1 result supplies and the
            // caller validates app.activeDocument.fullName.fsName instead.
            return OperationResult.Ok(refreshed);
        }

        OperationResult<PhotoshopDocumentIdentity> identity = await _driver
            .ProbeDocumentIdentityAsync(refreshed, cancellationToken).ConfigureAwait(false);
        if (identity.IsFailure)
        {
            return OperationResult.Fail<PhotoshopTarget>(identity.Failure);
        }

        if (!PhotoshopDocumentIdentityRule.MatchesExpectedDocument(
                expectedPath, identity.Value.ObservedFullPath))
        {
            return OperationResult.Fail<PhotoshopTarget>(OperationFailure.Create(
                FailureCode.PhotoshopDocumentIdentityUnconfirmed,
                "The active Photoshop document is not the exact managed Working document. Nothing " +
                "further was sent.",
                context: new Dictionary<string, string>
                {
                    ["expectedDocument"] = expectedPath,
                    ["observedDocument"] = identity.Value.ObservedFullPath,
                    ["mutationInvoked"] = "false",
                }));
        }

        return OperationResult.Ok(refreshed);
    }

    private static OperationResult<PreparedOperation> BuildOperation(PhotoshopPreparation preparation)
    {
        ResizeDirection direction = preparation switch
        {
            FitWithinBoundsPreparation fit => fit.Plan.Mode switch
            {
                PrintPreparationMode.ResolutionOnly => ResizeDirection.ResolutionOnly,
                PrintPreparationMode.ProportionalShrink => ResizeDirection.Shrink,
                _ => (ResizeDirection)(-1),
            },
            TargetEdgePreparation target => target.Plan.Projection.Direction,
            _ => (ResizeDirection)(-1),
        };

        PhotoshopResizeMode expectedPolicy = direction switch
        {
            ResizeDirection.ResolutionOnly => PhotoshopResizeMode.None,
            ResizeDirection.Shrink => PhotoshopResizeMode.BicubicSharper,
            ResizeDirection.Enlarge => PhotoshopResizeMode.PreserveDetails,
            _ => (PhotoshopResizeMode)(-1),
        };
        if (preparation.ResizePolicy != expectedPolicy)
        {
            return OperationResult.Fail<PreparedOperation>(OperationFailure.Create(
                FailureCode.PreconditionNotMet,
                "The immutable preparation's direction and neutral resize policy disagree. Nothing " +
                "was changed.",
                context: new Dictionary<string, string> { ["mutationInvoked"] = "false" }));
        }

        OperationResult<PhotoshopNativeResampleMethod> mapped =
            PhotoshopResizePolicyMapping.Map(preparation.ResizePolicy);
        if (mapped.IsFailure)
        {
            return OperationResult.Fail<PreparedOperation>(mapped.Failure);
        }

        LimitingEdge edge = direction == ResizeDirection.ResolutionOnly
            ? LimitingEdge.None
            : preparation.PhotoshopEdge;
        double? millimetres = direction == ResizeDirection.ResolutionOnly
            ? null
            : preparation.PhotoshopEdgeValueMm;

        if (preparation.ProductionDpi != 300 ||
            direction is not (ResizeDirection.ResolutionOnly or ResizeDirection.Shrink or ResizeDirection.Enlarge) ||
            (direction == ResizeDirection.ResolutionOnly && (edge != LimitingEdge.None || millimetres is not null)) ||
            (direction != ResizeDirection.ResolutionOnly &&
             (edge is not (LimitingEdge.Width or LimitingEdge.Height) ||
              millimetres is not { } mm || !double.IsFinite(mm) || mm <= 0)))
        {
            return OperationResult.Fail<PreparedOperation>(OperationFailure.Create(
                FailureCode.PreconditionNotMet,
                "The immutable Photoshop preparation is not one of the closed one-edge/300-PPI " +
                "operations. Nothing was changed.",
                context: new Dictionary<string, string> { ["mutationInvoked"] = "false" }));
        }

        return OperationResult.Ok(new PreparedOperation(direction, edge, millimetres, mapped.Value));
    }

    private static OperationResult<Unit> ValidateReadBack(
        PhotoshopOpenedDocument opened,
        PhotoshopPreparation preparation,
        PreparedOperation operation,
        PhotoshopDocumentFacts before,
        PhotoshopDocumentFacts after)
    {
        string expectedPath = Path.GetFullPath(opened.Identity.ObservedFullPath);
        if (!SamePath(expectedPath, before.DocumentFullPath) ||
            !SamePath(expectedPath, after.DocumentFullPath))
        {
            return Invalid("Photoshop's native document path changed or did not match the managed Working path.");
        }

        if (before.PixelWidth != preparation.SourcePixelWidth ||
            before.PixelHeight != preparation.SourcePixelHeight)
        {
            return Invalid("Photoshop's pre-operation pixels did not match the immutable preparation source.");
        }

        if (before.W1Exists || after.W1Exists || HasSpot(before.Channels) || HasSpot(after.Channels))
        {
            return Invalid("W1 or another spot channel exists in the pre-W1 preparation document.");
        }

        if (!IsAcceptedRgb8(before) ||
            !string.Equals(before.ColourMode, after.ColourMode, StringComparison.Ordinal) ||
            !string.Equals(before.BitDepth, after.BitDepth, StringComparison.Ordinal) ||
            !before.Channels.SequenceEqual(after.Channels) ||
            before.DocumentCount != after.DocumentCount)
        {
            return Invalid("Colour mode, bit depth, channels or shared-process document count changed.");
        }

        if (after.PixelWidth != preparation.ProjectedPixelWidth ||
            after.PixelHeight != preparation.ProjectedPixelHeight)
        {
            return Invalid(
                $"Photoshop returned {after.PixelWidth}×{after.PixelHeight} px, not the immutable " +
                $"projected {preparation.ProjectedPixelWidth}×{preparation.ProjectedPixelHeight} px.");
        }

        if (Math.Abs(after.ResolutionPpi - preparation.ProductionDpi) > ResolutionTolerancePpi)
        {
            return Invalid(
                $"Photoshop reported {after.ResolutionPpi:R} PPI, not the required 300 PPI.");
        }

        if (operation.Direction == ResizeDirection.ResolutionOnly)
        {
            if (after.PixelWidth != before.PixelWidth || after.PixelHeight != before.PixelHeight)
            {
                return Invalid("Resolution-only preparation changed pixel dimensions.");
            }
        }
        else
        {
            double requested = operation.EdgeMillimetres!.Value;
            double actual = operation.CommandedEdge == LimitingEdge.Width
                ? after.PhysicalWidthMm
                : after.PhysicalHeightMm;
            // One half of one 300-PPI pixel is the exact physical representation boundary when
            // Photoshop materialises an integer pixel edge; the epsilon covers only double/API noise.
            double physicalTolerance = (25.4 / preparation.ProductionDpi / 2.0) +
                PhysicalRepresentationEpsilonMm;
            if (Math.Abs(actual - requested) > physicalTolerance)
            {
                return Invalid(
                    $"Photoshop's commanded physical edge is {actual:R} mm, not {requested:R} mm " +
                    "within one half-pixel of API representation.");
            }

            long proportionalError = operation.CommandedEdge == LimitingEdge.Width
                ? Math.Abs((long)after.PixelHeight * before.PixelWidth -
                           (long)before.PixelHeight * after.PixelWidth)
                : Math.Abs((long)after.PixelWidth * before.PixelHeight -
                           (long)before.PixelWidth * after.PixelHeight);
            long roundingDenominator = operation.CommandedEdge == LimitingEdge.Width
                ? before.PixelWidth
                : before.PixelHeight;
            if (proportionalError * 2 > roundingDenominator)
            {
                return Invalid("Photoshop's returned integer pixels do not preserve source proportions.");
            }
        }

        return OperationResult.Ok();
    }

    private static bool IsAcceptedRgb8(PhotoshopDocumentFacts facts) =>
        string.Equals(facts.ColourMode, "DocumentMode.RGB", StringComparison.Ordinal) &&
        string.Equals(facts.BitDepth, "BitsPerChannelType.EIGHT", StringComparison.Ordinal) &&
        facts.Channels.Count(c => string.Equals(
            c.Type, "ChannelType.COMPONENT", StringComparison.Ordinal)) == 3;

    private static bool HasSpot(ImmutableArray<PhotoshopChannelFact> channels) =>
        channels.Any(c => string.Equals(c.Type, "ChannelType.SPOTCOLOR", StringComparison.Ordinal));

    private static bool SamePath(string expected, string actual)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(expected), Path.GetFullPath(actual), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static OperationResult<Sha256> Hash(string path)
    {
        try
        {
            using FileStream stream = new(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: false);
            return OperationResult.Ok(Sha256.FromBytes(SHA256.HashData(stream)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail<Sha256>(OperationFailure.Create(
                FailureCode.OutputUnreadable,
                $"The managed Working backing file could not be hashed: {ex.Message}"));
        }
    }

    private static OperationResult<ImmutableArray<string>> SnapshotDirectory(string backingPath)
    {
        try
        {
            string directory = Path.GetDirectoryName(backingPath)!;
            return OperationResult.Ok(Directory
                .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFullPath)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail<ImmutableArray<string>>(OperationFailure.Create(
                FailureCode.WorkspaceError,
                $"The controlled Working directory could not be observed: {ex.Message}"));
        }
    }

    private static OperationResult<Unit> Invalid(string detail) =>
        OperationResult.Fail<Unit>(OperationFailure.Create(
            FailureCode.OutputValidationFailed,
            detail + " No corrective resize, retry, save, W1 action or later operation was attempted.",
            context: new Dictionary<string, string>
            {
                ["automaticRetry"] = "false",
                ["saved"] = "false",
                ["w1ActionInvoked"] = "false",
            }));

    private static OperationResult<PhotoshopPreparedDocument> CancelledBeforeMutation(
        string path, Sha256? hash = null)
    {
        Dictionary<string, string> context = new(NoMutationContext(path));
        if (hash is { } observed)
        {
            context["backingSha256"] = observed.ToString();
        }

        return OperationResult.Fail<PhotoshopPreparedDocument>(OperationFailure.Create(
            FailureCode.Cancelled,
            "Photoshop preparation was cancelled before the native call. No mutation occurred and " +
            "nothing was saved.",
            context: context));
    }

    private static OperationResult<PhotoshopPreparedDocument> RetainedFailure(
        OperationFailure failure, bool cancellationRequested)
    {
        Dictionary<string, string> context = new(failure.Context, StringComparer.Ordinal)
        {
            ["inMemoryPreparedMayBeRetained"] = "true",
            ["saved"] = "false",
            ["w1ActionInvoked"] = "false",
            ["adapterOutputCreated"] = "false",
            ["revisionCreated"] = "false",
            ["automaticRetry"] = "false",
        };
        if (cancellationRequested)
        {
            context["cancellationRequestedAfterNativeCall"] = "true";
        }

        return OperationResult.Fail<PhotoshopPreparedDocument>(failure with { Context = context });
    }

    private static Dictionary<string, string> ResultContext(PhotoshopPreparedDocument result) => new()
    {
        ["inMemoryPreparedMayBeRetained"] = "true",
        ["actualPixelWidth"] = result.Actual.PixelWidth.ToString(CultureInfo.InvariantCulture),
        ["actualPixelHeight"] = result.Actual.PixelHeight.ToString(CultureInfo.InvariantCulture),
        ["actualResolutionPpi"] = result.Actual.ResolutionPpi.ToString("R", CultureInfo.InvariantCulture),
        ["backingSha256"] = result.BackingWorkingSha256.ToString(),
        ["saved"] = "false",
        ["w1ActionInvoked"] = "false",
        ["adapterOutputCreated"] = "false",
        ["revisionCreated"] = "false",
    };

    private static IReadOnlyDictionary<string, string> NoMutationContext(string path) =>
        new Dictionary<string, string>
        {
            ["document"] = path,
            ["mutationInvoked"] = "false",
            ["saved"] = "false",
            ["w1ActionInvoked"] = "false",
        };

    private static OperationFailure TargetLost(string detail) => OperationFailure.Create(
        FailureCode.PhotoshopTargetLost,
        detail + " No document was substituted and no further input was produced.",
        isRetryable: true,
        context: new Dictionary<string, string> { ["mutationInvoked"] = "false" });

    private static OperationFailure AsPhotoshop(OperationFailure failure)
    {
        FailureCode code = failure.Code switch
        {
            FailureCode.MeituTargetLost => FailureCode.PhotoshopTargetLost,
            FailureCode.MeituUnknownState => FailureCode.PhotoshopUnknownState,
            FailureCode.MeituBlockingDialog => FailureCode.PhotoshopBlockingDialog,
            _ => failure.Code,
        };
        return code == failure.Code
            ? failure
            : failure with { Code = code, MessageKey = $"Failure_{code}" };
    }

    private sealed record PreparedOperation(
        ResizeDirection Direction,
        LimitingEdge CommandedEdge,
        double? EdgeMillimetres,
        PhotoshopNativeResampleMethod NativeMethod);
}
