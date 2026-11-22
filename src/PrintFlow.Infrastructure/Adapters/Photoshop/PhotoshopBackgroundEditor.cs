using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using ImageMagick;
using ImageMagick.Formats;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Preset;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

public sealed class PhotoshopBackgroundEditor : IPhotoshopBackgroundEditor
{
    private const long MaxPsdBytes = 1024L * 1024 * 1024;
    private static readonly TimeSpan SavePollInterval = TimeSpan.FromMilliseconds(500);
    private readonly IWorkspace _workspace;
    private readonly IWorkstationAutomationLeaseManager _leases;
    private readonly string _presetPath;
    private readonly Sha256 _presetHash;
    private readonly Func<ProcessStartInfo, bool> _launch;

    public PhotoshopBackgroundEditor(
        IWorkspace workspace, IWorkstationAutomationLeaseManager leases, string presetPath, Sha256 presetHash)
        : this(workspace, leases, presetPath, presetHash, StartPhotoshop)
    {
    }

    internal PhotoshopBackgroundEditor(
        IWorkspace workspace, IWorkstationAutomationLeaseManager leases, string presetPath, Sha256 presetHash,
        Func<ProcessStartInfo, bool> launch)
    {
        _workspace = workspace;
        _leases = leases;
        _presetPath = presetPath;
        _presetHash = presetHash;
        _launch = launch;
    }

