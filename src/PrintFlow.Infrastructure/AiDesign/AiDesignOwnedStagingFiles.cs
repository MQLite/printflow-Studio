using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Delivery;
using PrintFlow.Infrastructure.Workspace;

namespace PrintFlow.Infrastructure.AiDesign;

/// <summary>Creation-issued authority for this operation's exact staging files, never retention or production disposal.</summary>
public static class AiDesignOwnedStagingFiles
{
    public enum RequestPurpose { RequestAuthority, AttachmentSequence, BridgeQueue }

    internal static OwnedFile CreateAuthoring(string workspaceRoot, Guid? draftId, Guid? versionId)
    {
        string root = AuthoringDirectory(workspaceRoot, draftId);
        if (versionId == Guid.Empty) throw new IOException("Authoring version identity required.");
        PrepareDirectory(root);
        string destination = versionId is null ? "current.json" : $"revision-{versionId:N}.json";
        return OwnedFile.Create(root, System.IO.Path.Combine(root, $".authoring-{Guid.NewGuid():N}.tmp"), System.IO.Path.Combine(root, destination));
    }

    internal static string AuthoringDirectory(string workspaceRoot, Guid? draftId)
    {
        if (draftId == Guid.Empty) throw new IOException("Authoring draft identity required.");
        string root = System.IO.Path.Combine(ValidRoot(workspaceRoot), "AiDesign", "Authoring");
        return draftId is null ? System.IO.Path.Combine(root, "Templates")
            : System.IO.Path.Combine(root, "Drafts", draftId.Value.ToString("D"));
    }

    internal static IDisposable PinAuthoringDirectory(string workspaceRoot, Guid? draftId)
    {
        string root = AuthoringDirectory(workspaceRoot, draftId); PrepareDirectory(root);
        return PinnedParents.Open(root, System.IO.Path.Combine(root, ".lock"));
    }

    internal static IDisposable PinAuthoringDraftsRoot(string workspaceRoot)
    {
        string root = System.IO.Path.Combine(ValidRoot(workspaceRoot), "AiDesign", "Authoring", "Drafts");
        PrepareDirectory(root);
        return PinnedParents.Open(root, System.IO.Path.Combine(root, ".enumeration"));
    }

    internal static async Task PublishAuthoringAsync(OwnedFile version, OwnedFile pointer, CancellationToken ct)
    {
        await version.MoveWithinBatchAsync(ct);
        // Reopen and verify the moved creation identity, then deny write/delete sharing
        // until the authority pointer commits. This guard unwinds before caller rollback.
        using var versionIdentity = version.PinIdentity();
        await pointer.PublishAsync(ct);
        version.Release();
    }

    public static OwnedFile CreateRequest(string workspaceRoot, Guid requestId, RequestPurpose purpose)
        => CreateRequestCore(workspaceRoot, requestId, purpose, Guid.NewGuid());

    internal static OwnedFile CreateRequestForTest(string workspaceRoot, Guid requestId, RequestPurpose purpose, Guid stagingId)
        => CreateRequestCore(workspaceRoot, requestId, purpose, stagingId);

    private static OwnedFile CreateRequestCore(string workspaceRoot, Guid requestId, RequestPurpose purpose, Guid stagingId)
    {
        string root = RequestRoot(workspaceRoot, requestId);
        if (stagingId == Guid.Empty) throw new IOException("Staging identity required.");
        (string prefix, string destination) = purpose switch
        {
            RequestPurpose.RequestAuthority => (".request-", "request.json"),
            RequestPurpose.AttachmentSequence => (".sequence-", ".attachment-sequence"),
            RequestPurpose.BridgeQueue => (".bridge-queue-", "bridge-queue.local.json"),
            _ => throw new IOException("Unknown staging purpose.")
        };
        PrepareDirectory(root);
        return OwnedFile.Create(root, System.IO.Path.Combine(root, $"{prefix}{stagingId:N}.tmp"), System.IO.Path.Combine(root, destination));
    }

    public static OwnedFile CreateConnection(string installationDirectory)
    {
        string root = ValidRoot(installationDirectory);
        return OwnedFile.Create(root, System.IO.Path.Combine(root, $".connection-{Guid.NewGuid():N}.tmp"), System.IO.Path.Combine(root, "connection.local.json"));
    }

