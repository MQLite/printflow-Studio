using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>
/// Surrounds one fixed native W1 call with the accepted Part A/B1A.3 guards and factual checks.
/// </summary>
internal sealed class GuardedPhotoshopW1Executor
{
    private const double ResolutionTolerancePpi = 0.0000001;
    private const double PhysicalToleranceMm = 0.000001;

    private readonly IPhotoshopBaselineProvider _baselines;
    private readonly GuardedPhotoshopDocumentPreparer _targetGuard;
    private readonly IPhotoshopW1NativeBridge _native;

    internal GuardedPhotoshopW1Executor(
        IPhotoshopBaselineProvider baselines,
        GuardedPhotoshopDocumentPreparer targetGuard,
        IPhotoshopW1NativeBridge native)
    {
        _baselines = baselines ?? throw new ArgumentNullException(nameof(baselines));
        _targetGuard = targetGuard ?? throw new ArgumentNullException(nameof(targetGuard));
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    internal async Task<OperationResult<PhotoshopW1PreparedDocument>> ExecuteW1Async(
        PhotoshopOpenedDocument opened,
        PhotoshopPreparedDocument prepared,
        WhiteUnderbaseBranch branch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(opened);
        ArgumentNullException.ThrowIfNull(prepared);

        if (cancellationToken.IsCancellationRequested)
        {
            return CancelledBeforeAction(prepared.Actual.DocumentFullPath);
        }

        OperationResult<PhotoshopBaseline> baseline = _baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            return OperationResult.Fail<PhotoshopW1PreparedDocument>(baseline.Failure);
        }

        if (baseline.Value.W1Action is not { } contract)
        {
            return OperationResult.Fail<PhotoshopW1PreparedDocument>(OperationFailure.Create(
                FailureCode.EnvironmentNotVerified,
                "The verified preset carries no accepted CMYK + W1 runtime Action evidence. " +
                "No Action was invoked.",
                context: NoActionContext(prepared.Actual.DocumentFullPath)));
        }

        OperationResult<string> actionName = PhotoshopW1Program.ActionName(branch, contract);
        if (actionName.IsFailure)
        {
            return OperationResult.Fail<PhotoshopW1PreparedDocument>(actionName.Failure);
        }

        OperationResult<Unit> artifact = VerifyArtifact(contract);
        if (artifact.IsFailure)
        {
            return OperationResult.Fail<PhotoshopW1PreparedDocument>(artifact.Failure);
        }

        OperationResult<Unit> preparedState = ValidatePrepared(opened, prepared);
        if (preparedState.IsFailure)
        {
            return OperationResult.Fail<PhotoshopW1PreparedDocument>(preparedState.Failure);
        }

        OperationResult<PhotoshopTarget> guarded = await _targetGuard
            .VerifyExactMutationTargetAsync(opened, baseline.Value, cancellationToken)
            .ConfigureAwait(false);
        if (guarded.IsFailure)
        {
            return OperationResult.Fail<PhotoshopW1PreparedDocument>(guarded.Failure);
        }

        string backingPath = Path.GetFullPath(prepared.Actual.DocumentFullPath);
        OperationResult<Sha256> hashBefore = Hash(backingPath);
        if (hashBefore.IsFailure)
        {
            return OperationResult.Fail<PhotoshopW1PreparedDocument>(hashBefore.Failure);
        }

        if (!hashBefore.Value.Equals(prepared.BackingWorkingSha256))
        {
            return OperationResult.Fail<PhotoshopW1PreparedDocument>(OperationFailure.Create(
                FailureCode.RevisionIntegrityMismatch,
                "The managed Working backing bytes changed after preparation. No Action was invoked.",
                context: new Dictionary<string, string>(NoActionContext(backingPath))
                {
                    ["preparedSha256"] = prepared.BackingWorkingSha256.ToString(),
                    ["actualSha256"] = hashBefore.Value.ToString(),
                }));
        }

        OperationResult<ImmutableArray<string>> filesBefore = SnapshotDirectory(backingPath);
        if (filesBefore.IsFailure)
        {
            return OperationResult.Fail<PhotoshopW1PreparedDocument>(filesBefore.Failure);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return CancelledBeforeAction(backingPath, hashBefore.Value);
        }

        PhotoshopNativeW1Command command = new(
            backingPath,
            prepared.Actual.PixelWidth,
            prepared.Actual.PixelHeight,
            prepared.Actual.ResolutionPpi,
            branch,
            contract);
        OperationResult<PhotoshopNativeW1Outcome> native = _native.ExecuteOnce(command, baseline.Value);
        bool cancellationAfterAction = cancellationToken.IsCancellationRequested;

        OperationResult<Sha256> hashAfter = Hash(backingPath);
        OperationResult<ImmutableArray<string>> filesAfter = SnapshotDirectory(backingPath);
        if (hashAfter.IsFailure)
        {
            return Retained(hashAfter.Failure, cancellationAfterAction);
        }
        if (filesAfter.IsFailure)
        {
            return Retained(filesAfter.Failure, cancellationAfterAction);
        }
        if (!hashBefore.Value.Equals(hashAfter.Value))
        {
            return Retained(OperationFailure.Create(
                FailureCode.OutputValidationFailed,
                "The CMYK + W1 operation changed the managed Working backing bytes even though no " +
                "save was authorised.",
                context: new Dictionary<string, string>
                {
                    ["sha256Before"] = hashBefore.Value.ToString(),
                    ["sha256After"] = hashAfter.Value.ToString(),
                }), cancellationAfterAction);
        }
        if (!filesBefore.Value.SequenceEqual(filesAfter.Value, StringComparer.OrdinalIgnoreCase))
        {
            return Retained(OperationFailure.Create(
                FailureCode.OutputValidationFailed,
                "A file appeared or disappeared beside the managed Working document during the " +
                "in-memory CMYK + W1 operation.",
                context: new Dictionary<string, string>
                {
                    ["filesBefore"] = filesBefore.Value.Length.ToString(CultureInfo.InvariantCulture),
                    ["filesAfter"] = filesAfter.Value.Length.ToString(CultureInfo.InvariantCulture),
                }), cancellationAfterAction);
        }

        OperationResult<PhotoshopTarget> afterGuard = await _targetGuard
            .VerifyExactMutationTargetAsync(opened with { Target = guarded.Value }, baseline.Value,
                CancellationToken.None, probeDocumentIdentity: false)
            .ConfigureAwait(false);
        if (afterGuard.IsFailure)
        {
            return Retained(afterGuard.Failure, cancellationAfterAction);
        }

        if (native.IsFailure)
        {
            return Retained(native.Failure, cancellationAfterAction);
        }

        PhotoshopNativeW1Outcome observed = native.Value;
        if (!observed.Succeeded || observed.ActionInvocationCount != 1 ||
            observed.Before is null || observed.After is null || observed.W1 is null)
        {
            OperationFailure failure = OperationFailure.Create(
                observed.ActionInvocationCount == 0
                    ? FailureCode.PreconditionNotMet
                    : FailureCode.PhotoshopUnknownState,
                observed.FailureDetail ?? "Photoshop did not return a complete factual CMYK + W1 result.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["actionInvocationCount"] = observed.ActionInvocationCount.ToString(CultureInfo.InvariantCulture),
                    ["automaticRetry"] = "false",
                    ["saved"] = "false",
                });
            return observed.ActionInvocationCount == 0
                ? OperationResult.Fail<PhotoshopW1PreparedDocument>(failure)
                : Retained(failure, cancellationAfterAction);
        }

