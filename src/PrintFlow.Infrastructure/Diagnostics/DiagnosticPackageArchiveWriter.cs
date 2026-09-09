using System.IO;
using System.IO.Compression;
using System.Text;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Infrastructure.Diagnostics;

/// <summary>Stages, validates and publishes exactly the archive entries in a package plan.</summary>
public sealed class DiagnosticPackageArchiveWriter : IDiagnosticPackageWriter
{
    private readonly LocalDiagnosticPackageEvidence _evidence;
    private readonly string _stagingRoot;

    public DiagnosticPackageArchiveWriter(
        LocalDiagnosticPackageEvidence evidence,
        string stagingRoot)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);
        _evidence = evidence;
        _stagingRoot = Path.GetFullPath(stagingRoot);
    }

    public async Task<OperationResult<DiagnosticPackageExportResult>> WriteAsync(
        DiagnosticPackagePlan plan,
        string requestedDestination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedDestination);

        string? staged = null;
        string? destinationStage = null;
        string? published = null;
        try
        {
            OperationResult<Unit> shape = ValidatePlanShape(plan);
            if (shape.IsFailure)
                return OperationResult.Fail<DiagnosticPackageExportResult>(shape.Failure);

            if (!Path.IsPathFullyQualified(requestedDestination) ||
                !string.Equals(Path.GetExtension(requestedDestination), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult.Fail<DiagnosticPackageExportResult>(
                    FailureCode.PreconditionNotMet,
                    "A diagnostic package destination must be a fully qualified .zip file path.");
            }

            string requested = Path.GetFullPath(requestedDestination);
            string? destinationDirectory = Path.GetDirectoryName(requested);
            if (destinationDirectory is null || !Directory.Exists(destinationDirectory))
            {
                return OperationResult.Fail<DiagnosticPackageExportResult>(
                    FailureCode.WorkspaceError,
                    "The selected diagnostic package destination folder is unavailable.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            ReparsePointGuard.RefuseAncestry(_stagingRoot);
            Directory.CreateDirectory(_stagingRoot);
            ReparsePointGuard.RefuseAncestry(_stagingRoot);
            staged = Path.Combine(_stagingRoot, $"{Guid.NewGuid():N}.zip.tmp");

            await BuildArchiveAsync(staged, plan, cancellationToken).ConfigureAwait(false);
            await ValidateArchiveAsync(staged, plan, cancellationToken).ConfigureAwait(false);

            string candidate = AvailableDestination(requested);
            string adjacentStage = Path.Combine(
                destinationDirectory,
                $".{Path.GetFileName(candidate)}.{Guid.NewGuid():N}.tmp");
            destinationStage = adjacentStage;
            await VerifiedCopyAsync(staged, adjacentStage, cancellationToken).ConfigureAwait(false);
            await ValidateArchiveAsync(adjacentStage, plan, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            for (int collision = 0; ; collision++)
            {
                candidate = AvailableDestination(requested, collision);
                try
                {
                    File.Move(adjacentStage, candidate, overwrite: false);
                    destinationStage = null;
                    published = candidate;
                    break;
                }
                catch (IOException) when (File.Exists(candidate) && collision < 9999)
                {
                    // Another writer won this exact name between planning and the move. Reuse the
                    // already validated adjacent staging file and choose the next safe suffix.
                }
            }

            // The final result is not success until a fresh reader can decompress every exact
            // entry from the operator-owned path.
            await ValidateArchiveAsync(published, plan, CancellationToken.None).ConfigureAwait(false);
            return OperationResult.Ok(new DiagnosticPackageExportResult(
                published,
                plan.ArchiveEntryNames.ToArray()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       ArgumentException or NotSupportedException or InvalidDataException)
        {
            if (published is not null)
            {
                TryDeleteOwnedFile(published);
                published = null;
            }

            return OperationResult.Fail<DiagnosticPackageExportResult>(
                FailureCode.WorkspaceError,
                $"The diagnostic package could not be created safely: {ex.Message}");
        }
        finally
        {
            if (staged is not null) TryDeleteOwnedFile(staged);
            if (destinationStage is not null) TryDeleteOwnedFile(destinationStage);
        }
    }

    private async Task BuildArchiveAsync(
        string staged,
        DiagnosticPackagePlan plan,
        CancellationToken cancellationToken)
    {
        await using FileStream output = new(
            staged, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 81920, useAsync: true);
        using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry manifest = archive.CreateEntry("manifest.txt", CompressionLevel.Optimal);
            await using (Stream stream = manifest.Open())
            await using (StreamWriter writer = new(
                             stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteAsync(
                        DiagnosticPackageManifestRenderer.Render(plan).AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
            }

            DiagnosticPackageItem? screenshot = plan.Items.SingleOrDefault(item =>
                item is { Role: DiagnosticPackageItemRole.FailureScreenshot,
                          Disposition: DiagnosticPackageItemDisposition.Included,
                          Content: DiagnosticPackageItemContent.EvidenceFile });
            if (screenshot is not null)
            {
                OperationResult<FileStream> opened =
                    _evidence.OpenVerifiedFailureScreenshot(screenshot.File!);
                if (opened.IsFailure)
                    throw new IOException(opened.Failure.TechnicalDetail);

                await using FileStream source = opened.Value;
                ZipArchiveEntry entry = archive.CreateEntry(
                    screenshot.ArchiveEntryName!, CompressionLevel.Optimal);
                await using Stream destination = entry.Open();
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }

    private static async Task VerifiedCopyAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using FileStream input = new(
            source, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        await using FileStream output = new(
            destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 81920, useAsync: true);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }

    private static async Task ValidateArchiveAsync(
        string path,
        DiagnosticPackagePlan plan,
        CancellationToken cancellationToken)
    {
        await using FileStream input = new(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        using ZipArchive archive = new(input, ZipArchiveMode.Read, leaveOpen: true);
        string[] expected = [.. plan.ArchiveEntryNames.Order(StringComparer.Ordinal)];
        string[] actual = [.. archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal)];
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal) ||
            archive.GetEntry("manifest.txt") is not { Length: > 0 })
        {
            throw new InvalidDataException("The archive entries do not exactly match the diagnostic package plan.");
        }

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            DiagnosticPackageItem? item = plan.Items.SingleOrDefault(candidate =>
                candidate.ArchiveEntryName == entry.FullName);
            if (item?.Content == DiagnosticPackageItemContent.EvidenceFile &&
                entry.Length != item.File!.Length)
            {
                throw new InvalidDataException($"Archive entry '{entry.FullName}' has an unexpected length.");
            }

            await using Stream content = entry.Open();
            await content.CopyToAsync(Stream.Null, cancellationToken).ConfigureAwait(false);
        }
    }

    private static OperationResult<Unit> ValidatePlanShape(DiagnosticPackagePlan plan)
    {
        DiagnosticPackageItem[] manifests = plan.Items.Where(item =>
            item.Role == DiagnosticPackageItemRole.Manifest).ToArray();
        if (manifests is not [{ Disposition: DiagnosticPackageItemDisposition.Included,
                               Content: DiagnosticPackageItemContent.GeneratedManifest,
                               ArchiveEntryName: "manifest.txt" }])
        {
            return OperationResult.Fail<Unit>(
                FailureCode.PreconditionNotMet,
                "The diagnostic package plan does not contain exactly one generated manifest.");
        }

        if (plan.Items.Any(item => item.Content == DiagnosticPackageItemContent.EvidenceFile &&
                                   (item.Role != DiagnosticPackageItemRole.FailureScreenshot ||
                                    item.ArchiveEntryName != "failure-screenshot.png" || item.File is null)))
        {
            return OperationResult.Fail<Unit>(
                FailureCode.PreconditionNotMet,
                "The diagnostic package plan contains an unrecognized evidence file role.");
        }

        return OperationResult.Ok();
    }

    private static string AvailableDestination(string requested, int startAt = 0)
    {
        string directory = Path.GetDirectoryName(requested)!;
        string stem = Path.GetFileNameWithoutExtension(requested);
        string extension = Path.GetExtension(requested);
        for (int index = startAt; index < 10000; index++)
        {
            string candidate = index == 0
                ? requested
                : Path.Combine(directory, $"{stem} ({index + 1}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }

        throw new IOException("No collision-safe diagnostic package file name is available.");
    }

    private static void TryDeleteOwnedFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
