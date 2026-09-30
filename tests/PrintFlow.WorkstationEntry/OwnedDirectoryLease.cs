using System.Text;

namespace PrintFlow.WorkstationEntry;

/// <summary>NTFS nonempty-directory protection, owned by the entry host only.</summary>
public sealed class OwnedDirectoryLease : IDisposable
{
    public const string MarkerName = ".pf-entry-directory-owner";
    private readonly FileStream marker;
    private readonly NativePathLease directory;
    private readonly string token;
    public string Identity { get; }
    public string MarkerPath { get; }
    private OwnedDirectoryLease(FileStream marker, NativePathLease directory, string identity, string path, string token)
    { this.marker = marker; this.directory = directory; Identity = identity; MarkerPath = path; this.token = token; }

    public static OwnedDirectoryLease Acquire(string path, string token, string? expected)
    {
        using NativePathLease restrictive = NativePathLease.RestrictDirectory(path);
        string markerPath = Path.Combine(path, MarkerName);
        FileStream? marker = null;
        NativePathLease? compatible = null;
        try
        {
            if (expected is null)
            {
                marker = new(markerPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
                marker.Write(Encoding.UTF8.GetBytes(token)); marker.Flush(true);
            }
            else
            {
                // Never truncate/adopt an existing marker. Its exact identity is in the owner ledger.
                using NativePathLease check = NativePathLease.ReadFile(markerPath);
                marker = new(markerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            string identity = restrictive.Identity + "|" + NativePathLease.IdentifyExclusiveFile(marker, markerPath);
            if (expected is not null && identity != expected) throw new IOException("Owned directory/marker identity changed.");
            marker.Position = 0;
            using (StreamReader reader = new(marker, Encoding.UTF8, false, leaveOpen: true))
                if (reader.ReadToEnd() != token) throw new IOException("Owned directory marker token mismatch.");
            compatible = NativePathLease.Ancestors(path);
            if (compatible.Identity != restrictive.Identity) throw new IOException("Directory changed during protection handoff.");
            // The marker prevents empty-directory reparsing before the restrictive hold ends.
            return new(marker, compatible, identity, markerPath, token);
        }
        catch { compatible?.Dispose(); marker?.Dispose(); throw; }
    }
    public void Verify()
    {
        using NativePathLease current = NativePathLease.ObserveFile(MarkerPath);
        if (directory.Identity + "|" + current.Identity != Identity) throw new IOException("Owned marker identity changed.");
        marker.Position = 0;
        using StreamReader reader = new(marker, Encoding.UTF8, false, leaveOpen: true);
        if (reader.ReadToEnd() != token) throw new IOException("Owned marker contents changed.");
    }
    public void Dispose() { directory.Dispose(); marker.Dispose(); }
}
