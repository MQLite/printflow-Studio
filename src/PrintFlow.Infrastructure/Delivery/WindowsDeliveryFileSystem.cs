using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using PrintFlow.Domain.Files;
using PrintFlow.Workflow.Delivery;

namespace PrintFlow.Infrastructure.Delivery;

/// <summary>Verified local NTFS delivery operations; unsupported guards fail closed.</summary>
public sealed class WindowsDeliveryFileSystem : IDeliveryFileSystem
{
    public DeliveryFileResult<string> ValidateLeaf(string candidate, ArtifactKind kind)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate is "." or ".." ||
            candidate.EndsWith(' ') || candidate.EndsWith('.') || candidate.Length > 240 ||
            candidate.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']) >= 0 ||
            candidate.Any(char.IsControl))
            return DeliveryFileResult<string>.Fail(DeliveryCode.InvalidName, "Choose a plain file name without trailing dots or spaces.");
        string stem = Path.GetFileNameWithoutExtension(candidate);
        string device = stem.Split('.')[0].ToUpperInvariant();
        if (device is "CON" or "PRN" or "AUX" or "NUL" or "COM1" or "COM2" or "COM3" or "COM4"
            or "COM5" or "COM6" or "COM7" or "COM8" or "COM9" or "LPT1" or "LPT2" or "LPT3"
            or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9")
            return DeliveryFileResult<string>.Fail(DeliveryCode.InvalidName, "Windows reserves this file name.");
        string extension = Path.GetExtension(candidate);
        if (extension.Length == 0)
            candidate += kind == ArtifactKind.ApprovedAssetPng ? ".png" : ".tif";
        else if (kind == ArtifactKind.ApprovedAssetPng && !extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                 kind == ArtifactKind.ApprovedPrintTiff &&
                 !extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) &&
                 !extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
            return DeliveryFileResult<string>.Fail(DeliveryCode.InvalidName, "File extension must match the approved image format.");
        return DeliveryFileResult<string>.Ok(candidate);
    }

    public string? SuggestAlternative(string leaf, Func<string, bool> exists)
    {
        string extension = Path.GetExtension(leaf);
        string stem = leaf[..^extension.Length];
        for (int i = 2; i <= 99; i++)
        {
            string suffix = $" ({i})";
            if (stem.Length + suffix.Length + extension.Length > 240) continue;
            string suggested = stem + suffix + extension;
            if (!exists(suggested)) return suggested;
        }
        return null;
    }

    public DeliveryFileResult<IDeliveryFileGuard> Open(
        string sourcePath, Sha256 expectedSourceHash, long expectedSourceLength,
        string requestedFolder, string finalLeaf, ArtifactKind kind,
        IReadOnlyList<string> protectedRoots, string? protectedOriginalPath)
    {
        DeliveryFileResult<string> leaf = ValidateLeaf(finalLeaf, kind);
        if (!leaf.IsSuccess || leaf.Value != finalLeaf)
            return DeliveryFileResult<IDeliveryFileGuard>.Fail(DeliveryCode.InvalidName,
                leaf.IsSuccess ? $"Confirm the effective file name '{leaf.Value}' before saving." : leaf.Detail!);
        if (!OperatingSystem.IsWindows())
            return DeliveryFileResult<IDeliveryFileGuard>.Fail(DeliveryCode.UnsupportedDestination, "Windows NTFS is required.");
        List<SafeFileHandle> held = [];
        FileStream? source = null;
        try
        {
            if (!Path.IsPathFullyQualified(requestedFolder))
                throw new NotSupportedException("Destination must be an absolute drive-letter folder.");
            string folder = Path.GetFullPath(requestedFolder);
            if (!Path.IsPathFullyQualified(folder) || folder.Length < 3 || folder[1] != ':')
                throw new NotSupportedException("Destination must be an absolute drive-letter folder.");
            WindowsDeliveryNative.RequireLocalNtfs(folder);
            SafeFileHandle directory = HoldChain(folder, held);
            string resolved = WindowsDeliveryNative.ResolvedPath(directory);
            WindowsDeliveryNative.RequireLocalNtfs(resolved);
            DeliveryFileIdentity directoryIdentity = WindowsDeliveryNative.Identity(directory);
            List<string> heldProtectedPaths = [];
            foreach (string protectedRoot in protectedRoots)
            {
                string protectedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(protectedRoot));
                SafeFileHandle protectedHandle;
                try { protectedHandle = HoldChain(protectedPath, held); }
                catch (Win32Exception ex) when ((ex.NativeErrorCode is 2 or 3) &&
                    heldProtectedPaths.Any(ancestor => IsInsideOrEqual(protectedPath, ancestor)))
                {
                    // Optional, absent descendants such as workspace/Evidence are already
                    // protected by the held ancestor. Existing junctions or inaccessible
                    // descendants still fail: neither is proved absent by this exception.
                    continue;
                }
                heldProtectedPaths.Add(protectedPath);
                string protectedResolved = WindowsDeliveryNative.ResolvedPath(protectedHandle);
                if (IsInsideOrEqual(resolved, protectedResolved))
                    return FailGuard(held, source, DeliveryCode.ProtectedDestination,
                        "Choose a folder outside PrintFlow's managed storage.");
            }
            if (protectedOriginalPath is { Length: > 0 })
            {
                string original = Path.GetFullPath(protectedOriginalPath);
                string originalFolder = Path.GetDirectoryName(original)!;
                SafeFileHandle originalParent = HoldChain(originalFolder, held);
                string resolvedOriginalParent = WindowsDeliveryNative.ResolvedPath(originalParent);
                if (string.Equals(Path.Combine(resolved, finalLeaf),
                        Path.Combine(resolvedOriginalParent, Path.GetFileName(original)),
                        StringComparison.OrdinalIgnoreCase))
                    return FailGuard(held, source, DeliveryCode.ProtectedDestination,
                        "That final name is the protected imported original.");
            }
            // Source ancestry is checked and held independently. A resolved file name cannot
            // be smuggled in through a symlink while the source handle is in use.
            HoldChain(Path.GetDirectoryName(Path.GetFullPath(sourcePath))!, held);
            SafeFileHandle sourceHandle = WindowsDeliveryNative.OpenSource(sourcePath);
            source = new FileStream(sourceHandle, FileAccess.Read);
            DeliveryFileIdentity sourceIdentity = WindowsDeliveryNative.Identity(source.SafeFileHandle);
            if (source.Length != expectedSourceLength || Hash(source) != expectedSourceHash)
                return FailGuard(held, source, DeliveryCode.SourceChanged,
                    "Approved source bytes no longer match the reviewed result.");
            if (Path.Combine(resolved, finalLeaf).Length >= 32000)
                return FailGuard(held, source, DeliveryCode.InvalidName, "The full destination path is too long.");
            IDeliveryFileGuard guard = new Guard(held, directory, source,
                sourceIdentity, expectedSourceHash, expectedSourceLength, resolved, finalLeaf,
                directoryIdentity);
            return DeliveryFileResult<IDeliveryFileGuard>.Ok(guard);
        }
        catch (NotSupportedException ex)
        {
            return FailGuard(held, source, DeliveryCode.UnsupportedDestination, ex.Message);
        }
        catch (Win32Exception ex)
        {
            DeliveryCode code = ex.NativeErrorCode is 2 or 3 or 15 or 21
                ? DeliveryCode.DestinationUnavailable : DeliveryCode.UnsupportedDestination;
            return FailGuard(held, source, code, ex.Message);
        }
        catch (IOException ex) { return FailGuard(held, source, DeliveryCode.DestinationUnavailable, ex.Message); }
        catch (UnauthorizedAccessException ex) { return FailGuard(held, source, DeliveryCode.DestinationNotWritable, ex.Message); }
    }

    public DeliveryFileResult<string> CheckFolder(string folder, IReadOnlyList<string> protectedRoots)
    {
        if (!OperatingSystem.IsWindows())
            return DeliveryFileResult<string>.Fail(DeliveryCode.UnsupportedDestination, "Windows NTFS is required.");
        List<SafeFileHandle> held = [];
        try
        {
            // The same held-chain, NTFS and protected-root rules Open applies, without a source
            // or a final name. Every handle is released before returning.
            if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
                throw new NotSupportedException("Destination must be an absolute drive-letter folder.");
            string full = Path.GetFullPath(folder);
            if (full.Length < 3 || full[1] != ':')
                throw new NotSupportedException("Destination must be an absolute drive-letter folder.");
            WindowsDeliveryNative.RequireLocalNtfs(full);
            string resolved = WindowsDeliveryNative.ResolvedPath(HoldChain(full, held));
            WindowsDeliveryNative.RequireLocalNtfs(resolved);
            foreach (string protectedRoot in protectedRoots)
            {
                string protectedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(protectedRoot));
                if (!Directory.Exists(protectedPath)) continue;
                List<SafeFileHandle> probe = [];
                try
                {
                    string protectedResolved = WindowsDeliveryNative.ResolvedPath(HoldChain(protectedPath, probe));
                    if (IsInsideOrEqual(resolved, protectedResolved))
                        return DeliveryFileResult<string>.Fail(DeliveryCode.ProtectedDestination,
                            "Choose a folder outside PrintFlow's managed storage.");
                }
                finally { foreach (SafeFileHandle handle in probe) handle.Dispose(); }
            }
            return DeliveryFileResult<string>.Ok(resolved);
        }
        catch (NotSupportedException ex) { return DeliveryFileResult<string>.Fail(DeliveryCode.UnsupportedDestination, ex.Message); }
        catch (Win32Exception ex)
        {
            return DeliveryFileResult<string>.Fail(ex.NativeErrorCode is 2 or 3 or 15 or 21
                ? DeliveryCode.DestinationUnavailable : DeliveryCode.UnsupportedDestination, ex.Message);
        }
        catch (IOException ex) { return DeliveryFileResult<string>.Fail(DeliveryCode.DestinationUnavailable, ex.Message); }
        catch (UnauthorizedAccessException ex) { return DeliveryFileResult<string>.Fail(DeliveryCode.DestinationNotWritable, ex.Message); }
        finally { foreach (SafeFileHandle handle in held) handle.Dispose(); }
    }

    private static DeliveryFileResult<IDeliveryFileGuard> FailGuard(
        List<SafeFileHandle> held, FileStream? source, DeliveryCode code, string detail)
    {
        source?.Dispose();
        foreach (SafeFileHandle handle in held) handle.Dispose();
        return DeliveryFileResult<IDeliveryFileGuard>.Fail(code, detail);
    }

    private static SafeFileHandle HoldChain(string absolutePath, List<SafeFileHandle> held)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(absolutePath));
        WindowsDeliveryNative.RequireLocalNtfs(full);
        string root = Path.GetPathRoot(full)!;
        SafeFileHandle current = WindowsDeliveryNative.OpenDirectory(root);
        held.Add(current);
        string remainder = full[root.Length..];
        foreach (string segment in remainder.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            string parent = WindowsDeliveryNative.ResolvedPath(current);
            current = WindowsDeliveryNative.OpenDirectory(Path.Combine(parent, segment));
            held.Add(current);
        }
        return current;
    }

    private static bool IsInsideOrEqual(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + "\\", StringComparison.OrdinalIgnoreCase);

    private static Sha256 Hash(Stream stream)
    {
        stream.Position = 0;
        Sha256 result = Sha256.FromBytes(SHA256.HashData(stream));
        stream.Position = 0;
        return result;
    }

    private sealed class Guard : IDeliveryFileGuard
    {
        private readonly List<SafeFileHandle> _directories;
        private readonly SafeFileHandle _directory;
        private FileStream? _source;
        private FileStream? _stage;
        private FileStream? _final;
        private readonly Sha256 _expectedSourceHash;
        private readonly long _expectedSourceLength;
        private bool _published;
        private bool _transferred;
        private bool _disposed;

        public Guard(List<SafeFileHandle> directories, SafeFileHandle directory,
            FileStream source, DeliveryFileIdentity sourceIdentity,
            Sha256 expectedSourceHash, long expectedSourceLength,
            string folder, string leaf, DeliveryFileIdentity directoryIdentity)
        {
            _directories = directories; _directory = directory; _source = source;
            SourceIdentity = sourceIdentity; _expectedSourceHash = expectedSourceHash;
            _expectedSourceLength = expectedSourceLength; ResolvedFolder = folder;
            FinalPath = Path.Combine(folder, leaf); VolumeId = directoryIdentity.VolumeId;
            DirectoryId = directoryIdentity.FileId;
            DestinationKey = $"{VolumeId}|{DirectoryId}|{leaf.ToUpperInvariant()}";
        }

        public string ResolvedFolder { get; }
        public string FinalPath { get; }
        public string VolumeId { get; }
        public string DirectoryId { get; }
        public string DestinationKey { get; }
        public DeliveryFileIdentity SourceIdentity { get; }

        public DeliveryFileResult<bool> VerifySource()
        {
            if (_source is null || _disposed) return DeliveryFileResult<bool>.Fail(DeliveryCode.SourceMissing, "Source guard is closed.");
            try
            {
                if (WindowsDeliveryNative.Identity(_source.SafeFileHandle) != SourceIdentity ||
                    _source.Length != _expectedSourceLength || Hash(_source) != _expectedSourceHash)
                    return DeliveryFileResult<bool>.Fail(DeliveryCode.SourceChanged, "Approved source changed.");
                if (WindowsDeliveryNative.Identity(_directory).VolumeId != VolumeId ||
                    WindowsDeliveryNative.Identity(_directory).FileId != DirectoryId)
                    return DeliveryFileResult<bool>.Fail(DeliveryCode.UnsupportedDestination, "Destination directory identity changed.");
                return DeliveryFileResult<bool>.Ok(true);
            }
            catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException)
            { return DeliveryFileResult<bool>.Fail(DeliveryCode.SourceChanged, ex.Message); }
        }

        public DeliveryFinalCheck CheckFinal(DeliveryFileIdentity? expectedIdentity = null)
        {
            if (_disposed) return new(DeliveryFilePresence.Unavailable, null, null, null, DeliveryCode.Unavailable, "Guard is closed.");
            try
            {
                _final?.Dispose();
                SafeFileHandle finalHandle = WindowsDeliveryNative.OpenFinal(FinalPath, _stage is not null);
                _final = new FileStream(finalHandle, FileAccess.Read);
                DeliveryFileIdentity identity = WindowsDeliveryNative.Identity(_final.SafeFileHandle);
                long length = _final.Length;
                Sha256 hash = Hash(_final);
                if (expectedIdentity is not null && expectedIdentity != identity)
                    return new(DeliveryFilePresence.Present, identity, length, hash, DeliveryCode.DeliveredFileChanged,
                        "Final name is a different file object.");
                return new(DeliveryFilePresence.Present, identity, length, hash);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
            { return new(DeliveryFilePresence.Absent, null, null, null); }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or NotSupportedException)
            { return new(DeliveryFilePresence.Unavailable, null, null, null, DeliveryCode.Unavailable, ex.Message); }
        }

        public DeliveryFileResult<DeliveryFileIdentity> CreateStaging(string stagingLeaf)
        {
            if (!ValidStagingLeaf(stagingLeaf) || _stage is not null || _disposed)
                return DeliveryFileResult<DeliveryFileIdentity>.Fail(DeliveryCode.InvalidName, "Invalid staging ownership name.");
            try
            {
                SafeFileHandle handle = WindowsDeliveryNative.OpenStage(Path.Combine(ResolvedFolder, stagingLeaf), true);
                _stage = new FileStream(handle, FileAccess.ReadWrite);
                return DeliveryFileResult<DeliveryFileIdentity>.Ok(WindowsDeliveryNative.Identity(_stage.SafeFileHandle));
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode is 80 or 183)
            { return DeliveryFileResult<DeliveryFileIdentity>.Fail(DeliveryCode.Collision, "Staging name already exists."); }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
            { return DeliveryFileResult<DeliveryFileIdentity>.Fail(DeliveryCode.DestinationNotWritable, ex.Message); }
        }

        public DeliveryFileResult<DeliveryFileIdentity> OpenOwnedStaging(string stagingLeaf, DeliveryFileIdentity expectedIdentity)
        {
            if (!ValidStagingLeaf(stagingLeaf) || _stage is not null || _disposed)
                return DeliveryFileResult<DeliveryFileIdentity>.Fail(DeliveryCode.InvalidName, "Invalid recorded staging name.");
            try
            {
                SafeFileHandle handle = WindowsDeliveryNative.OpenStage(Path.Combine(ResolvedFolder, stagingLeaf), false);
                _stage = new FileStream(handle, FileAccess.ReadWrite);
                DeliveryFileIdentity actual = WindowsDeliveryNative.Identity(_stage.SafeFileHandle);
                if (actual != expectedIdentity)
                {
                    _stage.Dispose(); _stage = null;
                    return DeliveryFileResult<DeliveryFileIdentity>.Fail(DeliveryCode.NeedsReconciliation,
                        "Recorded staging name is not the owned object.");
                }
                return DeliveryFileResult<DeliveryFileIdentity>.Ok(actual);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
            { return DeliveryFileResult<DeliveryFileIdentity>.Fail(DeliveryCode.SourceMissing, "Recorded staging file is absent."); }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
            { return DeliveryFileResult<DeliveryFileIdentity>.Fail(DeliveryCode.Unavailable, ex.Message); }
        }

        public async Task<DeliveryFileResult<bool>> CopyAndVerifyStageAsync(
            Sha256 expectedHash, long expectedLength, IProgress<long>? copied, CancellationToken cancellationToken)
        {
            if (_stage is null || _source is null || _disposed)
                return DeliveryFileResult<bool>.Fail(DeliveryCode.CopyFailed, "No held source and staging file.");
            try
            {
                _source.Position = 0; _stage.Position = 0; _stage.SetLength(0);
                byte[] buffer = new byte[1024 * 1024];
                long total = 0;
                while (true)
                {
                    int read = await _source.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    await _stage.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    total += read; copied?.Report(total);
                }
                _stage.Flush(flushToDisk: true);
                return VerifyStage(expectedHash, expectedLength);
            }
            catch (OperationCanceledException)
            { return DeliveryFileResult<bool>.Fail(DeliveryCode.Cancelled, "Copy cancelled before publication."); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { return DeliveryFileResult<bool>.Fail(DeliveryCode.CopyFailed, ex.Message); }
        }

        public DeliveryFileResult<bool> VerifyStage(Sha256 expectedHash, long expectedLength)
        {
            if (_stage is null || _disposed)
                return DeliveryFileResult<bool>.Fail(DeliveryCode.VerificationFailed, "No held staging file.");
            try
            {
                if (_stage.Length != expectedLength || Hash(_stage) != expectedHash)
                    return DeliveryFileResult<bool>.Fail(DeliveryCode.VerificationFailed, "Staged length or SHA-256 differs from approval.");
                return DeliveryFileResult<bool>.Ok(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { return DeliveryFileResult<bool>.Fail(DeliveryCode.VerificationFailed, ex.Message); }
        }

        public DeliveryFileResult<bool> PublishNoReplace(string stagingLeaf, DeliveryFileIdentity expectedIdentity)
        {
            if (_stage is null || _disposed || _published || !ValidStagingLeaf(stagingLeaf) ||
                WindowsDeliveryNative.Identity(_stage.SafeFileHandle) != expectedIdentity)
                return DeliveryFileResult<bool>.Fail(DeliveryCode.NeedsReconciliation, "Staging ownership is not verified.");
            try
            {
                if (VerifySource().IsSuccess == false)
                    return DeliveryFileResult<bool>.Fail(DeliveryCode.SourceChanged, "Approved source changed before publication.");
                // Once the native operation starts, uncertain completion must never allow
                // cleanup through the same handle (which may now refer to the final file).
                _published = true;
                WindowsDeliveryNative.RenameNoReplace(_stage.SafeFileHandle, _directory, Path.GetFileName(FinalPath));
                string renamedPath = WindowsDeliveryNative.ResolvedPath(_stage.SafeFileHandle);
                if (!string.Equals(renamedPath, FinalPath, StringComparison.OrdinalIgnoreCase))
                    return DeliveryFileResult<bool>.Fail(DeliveryCode.NeedsReconciliation,
                        $"Publication resolved to unexpected held path '{renamedPath}'.");
                return DeliveryFileResult<bool>.Ok(true);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode is 80 or 183)
            {
                _published = false; // Definite name collision: native rename did not occur.
                return DeliveryFileResult<bool>.Fail(DeliveryCode.Collision, "Final name was taken; no file was overwritten.");
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
            { return DeliveryFileResult<bool>.Fail(DeliveryCode.NeedsReconciliation, ex.Message); }
        }

        public DeliveryFileResult<bool> DeleteHeldStaging(DeliveryFileIdentity expectedIdentity)
        {
            if (_published || _stage is null || _disposed)
                return DeliveryFileResult<bool>.Fail(DeliveryCode.NeedsReconciliation, "Published or unheld file cannot be cleanup-deleted.");
            try
            {
                if (WindowsDeliveryNative.Identity(_stage.SafeFileHandle) != expectedIdentity)
                    return DeliveryFileResult<bool>.Fail(DeliveryCode.NeedsReconciliation, "Staging identity changed; cleanup refused.");
                WindowsDeliveryNative.DeleteHeldFile(_stage.SafeFileHandle);
                _stage.Dispose(); _stage = null;
                return DeliveryFileResult<bool>.Ok(true);
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
            { return DeliveryFileResult<bool>.Fail(DeliveryCode.NeedsReconciliation, ex.Message); }
        }

        public SelectionLeaseResult AcquireSelection(Guid deliveryId, DeliveryFileIdentity expectedIdentity,
            Sha256 expectedHash, long expectedLength)
        {
            DeliveryFinalCheck final = CheckFinal(expectedIdentity);
            if (final.Presence == DeliveryFilePresence.Absent)
                return new(DeliveryAvailability.Missing, null, "Delivered file is missing.");
            if (final.Presence == DeliveryFilePresence.Unavailable)
                return new(DeliveryAvailability.Unavailable, null, final.Detail);
            if (final.Identity != expectedIdentity || final.Hash != expectedHash || final.Length != expectedLength)
                return new(DeliveryAvailability.Changed, null, "Delivered file changed.");
            _transferred = true;
            return new(DeliveryAvailability.VerifiedNow, new SelectionLease(this, deliveryId, FinalPath));
        }

        public void Dispose()
        {
            if (_transferred) return;
            DisposeCore();
        }

        private void DisposeCore()
        {
            if (_disposed) return;
            _disposed = true;
            _final?.Dispose(); _stage?.Dispose(); _source?.Dispose();
            foreach (SafeFileHandle handle in _directories) handle.Dispose();
        }

        private static bool ValidStagingLeaf(string leaf) =>
            leaf.StartsWith(".printflow-", StringComparison.Ordinal) &&
            leaf.EndsWith(".partial", StringComparison.Ordinal) && leaf.Length == 51 &&
            leaf[11..43].All(Uri.IsHexDigit);

        private sealed class SelectionLease(Guard guard, Guid id, string path) : IDeliveredSelectionLease
        {
            public Guid DeliveryId { get; } = id;
            public string FinalPath => IsDisposed ? throw new ObjectDisposedException(nameof(SelectionLease)) : path;
            public bool IsDisposed { get; private set; }
            public void Dispose()
            {
                if (IsDisposed) return;
                IsDisposed = true;
                guard._transferred = false;
                guard.DisposeCore();
            }
        }
    }
}
