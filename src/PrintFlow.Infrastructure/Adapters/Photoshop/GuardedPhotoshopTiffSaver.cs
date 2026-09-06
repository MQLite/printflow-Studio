using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

internal interface IPhotoshopTiffFileProbe
{
    PhotoshopTiffSettleObservation Probe(string absolutePath, TimeSpan elapsed);
}

internal sealed class FileSystemPhotoshopTiffFileProbe : IPhotoshopTiffFileProbe
{
    public PhotoshopTiffSettleObservation Probe(string absolutePath, TimeSpan elapsed)
    {
        try
        {
            FileInfo info = new(absolutePath);
            if (!info.Exists) return new(elapsed, false, 0, 0, false);
            long length = info.Length;
            long read = 0;
            using FileStream stream = new(
                absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: false);
            byte[] buffer = new byte[1 << 20];
            int count;
            while ((count = stream.Read(buffer, 0, buffer.Length)) != 0) read += count;
            return new(elapsed, true, length, read, read == length && stream.Length == length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            bool exists = File.Exists(absolutePath);
            return new(elapsed, exists, exists ? SafeLength(absolutePath) : 0, 0, false);
        }
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }
}

internal static class PhotoshopTiffSettleRule
{
    internal const int RequiredConsecutiveObservations = 3;

    internal static bool IsSettled(IReadOnlyList<PhotoshopTiffSettleObservation> observations)
    {
        if (observations.Count < RequiredConsecutiveObservations) return false;
        PhotoshopTiffSettleObservation last = observations[^1];
        if (!last.Exists || last.ByteLength <= 0 || !last.CompletelyReadable ||
            last.BytesRead != last.ByteLength) return false;
        for (int index = observations.Count - RequiredConsecutiveObservations;
             index < observations.Count; index++)
        {
            PhotoshopTiffSettleObservation current = observations[index];
            if (!current.Exists || current.ByteLength != last.ByteLength) return false;
        }
        return true;
    }
}

/// <summary>Guards the one fixed TIFF save and turns only fully checked bytes into a C1 candidate.</summary>
internal sealed class GuardedPhotoshopTiffSaver
{
    private const double ResolutionTolerancePpi = 0.0000001;
    private readonly IPhotoshopBaselineProvider _baselines;
    private readonly GuardedPhotoshopDocumentPreparer _targetGuard;
    private readonly IPhotoshopTiffNativeBridge _native;
    private readonly IProductionTiffInspector _inspector;
    private readonly IPhotoshopTiffFileProbe _probe;
    private readonly IWorkspace _workspace;
    private readonly PhotoshopAutomationOptions _options;
    private readonly TimeProvider _clock;