    public static AttachmentBatch BeginAttachments(string workspaceRoot, Guid requestId)
        => AttachmentBatch.Begin(RequestRoot(workspaceRoot, requestId));

    private static string RequestRoot(string workspaceRoot, Guid requestId)
    {
        if (requestId == Guid.Empty) throw new IOException("Request identity required.");
        return System.IO.Path.Combine(ValidRoot(workspaceRoot), "AiDesign", "Requests", requestId.ToString("D"));
    }

    private static string ValidRoot(string root)
    {
        if (!System.IO.Path.IsPathFullyQualified(root)) throw new IOException("Absolute owned root required.");
        string full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(root));
        ReparsePointGuard.RefuseAncestry(full);
        if (!Directory.Exists(full)) throw new IOException("Owned root unavailable.");
        return full;
    }

    private static void PrepareDirectory(string path)
    {
        ReparsePointGuard.RefuseAncestry(path);
        Directory.CreateDirectory(path);
        ReparsePointGuard.RefuseAncestry(path);
    }

    private static void CheckPath(string root, string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        if (!full.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Staging escaped its owned scope.");
        ReparsePointGuard.RefuseAncestry(full);
    }

    private readonly record struct Identity(uint Volume, uint High, uint Low)
    {
        public static Identity Read(SafeFileHandle handle)
        {
            if (!NativeMethods.GetFileInformationByHandle(handle, out NativeMethods.FileInformation info)
                || info.NumberOfLinks != 1)
                throw new IOException("Staging has unverifiable or shared file identity.");
            return new(info.VolumeSerialNumber, info.FileIndexHigh, info.FileIndexLow);
        }
    }

    // Pins every parent against rename/reparse replacement while a file is created or mutated.
    // These are ordinary directory handles, never directory disposal authority.
    private sealed class PinnedParents : IDisposable
    {
        private readonly List<SafeFileHandle> _handles = [];
        public SafeFileHandle Parent => _handles[^1];
        public static PinnedParents Open(string root, string file)
        {
            CheckPath(root, file);
            var parents = new PinnedParents();
            try
            {
                string directory = System.IO.Path.GetDirectoryName(file)!;
                var paths = new Stack<string>();
                for (string? current = directory; current is not null; current = System.IO.Path.GetDirectoryName(current)) paths.Push(current);
                foreach (string path in paths)
                {
                    ReparsePointGuard.RefuseAncestry(path);
                    parents._handles.Add(Native(() => WindowsDeliveryNative.OpenDirectory(path)));
                }
                CheckPath(root, file); return parents;
            }
            catch { parents.Dispose(); throw; }
        }
        public void Dispose() { for (int i = _handles.Count - 1; i >= 0; i--) _handles[i].Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInformation
    {
        public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime;
        public uint Attributes;
    }

    private static T Native<T>(Func<T> operation)
    {
        try { return operation(); }
        catch (Exception ex) when (ex is Win32Exception or NotSupportedException)
        { throw new IOException("Owned staging native operation refused.", ex); }
    }

    private static void SetAttributes(SafeFileHandle handle, FileAttributes attributes)
    {
        if (attributes == 0) attributes = FileAttributes.Normal; // Native zero means leave unchanged.
        int size = Marshal.SizeOf<BasicInformation>(); nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(new BasicInformation { Attributes = (uint)attributes }, buffer, false);
            if (!NativeMethods.SetFileInformationByHandle(handle, 0 /* FileBasicInfo */, buffer, (uint)size))
                throw new IOException("Owned staging attributes refused.");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static void RenameReplacingFixedTarget(SafeFileHandle source, SafeFileHandle parent, string leaf)
    {
        byte[] name = Encoding.Unicode.GetBytes(leaf);
        int offset = Marshal.OffsetOf<NativeMethods.FileRenameInformationValue>(nameof(NativeMethods.FileRenameInformationValue.FileName)).ToInt32();
        int size = checked(Marshal.SizeOf<NativeMethods.FileRenameInformationValue>() + name.Length);
        nint buffer = Marshal.AllocHGlobal(size); bool referenced = false;
        try
        {
            parent.DangerousAddRef(ref referenced); Marshal.Copy(new byte[size], 0, buffer, size);
            Marshal.StructureToPtr(new NativeMethods.FileRenameInformationValue
            { Flags = 1 /* ReplaceIfExists, only the purpose-derived authority destination */, RootDirectory = parent.DangerousGetHandle(), FileNameLength = (uint)name.Length }, buffer, false);
            Marshal.Copy(name, 0, buffer + offset, name.Length);
            if (NativeMethods.NtSetInformationFile(source, out _, buffer, (uint)size, 10 /* FileRenameInformation */) != 0)
                throw new IOException("Owned staging atomic publication refused.");
        }
        finally { if (referenced) parent.DangerousRelease(); Marshal.FreeHGlobal(buffer); }
    }

    public sealed class OwnedFile : IDisposable
    {
        private readonly string _root, _destination;
        private readonly Identity _identity;
        private FileStream? _output;
        private bool _owned = true;
        public string Path { get; private set; }
        public bool CleanupRefused { get; private set; }
        internal Action? AfterVerificationForTest { get; set; }
        public FileStream Output => _output ?? throw new InvalidOperationException("Staging writer is closed.");

        private OwnedFile(string root, string path, string destination, FileStream output, Identity identity)
        { _root = root; Path = path; _destination = destination; _output = output; _identity = identity; }

        internal static OwnedFile Create(string root, string path, string destination)
        {
            CheckPath(root, path); CheckPath(root, destination);
            using var parents = PinnedParents.Open(root, path);
            // Never create authority for an existing pathname, including a failed CreateNew.
            var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            try { return new(root, path, destination, output, Identity.Read(output.SafeFileHandle)); }
            catch { output.Dispose(); throw; } // Unverifiable creation is preserved, not adopted for cleanup.
        }

        public async Task CloseFlushedAsync(CancellationToken ct)
        {
            if (_output is null) return;
            await _output.FlushAsync(ct); _output.Flush(true);
            await _output.DisposeAsync(); _output = null;
        }

        public async Task PublishAsync(CancellationToken ct)
        {
            if (!_owned) throw new InvalidOperationException("Staging authority already consumed.");
            await CloseFlushedAsync(ct); ct.ThrowIfCancellationRequested();
            using var parents = PinnedParents.Open(_root, Path);
            using var verified = VerifyIdentity();
            CheckPath(_root, _destination);
            RenameReplacingFixedTarget(verified, parents.Parent, System.IO.Path.GetFileName(_destination));
            _owned = false; // Published authority is never cleanup authority.
        }

        internal async Task MoveWithinBatchAsync(CancellationToken ct)
        {
            if (!_owned) throw new InvalidOperationException("Attachment authority already consumed.");
            await CloseFlushedAsync(ct); ct.ThrowIfCancellationRequested();
            using var parents = PinnedParents.Open(_root, Path);
            using var verified = VerifyIdentity();
            CheckPath(_root, _destination);
            Native(() => { WindowsDeliveryNative.RenameNoReplace(verified, parents.Parent, System.IO.Path.GetFileName(_destination)); return true; });
            Path = _destination; // Same creation identity remains owned until the whole batch commits.
        }

        internal void MarkReadOnly()
        {
            if (!_owned) throw new InvalidOperationException("Attachment authority already consumed.");
            using var parents = PinnedParents.Open(_root, Path);
            using var verified = VerifyIdentity();
            if (!NativeMethods.GetFileInformationByHandle(verified, out NativeMethods.FileInformation info)) throw new IOException("Owned staging attributes unavailable.");
            SetAttributes(verified, (FileAttributes)info.Attributes | FileAttributes.ReadOnly);
        }

        private SafeFileHandle VerifyIdentity()
        {
            CheckPath(_root, Path);
            const uint writeAttributes = 0x100;
            // Attribute access supports readonly rollback. Denying external delete/write sharing
            // keeps this same identity pinned until held rename/disposition/attributes complete.
            var current = NativeMethods.CreateFileW(Path, WindowsDeliveryNative.DeleteAccess | WindowsDeliveryNative.ReadAttributes | writeAttributes,
                WindowsDeliveryNative.ShareRead, 0, WindowsDeliveryNative.OpenExisting, WindowsDeliveryNative.OpenReparsePoint, 0);
            if (current.IsInvalid) { current.Dispose(); throw new IOException("Owned staging handle unavailable."); }
            try
            {
                if (!NativeMethods.GetFileInformationByHandle(current, out NativeMethods.FileInformation info)
                    || ((FileAttributes)info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Device)) != 0)
                    throw new IOException("Regular staging file required.");
                if (Identity.Read(current) != _identity) throw new IOException("Staging file was replaced.");
                AfterVerificationForTest?.Invoke();
                return current;
            }
            catch { current.Dispose(); throw; }
        }

        internal IDisposable PinIdentity() => VerifyIdentity();

        internal void Release() => _owned = false;

        public void Dispose()
        {
            try
            {
                _output?.Dispose(); _output = null;
                if (!_owned) return;
                CheckPath(_root, Path);
                if (!File.Exists(Path)) return;
                using var parents = PinnedParents.Open(_root, Path);
                using var verified = VerifyIdentity();
                if (!NativeMethods.GetFileInformationByHandle(verified, out NativeMethods.FileInformation info)) throw new IOException("Owned staging attributes unavailable.");
                FileAttributes attributes = (FileAttributes)info.Attributes;
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    SetAttributes(verified, attributes & ~FileAttributes.ReadOnly);
                }
                CheckPath(_root, Path);
                if (Identity.Read(verified) != _identity) throw new IOException("Staging identity changed.");
                Native(() => { WindowsDeliveryNative.DeleteHeldFile(verified); return true; });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
            { CleanupRefused = true; } // Preserve the original caller failure and every refused file.
            finally { _owned = false; }
        }
    }

    public sealed class AttachmentBatch : IDisposable
    {
        private readonly string _requestRoot;
        private readonly List<OwnedFile> _created = [];
        private bool _finished;
        private AttachmentBatch(string root) => _requestRoot = root;
        internal static AttachmentBatch Begin(string root) { PrepareDirectory(root); return new(root); }

        public Attachment Create(int order, string extension)
        {
            if (_finished) throw new InvalidOperationException("Attachment batch is finished.");
            if (order <= 0 || extension is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".pdf" or ".txt" or ".md" or ".csv" or ".docx" or ".xlsx" or ".pptx"))
                throw new IOException("Invalid attachment staging identity.");
            Guid id = Guid.NewGuid(); string name = $"attachment-{order:D2}{extension}";
            string folder = System.IO.Path.Combine(_requestRoot, "Attachments", id.ToString("N"));
            PrepareDirectory(folder);
            var snapshot = OwnedFile.Create(folder, System.IO.Path.Combine(folder, "snapshot.tmp"), System.IO.Path.Combine(folder, name));
            _created.Add(snapshot);
            return Attachment.Begin(this, id, name, folder, snapshot);
        }

        internal OwnedFile CreateIdentity(string folder)
        {
            if (_finished) throw new InvalidOperationException("Attachment batch is finished.");
            CheckPath(_requestRoot, folder);
            var seal = OwnedFile.Create(folder, System.IO.Path.Combine(folder, "identity.json"), System.IO.Path.Combine(folder, "identity.json"));
            _created.Add(seal); return seal;
        }

        public void Commit()
        {
            if (_finished) throw new InvalidOperationException("Attachment batch is finished.");
            foreach (var file in _created) file.Release();
            _finished = true;
        }

        public void Dispose()
        {
            foreach (var file in _created) file.Dispose();
            _finished = true;
        }
    }

    public sealed class Attachment
    {
        private readonly AttachmentBatch _batch;
        private readonly string _folder;
        private bool _identityCreated;
        public Guid Id { get; }
        public string UploadName { get; }
        public string RelativePath => $"Attachments/{Id:N}/{UploadName}";
        public OwnedFile Snapshot { get; }
        private Attachment(AttachmentBatch batch, Guid id, string name, string folder, OwnedFile snapshot)
        { _batch = batch; Id = id; UploadName = name; _folder = folder; Snapshot = snapshot; }
        internal static Attachment Begin(AttachmentBatch batch, Guid id, string name, string folder, OwnedFile snapshot) => new(batch, id, name, folder, snapshot);
        public OwnedFile CreateIdentity()
        {
            if (_identityCreated) throw new InvalidOperationException("Attachment identity already created.");
            var identity = _batch.CreateIdentity(_folder); _identityCreated = true; return identity;
        }
        public Task PublishSnapshotAsync(CancellationToken ct) => Snapshot.MoveWithinBatchAsync(ct);
        public void MarkReadOnly() => Snapshot.MarkReadOnly();
    }
}
