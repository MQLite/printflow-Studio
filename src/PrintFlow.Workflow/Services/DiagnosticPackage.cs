using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

/// <summary>The closed vocabulary of content a support package can describe.</summary>
public enum DiagnosticPackageItemRole
{
    Manifest,
    StructuredFailureSummary,
    StructuredAutomationLog,
    EnvironmentSummary,
    ApplicationInfo,
    LocalPaths,
    FailureScreenshot,
    CustomerSource,
    InputSnapshot,
    RevisionArtwork,
    ApprovedOutput,
    ProductionOutput,
    ManualArtwork,
    RecoveryEvidence,
    UnknownEvidence,
    DiagnosticDatabase,
}

public enum DiagnosticPackageItemDisposition
{
    Included,
    Unavailable,
    ExcludedByPolicy,
    ExcludedByOperator,
}

public enum DiagnosticPackageItemContent
{
    None,
    GeneratedManifest,
    ManifestMetadata,
    EvidenceFile,
}

/// <summary>Identity facts rechecked immediately before a planned evidence file is read.</summary>
public sealed record DiagnosticPackageFileSnapshot(
    string CanonicalPath,
    long Length,
    DateTimeOffset LastWriteTimeUtc);

/// <summary>One preview row and, when applicable, one exact archive entry.</summary>
public sealed record DiagnosticPackageItem
{
    private DiagnosticPackageItem(
        DiagnosticPackageItemRole role,
        DiagnosticPackageItemDisposition disposition,
        DiagnosticPackageItemContent content,
        string? archiveEntryName,
        DiagnosticPackageFileSnapshot? file)
    {
        Role = role;
        Disposition = disposition;
        Content = content;
        ArchiveEntryName = archiveEntryName;
        File = file;
    }

    public DiagnosticPackageItemRole Role { get; }
    public DiagnosticPackageItemDisposition Disposition { get; }
    public DiagnosticPackageItemContent Content { get; }
    public string? ArchiveEntryName { get; }
    public DiagnosticPackageFileSnapshot? File { get; }

    public static DiagnosticPackageItem GeneratedManifest() =>
        new(DiagnosticPackageItemRole.Manifest, DiagnosticPackageItemDisposition.Included,
            DiagnosticPackageItemContent.GeneratedManifest, "manifest.txt", null);

    public static DiagnosticPackageItem IncludedMetadata(DiagnosticPackageItemRole role) =>
        new(role, DiagnosticPackageItemDisposition.Included,
            DiagnosticPackageItemContent.ManifestMetadata, null, null);

    public static DiagnosticPackageItem IncludedEvidence(
        DiagnosticPackageItemRole role, string archiveEntryName, DiagnosticPackageFileSnapshot file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveEntryName);
        ArgumentNullException.ThrowIfNull(file);
        return new(role, DiagnosticPackageItemDisposition.Included,
            DiagnosticPackageItemContent.EvidenceFile, archiveEntryName, file);
    }

    public static DiagnosticPackageItem Unavailable(DiagnosticPackageItemRole role) =>
        new(role, DiagnosticPackageItemDisposition.Unavailable,
            DiagnosticPackageItemContent.None, null, null);

    public static DiagnosticPackageItem ExcludedByPolicy(DiagnosticPackageItemRole role) =>
        new(role, DiagnosticPackageItemDisposition.ExcludedByPolicy,
            DiagnosticPackageItemContent.None, null, null);
}

public sealed record DiagnosticPackageSubject(
    SessionId SessionId,
    AttemptId AttemptId,
    string ProcessingName,
    WorkflowType Workflow,
    StepKind Step,
    AttemptStatus AttemptStatus,
    OperationKind Operation,
    string AdapterId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    int AttemptNumber,
    int PreviousRetries,
    bool IsCurrent);

public enum DiagnosticPackageLogStatus
{
    Available,
    Unavailable,
    NotRecorded,
}

public sealed record DiagnosticPackageFailureFacts(
    string? StableCode,
    string? MessageKey,
    string? TechnicalDetail,
    DiagnosticPackageLogStatus LogStatus,
    AutomationLogId? LogEntryId,
    DateTimeOffset? LogAtUtc,
    string? ManagedInputPath,
    ErrorPathStatus InputPathStatus,
    string? ExpectedOutputPath,
    ErrorPathStatus ExpectedOutputPathStatus,
    string? ScreenshotPath,
    DiagnosticImageStatus ScreenshotStatus);

public sealed record DiagnosticPackageApplicationInfo(string Name, string Version);