    internal GuardedPhotoshopTiffSaver(
        IPhotoshopBaselineProvider baselines,
        GuardedPhotoshopDocumentPreparer targetGuard,
        IPhotoshopTiffNativeBridge native,
        IProductionTiffInspector inspector,
        IPhotoshopTiffFileProbe probe,
        IWorkspace workspace,
        PhotoshopAutomationOptions options,
        TimeProvider clock)
    {
        _baselines = baselines ?? throw new ArgumentNullException(nameof(baselines));
        _targetGuard = targetGuard ?? throw new ArgumentNullException(nameof(targetGuard));
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    internal async Task<OperationResult<PhotoshopValidatedTiffCandidate>> SaveProductionTiffAsync(
        PhotoshopOpenedDocument opened,
        PhotoshopW1PreparedDocument prepared,
        WorkspaceFileRef expectedOutput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(opened);
        ArgumentNullException.ThrowIfNull(prepared);

        if (cancellationToken.IsCancellationRequested)
        {
            return CancelledBeforeSave(prepared.DocumentFullPath, expectedOutput);
        }
        if (expectedOutput.Area != WorkspaceArea.Working)
        {
            return Refused(
                $"Photoshop TIFF output must be in Working, not {expectedOutput.Area}.", expectedOutput);
        }
        if (!string.Equals(Path.GetExtension(expectedOutput.FileName), ".tif",
                StringComparison.OrdinalIgnoreCase))
        {
            return Refused("The supplied production output name is not the already-rendered .tif name.",
                expectedOutput);
        }

        string backingPath;
        string outputPath;
        try
        {
            backingPath = Path.GetFullPath(prepared.DocumentFullPath);
            outputPath = Path.GetFullPath(_workspace.ResolveAbsolute(expectedOutput));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Refused($"The managed TIFF destination could not be resolved: {ex.Message}", expectedOutput);
        }
        if (!SamePath(Path.GetDirectoryName(backingPath)!, Path.GetDirectoryName(outputPath)!))
        {
            return Refused(
                "The TIFF is not beside the B1B backing file in the same attempt-specific Working directory.",
                expectedOutput);
        }
        if (File.Exists(outputPath))
        {
            return Refused("The exact Working TIFF destination already exists; it was not overwritten or deleted.",
                expectedOutput);
        }

        OperationResult<Unit> preparedValidation = ValidatePrepared(opened, prepared);
        if (preparedValidation.IsFailure)
        {
            return OperationResult.Fail<PhotoshopValidatedTiffCandidate>(preparedValidation.Failure);
        }
        OperationResult<PhotoshopBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<PhotoshopValidatedTiffCandidate>(baseline.Failure);
        }
        OperationResult<PhotoshopTarget> guarded = await _targetGuard
            .VerifyExactMutationTargetAsync(
                opened, baseline.Value, cancellationToken, probeDocumentIdentity: false)
            .ConfigureAwait(false);
        if (guarded.IsFailure)
        {
            return OperationResult.Fail<PhotoshopValidatedTiffCandidate>(guarded.Failure);
        }

        OperationResult<Sha256> backingBefore = Hash(backingPath, "managed Working backing file");
        if (backingBefore.IsFailure)
        {
            return OperationResult.Fail<PhotoshopValidatedTiffCandidate>(backingBefore.Failure);
        }
        if (!backingBefore.Value.Equals(prepared.BackingWorkingSha256))
        {
            return OperationResult.Fail<PhotoshopValidatedTiffCandidate>(OperationFailure.Create(
                FailureCode.RevisionIntegrityMismatch,
                "The managed Working backing bytes changed after B1B. No TIFF save was invoked.",
                context: new Dictionary<string, string>(NoSaveContext(expectedOutput))
                {
                    ["expectedSha256"] = prepared.BackingWorkingSha256.ToString(),
                    ["actualSha256"] = backingBefore.Value.ToString(),
                }));
        }
        OperationResult<ImmutableArray<string>> filesBefore = SnapshotDirectory(backingPath);
        if (filesBefore.IsFailure)
        {
            return OperationResult.Fail<PhotoshopValidatedTiffCandidate>(filesBefore.Failure);
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return CancelledBeforeSave(backingPath, expectedOutput);
        }

        DateTimeOffset started = _clock.GetUtcNow();
        OperationResult<PhotoshopNativeTiffOutcome> native = _native.SaveOnce(
            new PhotoshopNativeTiffCommand(
                backingPath, outputPath, prepared.PixelWidth, prepared.PixelHeight, prepared.ResolutionPpi),
            baseline.Value);
        bool cancellationAfterSaveBegan = cancellationToken.IsCancellationRequested;
        if (native.IsFailure)
        {
            return Retained(native.Failure, cancellationAfterSaveBegan);
        }
        PhotoshopNativeTiffOutcome observed = native.Value;
        if (!observed.Succeeded || observed.SaveInvocationCount != 1 || observed.Before is null ||
            observed.After is null || observed.W1Before is null || !observed.OutputExists ||
            !observed.AcceptedSettingsObserved)
        {
            OperationFailure failure = OperationFailure.Create(
                observed.SaveInvocationCount == 0
                    ? FailureCode.PreconditionNotMet
                    : FailureCode.PhotoshopUnknownState,
                observed.FailureDetail ?? "Photoshop did not return a complete factual TIFF-save result.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["saveInvocationCount"] = observed.SaveInvocationCount.ToString(CultureInfo.InvariantCulture),
                    ["outputExists"] = observed.OutputExists ? "true" : "false",
                    ["automaticRetry"] = "false",
                    ["validatedCandidateCreated"] = "false",
                });
            return observed.SaveInvocationCount == 0
                ? OperationResult.Fail<PhotoshopValidatedTiffCandidate>(failure)
                : Retained(failure, cancellationAfterSaveBegan);
        }
        OperationResult<Unit> nativeFacts = ValidateNativeFacts(prepared, observed);
        if (nativeFacts.IsFailure)
        {
            return Retained(nativeFacts.Failure, cancellationAfterSaveBegan);
        }

        OperationResult<ImmutableArray<PhotoshopTiffSettleObservation>> settled =
            await SettleAsync(outputPath, started).ConfigureAwait(false);
        cancellationAfterSaveBegan |= cancellationToken.IsCancellationRequested;
        if (settled.IsFailure)
        {
            return Retained(settled.Failure, cancellationAfterSaveBegan);
        }

