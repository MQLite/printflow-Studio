using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Delivery;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign.Memorial;

namespace PrintFlow.Infrastructure.AiDesign.Memorial;

public sealed class FileMemorialMaterialExporter : IMemorialMaterialExporter
{
    private readonly IMemorialRenderStore _renders;
    private readonly IMemorialReviewStore _reviews;
    private readonly IAiAuthoringDraftStore _drafts;
    private readonly string[] _protectedRoots;

    public FileMemorialMaterialExporter(IMemorialRenderStore renders, IMemorialReviewStore reviews,
        IAiAuthoringDraftStore drafts, IEnumerable<string> protectedRoots)
    {
        _renders = renders;
        _reviews = reviews;
        _drafts = drafts;
        _protectedRoots = protectedRoots.Select(root => Path.IsPathFullyQualified(root)
                ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))
                : throw new ArgumentException("Protected roots must be absolute directories.", nameof(protectedRoots)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (_protectedRoots.Length == 0) throw new ArgumentException("The managed workspace must be protected.", nameof(protectedRoots));
    }

    public async Task<OperationResult<string>> ExportAsync(MemorialRenderResult expected, string selectedAbsolutePath, CancellationToken ct)
    {
        List<IDisposable> held = [];
        try
        {
            ct.ThrowIfCancellationRequested();
            if (expected?.File is not { Purpose: MemorialRenderPurpose.DesignMaterial } file ||
                file.DraftId == Guid.Empty || file.RenderId == Guid.Empty || expected.DraftRevision < 0 ||
                expected.Facts is not { Format: ImageFormat.Png, ByteLength: > 0 } facts || !Sha256.TryParse(facts.Sha256.Value, out _))
                return Refused("Only a reviewed design-material PNG can be exported.");
            if (!TryDestination(selectedAbsolutePath, out string destination))
                return Refused("Choose an absolute path with an ordinary PNG file name.");

            var review = await _reviews.ReadAsync(file, ct);
            if (review.IsFailure && review.Failure.Code != FailureCode.OutputMissing)
                return OperationResult.Fail<string>(review.Failure);
            if (review.IsFailure || !MatchesAcceptance(review.Value, expected))
                return Refused("Accept this exact design-material result before exporting it.");
            var current = await _drafts.ReadAsync(file.DraftId, ct);
            if (current.IsFailure) return OperationResult.Fail<string>(current.Failure);
            if (!MatchesDraft(current.Value, expected)) return Stale();

            string folder = Path.GetDirectoryName(destination)!;
            string leaf = Path.GetFileName(destination);
            if (_protectedRoots.Any(root => IsWithin(folder, root))) return Protected();
            SafeFileHandle directory = PinLocalFolder(folder, held);
            string resolvedFolder = WindowsDeliveryNative.ResolvedPath(directory);
            foreach (string root in _protectedRoots)
            {
                try
                {
                    string resolvedRoot = WindowsDeliveryNative.ResolvedPath(PinLocalFolder(root, held));
                    if (IsWithin(resolvedFolder, resolvedRoot)) return Protected();
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
                {
                    // An optional configured directory can be absent. The lexical check
                    // already excludes its name; an absent root has no filesystem alias.
                }
            }
            string final = Path.Combine(resolvedFolder, leaf);
            ReparsePointGuard.RefuseAncestry(final);
            if (File.Exists(final) || Directory.Exists(final)) return Collision();

            var opened = await _renders.OpenVerifiedAsync(expected, ct);
            if (opened.IsFailure) return OperationResult.Fail<string>(opened.Failure);
            await using Stream input = opened.Value;
            ct.ThrowIfCancellationRequested();

            // Create a fresh sibling and retain its creation handle. Until the atomic no-replace
            // rename succeeds, the chosen name cannot expose an incomplete or unverified copy.
            string stagePath = Path.Combine(resolvedFolder, $".memorial-export-{Guid.NewGuid():N}.tmp");
            using SafeFileHandle creation = NativeMethods.CreateFileW(stagePath,
                WindowsDeliveryNative.GenericRead | WindowsDeliveryNative.GenericWrite | WindowsDeliveryNative.DeleteAccess,
                0, 0, WindowsDeliveryNative.CreateNew, WindowsDeliveryNative.Normal | WindowsDeliveryNative.OpenReparsePoint, 0);
            if (creation.IsInvalid) throw WindowsDeliveryNative.Error("Create new memorial export staging file");
            FileStream? output = null;
            bool published = false;
            try
            {
                RequireUnlinked(creation);
                output = new FileStream(creation, FileAccess.Write, bufferSize: 1, isAsync: false);
                if (!await CopyVerifiedAsync(input, output, facts, ct))
                    return OperationResult.Fail<string>(FailureCode.RevisionIntegrityMismatch, "The held PNG no longer matches its reviewed bytes.");
                output.Flush(flushToDisk: true);

                // Hold the draft and acceptance CAS gates only for the publication. Both can
                // change during the copy, but neither can change between this check and rename.
                return await _drafts.WithCurrentRevisionAsync(file.DraftId, expected.DraftRevision, draft =>
                {
                    if (!MatchesDraft(draft, expected)) return Task.FromResult(Stale());
                    return _reviews.WithCurrentRevisionAsync(file, review.Value.Revision, accepted =>
                    {
                        if (!MatchesAcceptance(accepted, expected))
                            return Task.FromResult(Refused("The design-material acceptance changed during export."));
                        ct.ThrowIfCancellationRequested();
                        RequireUnlinked(creation);
                        try { WindowsDeliveryNative.RenameNoReplace(creation, directory, leaf); }
                        catch (Win32Exception ex) when (ex.NativeErrorCode is 80 or 183) { return Task.FromResult(Collision()); }
                        published = true;
                        return Task.FromResult(OperationResult.Ok(destination));
                    }, ct);
                }, ct);
            }
            finally
            {
                // The creation handle denied external sharing throughout.
                // Never reopen or remove a name another process could have taken over.
                try { if (!published) WindowsDeliveryNative.DeleteHeldFile(creation); }
                finally { output?.Dispose(); }
            }
        }
        catch (OperationCanceledException) { return OperationResult.Fail<string>(FailureCode.Cancelled, "Memorial material export cancelled."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or Win32Exception)
        { return OperationResult.Fail<string>(FailureCode.WorkspaceError, "The selected PNG could not be written safely."); }
        finally { for (int i = held.Count - 1; i >= 0; i--) held[i].Dispose(); }
    }

    private static bool MatchesAcceptance(MemorialReviewState? review, MemorialRenderResult expected) =>
        review is { Accepted: true, Checks: { } checks } && review.Render == expected &&
        Enum.GetValues<MemorialReviewItem>().All(item => checks.TryGetValue(item, out bool checkedItem) && checkedItem);

    private static bool MatchesDraft(AiAuthoringDraft draft, MemorialRenderResult expected)
    {
        if (draft.Id != expected.File.DraftId || draft.Revision != expected.DraftRevision) return false;
        var plan = MemorialCompositionModule.CreatePlan(draft, MemorialRenderPurpose.DesignMaterial);
        return plan.IsSuccess && string.Equals(plan.Value.SourceManifestSha256, expected.SourceManifestSha256, StringComparison.Ordinal);
    }

    private static SafeFileHandle PinLocalFolder(string folder, List<IDisposable> held)
    {
        WindowsDeliveryNative.RequireLocalNtfs(folder);
        VerifiedAiAttachmentReader.PinParents(folder, held);
        return (SafeFileHandle)held[^1];
    }

    private static void RequireUnlinked(SafeFileHandle handle)
    {
        if (!NativeMethods.GetFileInformationByHandle(handle, out NativeMethods.ByHandleFileInformation info) ||
            info.NumberOfLinks != 1 || (info.Attributes & (uint)(FileAttributes.Device | FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException("A new ordinary unshared export file is required.");
    }

    private static async Task<bool> CopyVerifiedAsync(Stream input, Stream output, FileFacts facts, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536];
        long remaining = facts.ByteLength;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            if (read > remaining) return false;
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            hash.AppendData(buffer.AsSpan(0, read));
            remaining -= read;
        }
        return remaining == 0 && Sha256.FromBytes(hash.GetHashAndReset()) == facts.Sha256;
    }

    private static bool TryDestination(string? selected, out string full)
    {
        full = "";
        if (selected is null || selected.Length is < 7 or >= 32000) return false;
        selected = selected.Replace('/', '\\');
        if (!char.IsAsciiLetter(selected[0]) || selected[1] != ':' || selected[2] != '\\' ||
            !Path.IsPathFullyQualified(selected) || selected[3..].Split('\\').Any(segment => !IsPlainSegment(segment)) ||
            !string.Equals(Path.GetExtension(selected), ".png", StringComparison.OrdinalIgnoreCase)) return false;
        full = Path.GetFullPath(selected);
        return true;
    }

    private static bool IsPlainSegment(string segment)
    {
        if (segment.Length is < 1 or > 255 || segment.EndsWith(' ') || segment.EndsWith('.') ||
            segment.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0 || segment.Any(char.IsControl)) return false;
        string stem = segment.Split('.', 2)[0].TrimEnd(' ').ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$") &&
            !(stem.Length == 4 && stem[..3] is ("COM" or "LPT") && "123456789¹²³".Contains(stem[3]));
    }

    private static bool IsWithin(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static OperationResult<string> Refused(string detail) => OperationResult.Fail<string>(FailureCode.PreconditionNotMet, detail);
    private static OperationResult<string> Protected() => Refused("Choose a folder outside the workspace and protected evidence or preset directories.");
    private static OperationResult<string> Collision() => Refused("A file or directory already occupies the chosen export name.");
    private static OperationResult<string> Stale() => OperationResult.Fail<string>(FailureCode.RevisionIntegrityMismatch, "The design material no longer matches the current draft.");
}
