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
public sealed class WicManualResultImporter(IWorkspace workspace, IFileInspector inspector) : IManualResultImporter
{
    public const long MaxBytes = 256L * 1024 * 1024;
    public const long MaxPixels = 100_000_000;

    public async Task<OperationResult<ManualResult>> ImportAsync(
        WorkspaceDirRef session, AttemptId attempt, StepKind step, FileFacts upstream, string selectedPath,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!ManualResultEligibility.Supports(step) || !Path.IsPathFullyQualified(selectedPath))
                return Fail(FailureCode.OutputValidationFailed, "Choose a supported local raster result.");
            string name = Path.GetFileName(selectedPath);
            string extension = Path.GetExtension(name).ToLowerInvariant();
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                (step == StepKind.BackgroundRemoval ? extension != ".png" :
                    extension is not (".png" or ".jpg" or ".jpeg")))
                return Fail(FailureCode.OutputValidationFailed, "The selected format is not supported for this operation.");
            if (!File.Exists(selectedPath))
                return Fail(FailureCode.OutputMissing, "The selected manual result no longer exists.");

            // FileShare.Read excludes writers and rename/delete for the entire snapshot operation.
            await using FileStream input = new(selectedPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 1 << 20, useAsync: true);
            if (input.Length <= 0 || input.Length > MaxBytes)
                return Fail(FailureCode.OutputValidationFailed, "The selected file is empty or exceeds the import limit.");
            Sha256 selectedHash = Sha256.FromBytes(await SHA256.HashDataAsync(input, cancellationToken));
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
            var inspected = await inspector.InspectAsync(destination, cancellationToken);
            if (inspected.IsFailure)
                return OperationResult.Fail<ManualResult>(Localise(inspected.Failure));
            FileFacts facts = inspected.Value;
            ImageFormat expected = extension == ".png" ? ImageFormat.Png : ImageFormat.Jpeg;
            if (facts.Sha256 != selectedHash || facts.ByteLength != input.Length || facts.Format != expected ||
                !facts.HasPixelDimensions || (long)facts.PixelWidth!.Value * facts.PixelHeight!.Value > MaxPixels)
                return Fail(FailureCode.OutputValidationFailed, "The managed copy is not a stable supported raster within the pixel limit.");

            if (step == StepKind.BackgroundRemoval &&
                (facts.PixelWidth != upstream.PixelWidth || facts.PixelHeight != upstream.PixelHeight))
                return OperationResult.Fail<ManualResult>(OperationFailure.Create(FailureCode.OutputValidationFailed,
                    "Background removal must preserve the approved input canvas dimensions.",
                    messageKey: "Failure_ManualResultCanvas"));

            var decoded = await Task.Run(() => ValidatePixels(heldCopy, step, cancellationToken), cancellationToken);
            if (decoded.IsFailure)
                return OperationResult.Fail<ManualResult>(decoded.Failure);
            // A second managed hash binds the decoded bytes to the immutable facts.
            heldCopy.Position = 0;
            if (Sha256.FromBytes(await SHA256.HashDataAsync(heldCopy, cancellationToken)) != selectedHash)
                return Fail(FailureCode.OutputValidationFailed, "The managed result changed during validation.");
            File.SetAttributes(destination, FileAttributes.ReadOnly);
            return OperationResult.Ok(new ManualResult(target, facts));
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