        OperationResult<ProductionTiffFacts> inspected = _inspector.Inspect(outputPath);
        if (inspected.IsFailure)
        {
            return Retained(inspected.Failure, cancellationAfterSaveBegan);
        }
        if (inspected.Value.PixelWidth != prepared.PixelWidth ||
            inspected.Value.PixelHeight != prepared.PixelHeight ||
            Math.Abs(inspected.Value.XResolutionDpi - prepared.ResolutionPpi) > ResolutionTolerancePpi ||
            Math.Abs(inspected.Value.YResolutionDpi - prepared.ResolutionPpi) > ResolutionTolerancePpi)
        {
            return Retained(OperationFailure.Create(
                FailureCode.OutputValidationFailed,
                "The on-disk TIFF geometry does not exactly match the factual B1B prepared document."),
                cancellationAfterSaveBegan);
        }

        OperationResult<Sha256> backingAfter = Hash(backingPath, "managed Working backing file");
        OperationResult<Sha256> outputHash = Hash(outputPath, "settled production TIFF");
        OperationResult<ImmutableArray<string>> filesAfter = SnapshotDirectory(backingPath);
        if (backingAfter.IsFailure) return Retained(backingAfter.Failure, cancellationAfterSaveBegan);
        if (outputHash.IsFailure) return Retained(outputHash.Failure, cancellationAfterSaveBegan);
        if (filesAfter.IsFailure) return Retained(filesAfter.Failure, cancellationAfterSaveBegan);
        if (!backingAfter.Value.Equals(prepared.BackingWorkingSha256))
        {
            return Retained(OperationFailure.Create(
                FailureCode.OutputValidationFailed,
                "Saving the TIFF copy changed the original managed Working backing bytes.",
                context: new Dictionary<string, string>
                {
                    ["expectedSha256"] = prepared.BackingWorkingSha256.ToString(),
                    ["actualSha256"] = backingAfter.Value.ToString(),
                }), cancellationAfterSaveBegan);
        }
        if (!ExactlyOneExpectedAdded(filesBefore.Value, filesAfter.Value, outputPath))
        {
            return Retained(OperationFailure.Create(
                FailureCode.OutputValidationFailed,
                "The final Working directory file set is not exactly the prior set plus one expected TIFF.",
                context: new Dictionary<string, string>
                {
                    ["filesBefore"] = filesBefore.Value.Length.ToString(CultureInfo.InvariantCulture),
                    ["filesAfter"] = filesAfter.Value.Length.ToString(CultureInfo.InvariantCulture),
                }), cancellationAfterSaveBegan);
        }

        OperationResult<PhotoshopTarget> afterGuard = await _targetGuard
            .VerifyExactMutationTargetAsync(
                opened with { Target = guarded.Value }, baseline.Value, CancellationToken.None,
                probeDocumentIdentity: false)
            .ConfigureAwait(false);
        if (afterGuard.IsFailure)
        {
            return Retained(afterGuard.Failure with
            {
                Context = new Dictionary<string, string>(afterGuard.Failure.Context)
                {
                    ["independentlyValidatedTiffSha256"] = outputHash.Value.ToString(),
                    ["targetOwnershipLostAfterSave"] = "true",
                },
            }, cancellationAfterSaveBegan);
        }

        TimeSpan elapsed = _clock.GetUtcNow() - started;
        FileInfo outputInfo = new(outputPath);
        PhotoshopValidatedTiffCandidate candidate = new(
            expectedOutput,
            outputHash.Value,
            outputInfo.Length,
            inspected.Value,
            PhotoshopProductionTiffSaveSettings.Accepted,
            backingAfter.Value,
            observed.Before.DocumentFullPath,
            observed.After.DocumentFullPath,
            elapsed,
            settled.Value,
            inspected.Value.ValidationLimitations);

        if (cancellationAfterSaveBegan)
        {
            return OperationResult.Fail<PhotoshopValidatedTiffCandidate>(OperationFailure.Create(
                FailureCode.Cancelled,
                "Cancellation was requested after the synchronous save began. The retained Working TIFF " +
                "validated factually, but cancellation remains authoritative and no workflow success exists.",
                context: new Dictionary<string, string>
                {
                    ["retainedTiff"] = expectedOutput.RelativePath,
                    ["retainedTiffSha256"] = candidate.Sha256.ToString(),
                    ["validatedCandidateCommitted"] = "false",
                    ["adapterOutputCreated"] = "false",
                    ["revisionCreated"] = "false",
                }));
        }
        return OperationResult.Ok(candidate);
    }