        OperationResult<Unit> validation = ValidateResult(prepared, observed.Before, observed.After, observed.W1);
        if (validation.IsFailure)
        {
            return Retained(validation.Failure, cancellationAfterAction);
        }

        PhotoshopW1PreparedDocument result = new(
            observed.After.DocumentFullPath,
            observed.After.PixelWidth,
            observed.After.PixelHeight,
            observed.After.ResolutionPpi,
            observed.After.PhysicalWidthMm,
            observed.After.PhysicalHeightMm,
            observed.After.ColourMode,
            observed.After.BitDepth,
            [.. observed.After.Channels.Where(IsComponent)],
            observed.W1,
            branch,
            contract.SetName,
            actionName.Value,
            prepared.OtherDocumentsMayBeOpen || observed.Before.DocumentCount > 1,
            hashAfter.Value,
            ActionInvocationOccurredExactlyOnce: true);

        if (cancellationAfterAction)
        {
            return OperationResult.Fail<PhotoshopW1PreparedDocument>(OperationFailure.Create(
                FailureCode.Cancelled,
                "Cancellation was requested after the synchronous W1 Action began. Factual " +
                "post-observation completed; the in-memory CMYK/W1 state may be retained. Nothing " +
                "was saved and no later operation ran.",
                context: ResultContext(result)));
        }

