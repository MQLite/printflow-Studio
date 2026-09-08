using PrintFlow.Domain.Automation;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Settings;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

public sealed record DiagnosticRetentionOptions(
    string EvidenceRoot,
    int ConfiguredRetentionDays,
    int ProductDefaultRetentionDays,
    int MaximumConfiguredRetentionDays,
    int BatchSize = 128);

public sealed record DiagnosticRetentionReport(
    int ExpiredDatabaseDiagnostics,
    int DeletedDiagnosticFiles,
    int MissingDiagnosticFiles,
    int PreservedDiagnostics,
    OperationFailure? Warning)
{
    public bool Succeeded => Warning is null;
}

public interface IDiagnosticRetentionService
{
    Task<DiagnosticRetentionReport> MaintainAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Startup-bounded maintenance for local structured diagnostics and their screenshots. It never
/// acquires automation ownership and never calls an external application.
/// </summary>
public sealed class DiagnosticRetentionService : IDiagnosticRetentionService
{
    private readonly IDiagnosticRetentionRepository _diagnostics;
    private readonly ISessionRepository _sessions;
    private readonly ISettingsRepository _settings;
    private readonly IDiagnosticFileStore _files;
    private readonly DiagnosticRetentionOptions _options;
    private readonly TimeProvider _clock;

    public DiagnosticRetentionService(
        IDiagnosticRetentionRepository diagnostics,
        ISessionRepository sessions,
        ISettingsRepository settings,
        IDiagnosticFileStore files,
        DiagnosticRetentionOptions options,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        _diagnostics = diagnostics;
        _sessions = sessions;
        _settings = settings;
        _files = files;
        _options = options;
        _clock = clock;
    }

    public async Task<DiagnosticRetentionReport> MaintainAsync(CancellationToken cancellationToken)
    {
        OperationResult<AutomationLockState> automationLock =
            await _sessions.GetAutomationLockAsync(cancellationToken);
        if (automationLock.IsFailure)
            return Warning(automationLock.Failure, preserved: 0);
        if (automationLock.Value.IsHeld)
            return Warning(
                OperationFailure.Create(
                    FailureCode.AdapterUnavailable,
                    "Diagnostic retention was deferred because automation ownership remains held after recovery."),
                preserved: 0);

        OperationResult<SettingEntry?> setting =
            await _settings.ReadAsync(SettingKey.LogRetentionDays, cancellationToken);
        if (setting.IsFailure)
            return Warning(setting.Failure, preserved: 0);

        int retentionDays = ResolveDays(setting.Value);
        DateTimeOffset cutoff = _clock.GetUtcNow().Subtract(TimeSpan.FromDays(retentionDays));
        DiagnosticRetentionCursor? cursor = null;
        int expired = 0;
        int deleted = 0;
        int missing = 0;
        int preserved = 0;
        List<string> warnings = [];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OperationResult<DiagnosticRetentionBatch> read = await _diagnostics.ReadExpiredBatchAsync(
                cutoff, cursor, _options.BatchSize, cancellationToken);
            if (read.IsFailure)
                return Report(read.Failure, expired, deleted, missing, preserved);
            if (read.Value.Entries.Count == 0) break;

            string[] paths = read.Value.Entries
                .Select(entry => entry.ScreenshotPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            OperationResult<IReadOnlyList<DiagnosticFileReference>> references =
                await _diagnostics.ReadFileReferencesAsync(paths, cancellationToken);
            if (references.IsFailure)
                return Report(references.Failure, expired, deleted, missing, preserved);

            OperationResult<IReadOnlyList<string>> authorities =
                await _diagnostics.FindAuthoritativePathsAsync(paths, cancellationToken);
            if (authorities.IsFailure)
                return Report(authorities.Failure, expired, deleted, missing, preserved);

            OperationResult<IReadOnlyDictionary<SessionId, SessionAggregate?>> sessions =
                await LoadSessionsAsync(read.Value.Entries, references.Value, cancellationToken);
            if (sessions.IsFailure)
                return Report(sessions.Failure, expired, deleted, missing, preserved);

            DiagnosticRetentionPlan plan = DiagnosticRetentionPlan.Create(
                _options.EvidenceRoot,
                cutoff,
                read.Value.Entries,
                references.Value,
                sessions.Value,
                authorities.Value);

            HashSet<AutomationLogId> expireIds = plan.ExpireDatabaseDiagnostic.ToHashSet();
            preserved += plan.Keep.Count;

            foreach ((string path, IReadOnlyList<AutomationLogId> ids) in plan.DeleteDiagnosticFile)
            {
                OperationResult<DiagnosticFileDeletion> removal =
                    await _files.DeleteExpiredAsync(path, cutoff, cancellationToken);
                if (removal.IsFailure)
                {
                    warnings.Add(removal.Failure.TechnicalDetail);
                    preserved += ids.Count;
                    continue;
                }

                switch (removal.Value)
                {
                    case DiagnosticFileDeletion.Deleted:
                        deleted++;
                        expireIds.UnionWith(ids);
                        break;
                    case DiagnosticFileDeletion.Missing:
                        missing++;
                        expireIds.UnionWith(ids);
                        break;
                    case DiagnosticFileDeletion.Preserved:
                        preserved += ids.Count;
                        break;
                }
            }

            OperationResult<Unit> expiry = await _diagnostics.ExpireAsync(
                expireIds.ToList(), cutoff, cancellationToken);
            if (expiry.IsFailure)
                return Report(expiry.Failure, expired, deleted, missing, preserved);
            expired += expireIds.Count;

            cursor = read.Value.Next;
            if (cursor is null) break;
        }

        OperationFailure? warning = warnings.Count == 0
            ? null
            : OperationFailure.Create(
                FailureCode.WorkspaceError,
                $"Diagnostic retention preserved {warnings.Count} file operation(s) that could not be completed.");
        return new DiagnosticRetentionReport(expired, deleted, missing, preserved, warning);
    }

    private async Task<OperationResult<IReadOnlyDictionary<SessionId, SessionAggregate?>>> LoadSessionsAsync(
        IReadOnlyList<AutomationLogEntry> entries,
        IReadOnlyList<DiagnosticFileReference> references,
        CancellationToken cancellationToken)
    {
        SessionId[] ids = entries.Select(entry => entry.SessionId)
            .Concat(references.Select(reference => reference.SessionId))
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        Dictionary<SessionId, SessionAggregate?> result = [];
        foreach (SessionId id in ids)
        {
            OperationResult<SessionAggregate?> loaded = await _sessions.LoadAsync(id, cancellationToken);
            if (loaded.IsFailure)
                return OperationResult.Fail<IReadOnlyDictionary<SessionId, SessionAggregate?>>(loaded.Failure);
            result[id] = loaded.Value;
        }
        return OperationResult.Ok<IReadOnlyDictionary<SessionId, SessionAggregate?>>(result);
    }

    private int ResolveDays(SettingEntry? persisted)
    {
        if (persisted?.AsInteger() is { } days &&
            days >= 1 &&
            days <= _options.MaximumConfiguredRetentionDays) return days;
        if (_options.ConfiguredRetentionDays is >= 1 &&
            _options.ConfiguredRetentionDays <= _options.MaximumConfiguredRetentionDays)
            return _options.ConfiguredRetentionDays;
        return _options.ProductDefaultRetentionDays;
    }

    private static DiagnosticRetentionReport Warning(OperationFailure failure, int preserved) =>
        new(0, 0, 0, preserved, failure);

    private static DiagnosticRetentionReport Report(
        OperationFailure failure, int expired, int deleted, int missing, int preserved) =>
        new(expired, deleted, missing, preserved, failure);
}