    private async Task<OperationResult<ImmutableArray<PhotoshopTiffSettleObservation>>> SettleAsync(
        string path, DateTimeOffset started)
    {
        DateTimeOffset deadline = started + _options.TiffSettleTimeout;
        List<PhotoshopTiffSettleObservation> observations = [];
        while (true)
        {
            observations.Add(_probe.Probe(path, _clock.GetUtcNow() - started));
            if (PhotoshopTiffSettleRule.IsSettled(observations))
            {
                return OperationResult.Ok(observations.ToImmutableArray());
            }
            if (_clock.GetUtcNow() >= deadline)
            {
                PhotoshopTiffSettleObservation last = observations[^1];
                return OperationResult.Fail<ImmutableArray<PhotoshopTiffSettleObservation>>(
                    OperationFailure.Create(
                        last.Exists ? FailureCode.OutputUnreadable : FailureCode.OutputMissing,
                        "The once-invoked production TIFF did not reach three equal non-zero lengths " +
                        "with a complete readable pass before the bounded settle timeout.",
                        isRetryable: false,
                        context: new Dictionary<string, string>
                        {
                            ["observationCount"] = observations.Count.ToString(CultureInfo.InvariantCulture),
                            ["lastLength"] = last.ByteLength.ToString(CultureInfo.InvariantCulture),
                            ["lastBytesRead"] = last.BytesRead.ToString(CultureInfo.InvariantCulture),
                            ["automaticRetry"] = "false",
                        }));
            }
            await Task.Delay(_options.PollInterval, _clock, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static OperationResult<Unit> ValidatePrepared(
        PhotoshopOpenedDocument opened, PhotoshopW1PreparedDocument prepared)
    {
        if (!SamePath(opened.Identity.ObservedFullPath, prepared.DocumentFullPath) ||
            prepared.PixelWidth <= 0 || prepared.PixelHeight <= 0 ||
            Math.Abs(prepared.ResolutionPpi - 300) > ResolutionTolerancePpi ||
            !string.Equals(prepared.ColourMode, "DocumentMode.CMYK", StringComparison.Ordinal) ||
            !string.Equals(prepared.BitDepth, "BitsPerChannelType.EIGHT", StringComparison.Ordinal) ||
            prepared.ProcessChannels.Length != 4 || prepared.ProcessChannels.Any(channel =>
                !string.Equals(channel.Type, "ChannelType.COMPONENT", StringComparison.Ordinal)) ||
            !string.Equals(prepared.W1.Name, "W1", StringComparison.Ordinal) ||
            !string.Equals(prepared.W1.Type, "ChannelType.SPOTCOLOR", StringComparison.Ordinal) ||
            !prepared.W1.IsNonEmpty || prepared.W1.NonWhitePixelCount <= 0 ||
            !HasEstablishedProvenance(prepared.Provenance))
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.PreconditionNotMet,
                "Only the exact factual B1B CMYK/8, four-component + non-empty W1 result may be saved.",
                context: new Dictionary<string, string>
                {
                    ["saveInvocationCount"] = "0",
                    ["validatedCandidateCreated"] = "false",
                }));
        }
        return OperationResult.Ok();
    }

    /// <summary>
    /// Whether the document's white ink has an origin that was positively established, rather than
    /// merely being present.
    /// </summary>
    /// <remarks>
    /// The saver's job here has never been to check that the Action ran — the structural facts
    /// above are what prove the document is production-shaped, and the native bridge and the TIFF
    /// inspector beneath this method refer to the Action nowhere at all. Its job is to refuse a
    /// document whose W1 nobody has accounted for. Both accepted origins are named explicitly, so
    /// adding a third would have to be a deliberate edit here rather than a silent consequence of
    /// a new caller.
    /// </remarks>
    private static bool HasEstablishedProvenance(PhotoshopWhiteInkProvenance provenance) =>
        provenance switch
        {
            PhotoshopWhiteInkProvenance.Generated generated =>
                !string.IsNullOrWhiteSpace(generated.ActionSetName) &&
                !string.IsNullOrWhiteSpace(generated.ActionName),
            PhotoshopWhiteInkProvenance.Retained retained =>
                !string.IsNullOrWhiteSpace(retained.CarrierDocumentFullPath),
            _ => false,
        };

