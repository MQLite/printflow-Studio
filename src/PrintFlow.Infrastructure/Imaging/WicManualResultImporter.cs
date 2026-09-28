using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Infrastructure.Imaging;

/// <summary>Managed, independently decoded manual evidence for SCRUM-11092 / SCRUM-11112.</summary>
/// <remarks>
/// SCRUM-11148 adds a read-only preflight and a selected-bytes expectation. Both use the one
/// validation routine the import itself uses (<see cref="ValidateHeldAsync"/>), so a file the
/// preflight accepts is checked against exactly the limits the import applies, and the import's
/// own checks on the managed copy are unchanged.
/// </remarks>
public sealed class WicManualResultImporter(IWorkspace workspace, IFileInspector inspector) : IManualResultImporter
{
    public const long MaxBytes = 256L * 1024 * 1024;
    public const long MaxPixels = 100_000_000;

    public Task<OperationResult<ManualResult>> ImportAsync(
        WorkspaceDirRef session, AttemptId attempt, StepKind step, FileFacts upstream, string selectedPath,
        CancellationToken cancellationToken) =>
        ImportAsync(session, attempt, step, upstream, selectedPath, expectedSelectedHash: null, cancellationToken);

    public async Task<OperationResult<ManualResult>> ImportAsync(
        WorkspaceDirRef session, AttemptId attempt, StepKind step, FileFacts upstream, string selectedPath,
        Sha256? expectedSelectedHash, CancellationToken cancellationToken)
    {
        try
        {
            if (Refuse(step, selectedPath, out string name, out string extension) is { } refused)
                return OperationResult.Fail<ManualResult>(refused);

            // FileShare.Read excludes writers and rename/delete for the entire snapshot operation.
            await using FileStream input = new(selectedPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 1 << 20, useAsync: true);
            if (input.Length <= 0 || input.Length > MaxBytes)
                return Fail(FailureCode.OutputValidationFailed, "The selected file is empty or exceeds the import limit.");
            Sha256 selectedHash = Sha256.FromBytes(await SHA256.HashDataAsync(input, cancellationToken));

            // The bytes the operator's check approved, or nothing: a file swapped after the check is
            // refused before any copy exists (SCRUM-11148, design §6.5 step 3).
            if (expectedSelectedHash is { } expected && selectedHash != expected)
                return OperationResult.Fail<ManualResult>(OperationFailure.Create(FailureCode.OutputValidationFailed,
                    "The selected picture changed after it was checked; nothing was copied.",
                    isRetryable: true, messageKey: "Session_CorrectionChanged"));

            input.Position = 0;
            WorkspaceFileRef target = WorkspaceFileRef.Create(
                $"{session.RelativePath}/Working/{attempt.Value:D}/{name}", WorkspaceArea.Working);
            string destination = workspace.ResolveAbsolute(target);
            string directory = Path.GetDirectoryName(destination)!;
            // Refuse reparse-point redirection at every existing destination ancestor.
            for (DirectoryInfo? ancestor = new(directory); ancestor is not null; ancestor = ancestor.Parent)
                if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                    return Fail(FailureCode.WorkspaceError, "The managed destination contains a redirected directory.");
            Directory.CreateDirectory(directory);
            await using (FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1 << 20, useAsync: true))
                await input.CopyToAsync(output, cancellationToken);

            // Independent inspection and full pixel decode operate only on the managed copy.
            using FileStream heldCopy = new(destination, FileMode.Open, FileAccess.Read, FileShare.Read);
            OperationResult<FileFacts> validated = await ValidateHeldAsync(
                heldCopy, destination, step, upstream, selectedHash, input.Length, extension, cancellationToken);
            if (validated.IsFailure)
                return OperationResult.Fail<ManualResult>(validated.Failure);
            File.SetAttributes(destination, FileAttributes.ReadOnly);
            return OperationResult.Ok(new ManualResult(target, validated.Value));
        }
        catch (OperationCanceledException)
        {
            return Fail(FailureCode.Cancelled, "Manual result import was cancelled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            NotSupportedException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return Fail(ex is UnauthorizedAccessException ? FailureCode.WorkspaceError : FailureCode.OutputUnreadable,
                $"Manual result could not be imported: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks the selected file itself, read-only, with the import's own limits (SCRUM-11148).
    /// Nothing is copied or written.
    /// </summary>
    public async Task<OperationResult<ManualResultPreflight>> PreflightAsync(
        StepKind step, FileFacts upstream, string selectedPath, CancellationToken cancellationToken)
    {
        try
        {
            if (Refuse(step, selectedPath, out _, out string extension) is { } refused)
                return OperationResult.Fail<ManualResultPreflight>(refused);

            await using FileStream held = new(selectedPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 1 << 20, useAsync: true);
            if (held.Length <= 0 || held.Length > MaxBytes)
                return OperationResult.Fail<ManualResultPreflight>(Localise(OperationFailure.Create(
                    FailureCode.OutputValidationFailed, "The selected file is empty or exceeds the import limit.")));
            long length = held.Length;
            Sha256 selectedHash = Sha256.FromBytes(await SHA256.HashDataAsync(held, cancellationToken));

            OperationResult<FileFacts> validated = await ValidateHeldAsync(
                held, selectedPath, step, upstream, selectedHash, length, extension, cancellationToken);
            return validated.IsFailure
                ? OperationResult.Fail<ManualResultPreflight>(validated.Failure)
                : OperationResult.Ok(new ManualResultPreflight(
                    selectedHash, length, validated.Value.PixelWidth!.Value, validated.Value.PixelHeight!.Value));
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail<ManualResultPreflight>(Localise(OperationFailure.Create(
                FailureCode.Cancelled, "Checking the manual result was cancelled.", isRetryable: true)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            NotSupportedException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return OperationResult.Fail<ManualResultPreflight>(Localise(OperationFailure.Create(
                ex is UnauthorizedAccessException ? FailureCode.WorkspaceError : FailureCode.OutputUnreadable,
                $"Manual result could not be checked: {ex.Message}", isRetryable: true)));
        }
    }

    /// <summary>The selected file's hash, read with writers excluded. Nothing is written.</summary>
    public async Task<OperationResult<Sha256>> HashSelectedAsync(string selectedPath, CancellationToken cancellationToken)
    {
        try
        {
            if (!Path.IsPathFullyQualified(selectedPath) || !File.Exists(selectedPath))
                return OperationResult.Fail<Sha256>(Localise(OperationFailure.Create(
                    FailureCode.OutputMissing, "The selected manual result no longer exists.", isRetryable: true)));
            await using FileStream held = new(selectedPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 1 << 20, useAsync: true);
            return OperationResult.Ok(Sha256.FromBytes(await SHA256.HashDataAsync(held, cancellationToken)));
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail<Sha256>(Localise(OperationFailure.Create(
                FailureCode.Cancelled, "Hashing the manual result was cancelled.", isRetryable: true)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            NotSupportedException or ArgumentException)
        {
            return OperationResult.Fail<Sha256>(Localise(OperationFailure.Create(
                ex is UnauthorizedAccessException ? FailureCode.WorkspaceError : FailureCode.OutputUnreadable,
                $"Manual result could not be read: {ex.Message}", isRetryable: true)));
        }
    }

    /// <summary>The name, extension and existence rules shared by import and preflight.</summary>
    private static OperationFailure? Refuse(StepKind step, string selectedPath, out string name, out string extension)
    {
        name = string.Empty;
        extension = string.Empty;
        if (!ManualResultEligibility.Supports(step) || !Path.IsPathFullyQualified(selectedPath))
            return Localise(OperationFailure.Create(FailureCode.OutputValidationFailed,
                "Choose a supported local raster result.", isRetryable: true));
        name = Path.GetFileName(selectedPath);
        extension = Path.GetExtension(name).ToLowerInvariant();
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            (step == StepKind.BackgroundRemoval ? extension != ".png" :
                extension is not (".png" or ".jpg" or ".jpeg")))
            return Localise(OperationFailure.Create(FailureCode.OutputValidationFailed,
                "The selected format is not supported for this operation.", isRetryable: true));
        if (!File.Exists(selectedPath))
            return Localise(OperationFailure.Create(FailureCode.OutputMissing,
                "The selected manual result no longer exists.", isRetryable: true));
        return null;
    }

    /// <summary>
    /// The one validation routine: independent inspection, format/size/pixel-limit facts, the
    /// background-removal canvas rule, a full pixel decode and a second hash of the held bytes.
    /// </summary>
    private async Task<OperationResult<FileFacts>> ValidateHeldAsync(
        FileStream held, string path, StepKind step, FileFacts upstream, Sha256 expectedHash, long expectedLength,
        string extension, CancellationToken cancellationToken)
    {
        var inspected = await inspector.InspectAsync(path, cancellationToken);
        if (inspected.IsFailure)
            return OperationResult.Fail<FileFacts>(Localise(inspected.Failure));
        FileFacts facts = inspected.Value;
        ImageFormat expected = extension == ".png" ? ImageFormat.Png : ImageFormat.Jpeg;
        if (facts.Sha256 != expectedHash || facts.ByteLength != expectedLength || facts.Format != expected ||
            !facts.HasPixelDimensions || (long)facts.PixelWidth!.Value * facts.PixelHeight!.Value > MaxPixels)
            return OperationResult.Fail<FileFacts>(Localise(OperationFailure.Create(FailureCode.OutputValidationFailed,
                "The file is not a stable supported raster within the pixel limit.", isRetryable: true)));

        if (step == StepKind.BackgroundRemoval &&
            (facts.PixelWidth != upstream.PixelWidth || facts.PixelHeight != upstream.PixelHeight))
            return OperationResult.Fail<FileFacts>(OperationFailure.Create(FailureCode.OutputValidationFailed,
                "Background removal must preserve the approved input canvas dimensions.",
                messageKey: "Failure_ManualResultCanvas"));

        held.Position = 0;
        var decoded = await Task.Run(() => ValidatePixels(held, step, cancellationToken), cancellationToken);
        if (decoded.IsFailure)
            return OperationResult.Fail<FileFacts>(decoded.Failure);
        // A second hash binds the decoded bytes to the immutable facts.
        held.Position = 0;
        if (Sha256.FromBytes(await SHA256.HashDataAsync(held, cancellationToken)) != expectedHash)
            return OperationResult.Fail<FileFacts>(Localise(OperationFailure.Create(FailureCode.OutputValidationFailed,
                "The file changed during validation.", isRetryable: true)));
        return OperationResult.Ok(facts);
    }

    private static OperationResult<Unit> ValidatePixels(Stream stream, StepKind step, CancellationToken token)
    {
        BitmapDecoder decoder = BitmapDecoder.Create(stream,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
        if (decoder.Frames.Count != 1)
            return OperationResult.Fail<Unit>(Localise(OperationFailure.Create(
                FailureCode.OutputValidationFailed, "A manual result must contain exactly one image.")));
        BitmapSource frame = decoder.Frames[0];
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || (long)frame.PixelWidth * frame.PixelHeight > MaxPixels)
            return OperationResult.Fail<Unit>(Localise(OperationFailure.Create(
                FailureCode.OutputValidationFailed, "The image exceeds the manual result pixel limit.")));
        BitmapSource pixels = frame.Format == PixelFormats.Bgra32 ? frame :
            new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int stride = checked(frame.PixelWidth * 4);
        byte[] row = new byte[stride];
        bool transparent = false, visible = false;
        for (int y = 0; y < frame.PixelHeight; y++)
        {
            token.ThrowIfCancellationRequested();
            pixels.CopyPixels(new Int32Rect(0, y, frame.PixelWidth, 1), row, stride, 0);
            for (int x = 3; x < stride; x += 4)
            {
                transparent |= row[x] < 255;
                visible |= row[x] > 0;
            }
        }
        if (step == StepKind.BackgroundRemoval && (!transparent || !visible))
            return OperationResult.Fail<Unit>(OperationFailure.Create(FailureCode.OutputValidationFailed,
                "Background removal requires transparent pixels and visible foreground.",
                messageKey: "Failure_ManualResultTransparency"));
        return OperationResult.Ok();
    }

    private static OperationResult<ManualResult> Fail(FailureCode code, string detail) =>
        OperationResult.Fail<ManualResult>(Localise(OperationFailure.Create(code, detail, isRetryable: true)));

    private static OperationFailure Localise(OperationFailure failure) => failure with
    {
        MessageKey = failure.Code is FailureCode.WorkspaceError or FailureCode.Cancelled
            ? "Failure_ManualResultImport" : "Failure_ManualResultInvalid",
    };
}