        return OperationResult.Ok(result);
    }

    private static OperationResult<Unit> VerifyArtifact(PhotoshopW1ActionContract contract)
    {
        try
        {
            string path = Path.GetFullPath(contract.ArtifactPath);
            if (!File.Exists(path))
            {
                return ArtifactFailure("The canonical W1 Action artifact is missing.", path);
            }

            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                1 << 20, useAsync: false);
            Sha256 actual = Sha256.FromBytes(SHA256.HashData(stream));
            return actual.Equals(contract.ArtifactSha256)
                ? OperationResult.Ok()
                : ArtifactFailure("The canonical W1 Action artifact SHA-256 changed.", path,
                    actual.ToString(), contract.ArtifactSha256.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return ArtifactFailure($"The canonical W1 Action artifact could not be verified: {ex.Message}",
                contract.ArtifactPath);
        }
    }

    private static OperationResult<Unit> ValidatePrepared(
        PhotoshopOpenedDocument opened, PhotoshopPreparedDocument prepared)
    {
        PhotoshopDocumentFacts facts = prepared.Actual;
        if (!SamePath(opened.Identity.ObservedFullPath, facts.DocumentFullPath))
        {
            return Precondition("The prepared document path does not match the exact opened Working document.",
                facts.DocumentFullPath);
        }
        if (facts.PixelWidth <= 0 || facts.PixelHeight <= 0 ||
            Math.Abs(facts.ResolutionPpi - 300) > ResolutionTolerancePpi)
        {
            return Precondition("The prepared geometry is invalid or is not exactly 300 PPI.", facts.DocumentFullPath);
        }
        if (!string.Equals(facts.ColourMode, "DocumentMode.RGB", StringComparison.Ordinal) ||
            !string.Equals(facts.BitDepth, "BitsPerChannelType.EIGHT", StringComparison.Ordinal) ||
            facts.W1Exists || facts.Channels.Length != 3 || facts.Channels.Any(c => !IsComponent(c)))
        {
            return Precondition("The prepared document is not the exact RGB/8, three-component, no-W1 state.",
                facts.DocumentFullPath);
        }

        return OperationResult.Ok();
    }

    private static OperationResult<Unit> ValidateResult(
        PhotoshopPreparedDocument prepared,
        PhotoshopDocumentFacts before,
        PhotoshopDocumentFacts after,
        PhotoshopW1ChannelFacts w1)
    {
        PhotoshopDocumentFacts expected = prepared.Actual;
        if (!SamePath(expected.DocumentFullPath, before.DocumentFullPath) ||
            !SamePath(expected.DocumentFullPath, after.DocumentFullPath))
        {
            return Invalid("The active document path changed during the Action.");
        }
        if (before.PixelWidth != expected.PixelWidth || before.PixelHeight != expected.PixelHeight ||
            Math.Abs(before.ResolutionPpi - expected.ResolutionPpi) > ResolutionTolerancePpi ||
            !string.Equals(before.ColourMode, "DocumentMode.RGB", StringComparison.Ordinal) ||
            !string.Equals(before.BitDepth, "BitsPerChannelType.EIGHT", StringComparison.Ordinal) ||
            before.Channels.Length != 3 || before.Channels.Any(c => !IsComponent(c)) || before.W1Exists)
        {
            return Invalid("Photoshop's immediate pre-Action facts did not match B1A.3 preparation.");
        }
        if (after.PixelWidth != before.PixelWidth || after.PixelHeight != before.PixelHeight ||
            Math.Abs(after.ResolutionPpi - before.ResolutionPpi) > ResolutionTolerancePpi ||
            Math.Abs(after.PhysicalWidthMm - before.PhysicalWidthMm) > PhysicalToleranceMm ||
            Math.Abs(after.PhysicalHeightMm - before.PhysicalHeightMm) > PhysicalToleranceMm)
        {
            return Invalid("The W1 Action changed pixel, physical or 300-PPI geometry.");
        }
        if (!string.Equals(after.ColourMode, "DocumentMode.CMYK", StringComparison.Ordinal) ||
            !string.Equals(after.BitDepth, "BitsPerChannelType.EIGHT", StringComparison.Ordinal))
        {
            return Invalid("The accepted Action did not produce CMYK/8.");
        }

        ImmutableArray<PhotoshopChannelFact> components = [.. after.Channels.Where(IsComponent)];
        PhotoshopChannelFact[] namedW1 = [.. after.Channels.Where(c =>
            string.Equals(c.Name, "W1", StringComparison.Ordinal))];
        if (components.Length != 4 || namedW1.Length != 1 || after.Channels.Length != 5 ||
            components.Any(c => string.Equals(c.Name, "W1", StringComparison.Ordinal)) ||
            !after.W1Exists || !string.Equals(namedW1[0].Type, "ChannelType.SPOTCOLOR", StringComparison.Ordinal))
        {
            return Invalid("Photoshop did not report exactly four CMYK components plus one W1 spot channel.");
        }
        if (!string.Equals(w1.Name, "W1", StringComparison.Ordinal) ||
            !string.Equals(w1.Type, "ChannelType.SPOTCOLOR", StringComparison.Ordinal) ||
            !w1.IsNonEmpty || w1.NonWhitePixelCount <= 0)
        {
            return Invalid("W1 is missing, is not a spot channel, or contains no non-white content.");
        }
        if (before.DocumentCount != after.DocumentCount)
        {
            return Invalid("The shared Photoshop document count changed during the W1 Action.");
        }

        return OperationResult.Ok();
    }

    private static bool IsComponent(PhotoshopChannelFact channel) =>
        string.Equals(channel.Type, "ChannelType.COMPONENT", StringComparison.Ordinal);

    private static bool SamePath(string expected, string actual)
    {
        try
        {
            return string.Equals(Path.GetFullPath(expected), Path.GetFullPath(actual),
                StringComparison.OrdinalIgnoreCase);
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
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                1 << 20, useAsync: false);
            return OperationResult.Ok(Sha256.FromBytes(SHA256.HashData(stream)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail<Sha256>(FailureCode.OutputUnreadable,
                $"The managed Working backing file could not be hashed: {ex.Message}");
        }
    }

    private static OperationResult<ImmutableArray<string>> SnapshotDirectory(string backingPath)
    {
        try
        {
            return OperationResult.Ok(Directory.EnumerateFiles(Path.GetDirectoryName(backingPath)!, "*",
                    SearchOption.TopDirectoryOnly)
                .Select(Path.GetFullPath)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail<ImmutableArray<string>>(FailureCode.WorkspaceError,
                $"The controlled Working directory could not be observed: {ex.Message}");
        }
    }

    private static OperationResult<Unit> ArtifactFailure(
        string detail, string path, string? actual = null, string? expected = null)
    {
        Dictionary<string, string> context = new(NoActionContext(path));
        if (actual is not null) context["actualSha256"] = actual;
        if (expected is not null) context["expectedSha256"] = expected;
        return OperationResult.Fail<Unit>(OperationFailure.Create(
            FailureCode.EnvironmentNotVerified, detail + " It was not repaired or reloaded.", context: context));
    }

    private static OperationResult<Unit> Precondition(string detail, string path) =>
        OperationResult.Fail<Unit>(OperationFailure.Create(
            FailureCode.PreconditionNotMet, detail + " No Action was invoked.",
            context: NoActionContext(path)));

    private static OperationResult<Unit> Invalid(string detail) =>
        OperationResult.Fail<Unit>(OperationFailure.Create(
            FailureCode.OutputValidationFailed,
            detail + " No corrective conversion, retry, save or later operation was attempted.",
            context: new Dictionary<string, string>
            {
                ["automaticRetry"] = "false",
                ["saved"] = "false",
                ["adapterOutputCreated"] = "false",
                ["revisionCreated"] = "false",
            }));

    private static OperationResult<PhotoshopW1PreparedDocument> CancelledBeforeAction(
        string path, Sha256? hash = null)
    {
        Dictionary<string, string> context = new(NoActionContext(path));
        if (hash is { } value) context["backingSha256"] = value.ToString();
        return OperationResult.Fail<PhotoshopW1PreparedDocument>(OperationFailure.Create(
            FailureCode.Cancelled,
            "CMYK + W1 execution was cancelled before Action invocation. The prepared RGB/8 " +
            "document and its backing file remain unchanged.",
            context: context));
    }

    private static OperationResult<PhotoshopW1PreparedDocument> Retained(
        OperationFailure failure, bool cancellationRequested)
    {
        Dictionary<string, string> context = new(failure.Context, StringComparer.Ordinal)
        {
            ["inMemoryCmykW1MayBeRetained"] = "true",
            ["saved"] = "false",
            ["adapterOutputCreated"] = "false",
            ["revisionCreated"] = "false",
            ["automaticRetry"] = "false",
        };
        if (cancellationRequested) context["cancellationRequestedAfterActionStarted"] = "true";
        return OperationResult.Fail<PhotoshopW1PreparedDocument>(failure with { Context = context });
    }

    private static Dictionary<string, string> ResultContext(PhotoshopW1PreparedDocument result) => new()
    {
        ["document"] = result.DocumentFullPath,
        ["branch"] = result.Branch.ToString(),
        ["action"] = result.ActionName,
        ["actionInvocationCount"] = "1",
        ["backingSha256"] = result.BackingWorkingSha256.ToString(),
        ["inMemoryCmykW1MayBeRetained"] = "true",
        ["saved"] = "false",
        ["adapterOutputCreated"] = "false",
        ["revisionCreated"] = "false",
    };

    private static IReadOnlyDictionary<string, string> NoActionContext(string path) =>
        new Dictionary<string, string>
        {
            ["document"] = path,
            ["actionInvocationCount"] = "0",
            ["saved"] = "false",
            ["adapterOutputCreated"] = "false",
            ["revisionCreated"] = "false",
        };
}