    private static OperationResult<Unit> ValidateNativeFacts(
        PhotoshopW1PreparedDocument prepared, PhotoshopNativeTiffOutcome outcome)
    {
        PhotoshopDocumentFacts before = outcome.Before!;
        PhotoshopDocumentFacts after = outcome.After!;
        PhotoshopW1ChannelFacts w1 = outcome.W1Before!;
        if (!SamePath(prepared.DocumentFullPath, before.DocumentFullPath) ||
            !SamePath(prepared.DocumentFullPath, after.DocumentFullPath) ||
            before.PixelWidth != prepared.PixelWidth || before.PixelHeight != prepared.PixelHeight ||
            after.PixelWidth != prepared.PixelWidth || after.PixelHeight != prepared.PixelHeight ||
            Math.Abs(before.ResolutionPpi - prepared.ResolutionPpi) > ResolutionTolerancePpi ||
            Math.Abs(after.ResolutionPpi - prepared.ResolutionPpi) > ResolutionTolerancePpi ||
            before.DocumentCount != after.DocumentCount ||
            !string.Equals(w1.Name, "W1", StringComparison.Ordinal) ||
            !string.Equals(w1.Type, "ChannelType.SPOTCOLOR", StringComparison.Ordinal) ||
            !w1.IsNonEmpty || w1.NonWhitePixelCount <= 0)
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.OutputValidationFailed,
                "Photoshop's immediate save read-back did not preserve document identity, geometry, " +
                "document count, or the non-empty W1 spot state."));
        }
        return OperationResult.Ok();
    }

    private static bool ExactlyOneExpectedAdded(
        ImmutableArray<string> before, ImmutableArray<string> after, string expected)
    {
        HashSet<string> expectedSet = new(before, StringComparer.OrdinalIgnoreCase) { expected };
        return after.Length == before.Length + 1 && expectedSet.SetEquals(after);
    }

    private static OperationResult<Sha256> Hash(string path, string description)
    {
        try
        {
            using FileStream stream = new(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: false);
            return OperationResult.Ok(Sha256.FromBytes(SHA256.HashData(stream)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail<Sha256>(FailureCode.OutputUnreadable,
                $"The {description} could not be read completely: {ex.Message}");
        }
    }

    private static OperationResult<ImmutableArray<string>> SnapshotDirectory(string backingPath)
    {
        try
        {
            return OperationResult.Ok(Directory.EnumerateFiles(
                    Path.GetDirectoryName(backingPath)!, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFullPath)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail<ImmutableArray<string>>(FailureCode.WorkspaceError,
                $"The attempt-specific Working directory could not be observed: {ex.Message}");
        }
    }

    private static bool SamePath(string first, string second)
    {
        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static OperationResult<PhotoshopValidatedTiffCandidate> Refused(
        string detail, WorkspaceFileRef output) =>
        OperationResult.Fail<PhotoshopValidatedTiffCandidate>(OperationFailure.Create(
            FailureCode.PreconditionNotMet, detail + " No save was invoked.",
            context: NoSaveContext(output)));

    private static OperationResult<PhotoshopValidatedTiffCandidate> CancelledBeforeSave(
        string document, WorkspaceFileRef output) =>
        OperationResult.Fail<PhotoshopValidatedTiffCandidate>(OperationFailure.Create(
            FailureCode.Cancelled,
            "Production TIFF save was cancelled before the one synchronous save call. No file was written.",
            context: new Dictionary<string, string>(NoSaveContext(output))
            {
                ["document"] = document,
            }));

    private static OperationResult<PhotoshopValidatedTiffCandidate> Retained(
        OperationFailure failure, bool cancellationRequested)
    {
        Dictionary<string, string> context = new(failure.Context)
        {
            ["workingArtefactMayBeRetained"] = "true",
            ["automaticRetry"] = "false",
            ["validatedCandidateCreated"] = "false",
            ["adapterOutputCreated"] = "false",
            ["printOutputCreated"] = "false",
            ["revisionCreated"] = "false",
            ["reviewRequiredCreated"] = "false",
        };
        if (cancellationRequested) context["cancellationRequestedAfterSaveBegan"] = "true";
        return OperationResult.Fail<PhotoshopValidatedTiffCandidate>(failure with { Context = context });
    }

    private static IReadOnlyDictionary<string, string> NoSaveContext(WorkspaceFileRef output) =>
        new Dictionary<string, string>
        {
            ["expectedOutput"] = output.RelativePath,
            ["saveInvocationCount"] = "0",
            ["automaticRetry"] = "false",
            ["validatedCandidateCreated"] = "false",
            ["adapterOutputCreated"] = "false",
            ["revisionCreated"] = "false",
        };
}