public sealed record DiagnosticPackageStorageLocations(string LocalLogLocation, string ScreenshotLocation);

public sealed record DiagnosticPackageEnvironmentCheck(
    string CheckKey,
    EnvironmentCheckStatus Status,
    bool IsBlocking);

public sealed record DiagnosticPackageEnvironment(
    bool Verified,
    string? PresetIdentity,
    DateTimeOffset ObservedAt,
    IReadOnlyList<DiagnosticPackageEnvironmentCheck> Checks);

/// <summary>
/// The sole package authority shared by preview and writer. It contains no directory or wildcard.
/// </summary>
public sealed class DiagnosticPackagePlan
{
    private DiagnosticPackagePlan(
        DateTimeOffset createdAtUtc,
        DiagnosticPackageSubject subject,
        DiagnosticPackageFailureFacts failure,
        DiagnosticPackageApplicationInfo application,
        DiagnosticPackageStorageLocations storage,
        DiagnosticPackageEnvironment environment,
        IReadOnlyList<DiagnosticPackageItem> items)
    {
        CreatedAtUtc = createdAtUtc;
        Subject = subject;
        Failure = failure;
        Application = application;
        Storage = storage;
        Environment = environment with
        {
            Checks = Array.AsReadOnly(environment.Checks.ToArray()),
        };
        Items = Array.AsReadOnly(items.ToArray());
    }

    public DateTimeOffset CreatedAtUtc { get; }
    public DiagnosticPackageSubject Subject { get; }
    public DiagnosticPackageFailureFacts Failure { get; }
    public DiagnosticPackageApplicationInfo Application { get; }
    public DiagnosticPackageStorageLocations Storage { get; }
    public DiagnosticPackageEnvironment Environment { get; }
    public IReadOnlyList<DiagnosticPackageItem> Items { get; }

    public IReadOnlyList<string> ArchiveEntryNames => Items
        .Where(item => item.Disposition == DiagnosticPackageItemDisposition.Included &&
                       item.Content is DiagnosticPackageItemContent.GeneratedManifest or
                           DiagnosticPackageItemContent.EvidenceFile)
        .Select(item => item.ArchiveEntryName!)
        .ToArray();

    public string SuggestedFileName =>
        $"PrintFlow-Diagnostics-{CreatedAtUtc:yyyyMMdd-HHmmss}-{Subject.AttemptId.ToString()[..8]}.zip";

    public static DiagnosticPackagePlan Create(
        DateTimeOffset createdAtUtc,
        DiagnosticPackageSubject subject,
        DiagnosticPackageFailureFacts failure,
        DiagnosticPackageApplicationInfo application,
        DiagnosticPackageStorageLocations storage,
        DiagnosticPackageEnvironment environment,
        IReadOnlyList<DiagnosticPackageItem> items)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(items);

        DiagnosticPackageItem[] manifest = items.Where(item =>
            item.Role == DiagnosticPackageItemRole.Manifest).ToArray();
        if (manifest is not [{ Disposition: DiagnosticPackageItemDisposition.Included,
                               Content: DiagnosticPackageItemContent.GeneratedManifest,
                               ArchiveEntryName: "manifest.txt" }])
        {
            throw new ArgumentException("A diagnostic package has exactly one generated manifest.txt.", nameof(items));
        }

        if (items.GroupBy(item => item.Role).Any(group => group.Count() != 1))
            throw new ArgumentException("Each diagnostic package role may appear exactly once.", nameof(items));

        if (items.Any(item => item.Content == DiagnosticPackageItemContent.EvidenceFile &&
                              (item.Role != DiagnosticPackageItemRole.FailureScreenshot ||
                               item.Disposition != DiagnosticPackageItemDisposition.Included ||
                               item.ArchiveEntryName != "failure-screenshot.png" || item.File is null)))
        {
            throw new ArgumentException("Only the exact planned failure screenshot may be an evidence file.", nameof(items));
        }

        string[] entries = items.Where(item => item.ArchiveEntryName is not null)
            .Select(item => item.ArchiveEntryName!).ToArray();
        if (entries.Distinct(StringComparer.Ordinal).Count() != entries.Length)
            throw new ArgumentException("Diagnostic package archive entry names must be unique.", nameof(items));

        return new DiagnosticPackagePlan(createdAtUtc, subject, failure, application, storage,
            environment, items);
    }
}

public sealed record DiagnosticPackageExportResult(string SavedPath, IReadOnlyList<string> ArchiveEntryNames);