    public async IAsyncEnumerable<OperationResult<PhotoshopBackgroundSave>> WatchSavesAsync(
        PhotoshopBackgroundEditSource source, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var admission = await _leases.TryAcquireAsync(null, cancellationToken).ConfigureAwait(false);
        if (admission.IsFailure)
        {
            yield return OperationResult.Fail<PhotoshopBackgroundSave>(admission.Failure);
            yield break;
        }

        try
        {
            var opened = await Task.Run(() => Open(source, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (opened.IsFailure)
            {
                yield return OperationResult.Fail<PhotoshopBackgroundSave>(opened.Failure);
                yield break;
            }

            var (path, examined, baselineHash) = opened.Value;
            SaveStamp? previous = null;
            while (true)
            {
                await Task.Delay(SavePollInterval, cancellationToken).ConfigureAwait(false);
                SaveStamp? stamp = ReadStamp(path);
                if (stamp is null || stamp == examined || stamp != previous)
                {
                    previous = stamp;
                    continue;
                }

                Guid previewId = Guid.NewGuid();
                string previewPath = FileWorkspace.PhotoshopPreviewPath(_workspace, source.SessionWorkspace, source.EditId, previewId);
                if (!TryReadSave(path, stamp.Value, baselineHash, source.Facts, previewPath, cancellationToken, out var saved))
                    continue;
                examined = stamp.Value;
                if (saved is not { } save) continue;
                try
                {
                    yield return save;
                }
                finally
                {
                    if (save.IsSuccess)
                        FileWorkspace.DiscardPhotoshopPreview(_workspace, source.SessionWorkspace, source.EditId, previewId);
                }
            }
        }
        finally
        {
            var released = await admission.Value.ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
            if (released.IsFailure) Trace.TraceWarning(released.Failure.ToString());
        }
    }

    private OperationResult<(string Path, SaveStamp Stamp, Sha256 Hash)> Open(
        PhotoshopBackgroundEditSource source, CancellationToken token)
    {
        try
        {
            var preset = VerifiedJsonFile.Read(_presetPath, _presetHash, "Workstation preset");
            if (preset.IsFailure) return OpenFailed(preset.Failure.ToString());
            using JsonDocument manifest = preset.Value;
            if (manifest.RootElement is not { ValueKind: JsonValueKind.Object } root ||
                !root.TryGetProperty("photoshopContract", out JsonElement contract) ||
                contract.ValueKind != JsonValueKind.Object ||
                !contract.TryGetProperty("executablePath", out JsonElement configured) ||
                configured.ValueKind != JsonValueKind.String || configured.GetString() is not { } executable ||
                !Path.IsPathFullyQualified(executable) ||
                !string.Equals(Path.GetFileName(executable), "Photoshop.exe", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(executable))
                return OpenFailed("The workstation preset does not point to an installed Photoshop executable.");

            string folder = _workspace.ResolveAbsoluteDirectory(WorkspaceDirRef.Create(
                $"{source.SessionWorkspace.RelativePath}/PhotoshopEdits/{source.EditId:N}"));
            ReparsePointGuard.RefuseAncestry(folder);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, source.PsdFileName);
            if (LocalImageFiles.MagickRuntimeFailure is { } runtimeError)
                return OpenFailed(runtimeError.ToString());

            using (var input = OpenSnapshot(_workspace.ResolveAbsolute(source.Image)))
            {
                if (input.Length != source.Facts.ByteLength || Hash(input, token) != source.Facts.Sha256)
                    return OpenFailed("The reviewed image changed on disk.");
                input.Position = 0;
                WriteWorkingDocument(input, path, source.Facts, token);
            }

            SaveStamp stamp = ReadStamp(path) ?? throw new IOException("The working PSD was not written.");
            Sha256 baselineHash;
            using (var prepared = OpenSnapshot(path))
                baselineHash = Hash(prepared, token);

            var launch = new ProcessStartInfo(executable) { UseShellExecute = false };
            launch.ArgumentList.Add(path);
            token.ThrowIfCancellationRequested();
            return _launch(launch) ? OperationResult.Ok((path, stamp, baselineHash)) : OpenFailed("Photoshop did not start.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or Win32Exception or MagickException)
        {
            token.ThrowIfCancellationRequested();
            return OpenFailed(ex.Message);
        }
    }

    private static bool StartPhotoshop(ProcessStartInfo launch)
    {
        using var process = Process.Start(launch);
        return process is not null;
    }

    internal static void WriteWorkingDocument(Stream source, string path, FileFacts facts, CancellationToken token)
    {
        if (!FitsPsd(facts.PixelWidth, facts.PixelHeight))
            throw new InvalidDataException("This canvas exceeds the supported Photoshop editing size.");

        MagickFormat format = facts.Format switch
        {
            ImageFormat.Png => MagickFormat.Png,
            ImageFormat.Jpeg => MagickFormat.Jpeg,
            ImageFormat.Tiff => MagickFormat.Tiff,
            _ => throw new InvalidDataException("The input is not a supported raster image."),
        };
        using var layers = new MagickImageCollection();
        var composite = new MagickImage(source, new MagickReadSettings { Format = format, FrameIndex = 0, FrameCount = 1 });
        layers.Add(composite);
        // Photoshop cannot edit layers in Indexed Color mode.
        composite.ClassType = ClassType.Direct;
        composite.ColorType = composite.HasAlpha ? ColorType.TrueColorAlpha : ColorType.TrueColor;
        var editable = composite.Clone();
        editable.Label = "Cutout";
        layers.Add(editable);
        FileWorkspace.WriteNewWorkingCandidate(path, output => layers.Write(output, MagickFormat.Psd), token);
    }

    private static bool TryReadSave(string path, SaveStamp stamp, Sha256 baselineHash, FileFacts expected,
        string previewPath, CancellationToken token, out OperationResult<PhotoshopBackgroundSave>? save)
    {
        save = null;
        try
        {
            using var input = OpenSnapshot(path);
            if (ReadStamp(path) != stamp) return false;
            if (input.Length <= 0 || input.Length > MaxPsdBytes)
            {
                save = OperationResult.Fail<PhotoshopBackgroundSave>(SaveFailure(
                    "The saved PSD is empty or exceeds the supported size."));
                return true;
            }
            Sha256 hash = Hash(input, token);
            if (hash == baselineHash) return true;
            input.Position = 0;

            var preview = ExtractSavedPreview(input, previewPath, expected, token);
            save = preview.IsSuccess ? OperationResult.Ok(new PhotoshopBackgroundSave(path, hash, preview.Value))
                : OperationResult.Fail<PhotoshopBackgroundSave>(preview.Failure);
            return true;
        }
        catch (IOException)
        {
            // A sharing violation or an atomic replace mid-observation is retried.
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            save = OperationResult.Fail<PhotoshopBackgroundSave>(SaveFailure(ex.Message));
            return true;
        }
    }

    internal static OperationResult<string> ExtractSavedPreview(Stream input, string output, FileFacts expected,
        CancellationToken token)
    {
        try
        {
            var compatible = PsdCompositeProbe.Inspect(input);
            if (compatible.IsFailure) return OperationResult.Fail<string>(SaveFailure(compatible.Failure.ToString()));
            input.Position = 0;
            var settings = new MagickReadSettings { Format = MagickFormat.Psd, FrameIndex = 0, FrameCount = 1 };
            var info = new MagickImageInfo(input, settings);
            if (info.Width != expected.PixelWidth || info.Height != expected.PixelHeight)
                return OperationResult.Fail<string>(OperationFailure.Create(FailureCode.OutputValidationFailed,
                    "The saved PSD changed the reviewed canvas.", messageKey: "Failure_ManualResultCanvas"));
            input.Position = 0;
            using var image = new MagickImage(input, settings);
            LocalImageFiles.WriteCandidate(image, output, new PngWriteDefines { PreserveiCCP = true }, token);
            return OperationResult.Ok(output);
        }
        catch (Exception ex) when (ex is IOException or MagickException)
        {
            token.ThrowIfCancellationRequested();
            return OperationResult.Fail<string>(SaveFailure(ex.Message));
        }
    }

    private static bool FitsPsd(int? width, int? height) =>
        width is int w && height is int h &&
        LocalImageFiles.Fits((uint)w, (uint)h, 30000, WicManualResultImporter.MaxPixels);

    private static FileStream OpenSnapshot(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    private static Sha256 Hash(Stream input, CancellationToken token) =>
        Sha256.FromBytes(SHA256.HashDataAsync(input, token).AsTask().GetAwaiter().GetResult());

    private static SaveStamp? ReadStamp(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? new SaveStamp(file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc) : null;
    }

    private static OperationResult<(string Path, SaveStamp Stamp, Sha256 Hash)> OpenFailed(string detail) =>
        OperationResult.Fail<(string, SaveStamp, Sha256)>(OperationFailure.Create(
            FailureCode.AdapterUnavailable, detail, messageKey: "Session_PhotoshopEditOpenFailed"));

    private static OperationFailure SaveFailure(string detail) => OperationFailure.Create(
        FailureCode.OutputUnreadable, detail, messageKey: "Session_PhotoshopEditSaveFailed");

    private readonly record struct SaveStamp(long Length, DateTime LastWrite, DateTime Creation);
}
