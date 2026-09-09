using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

public interface IDiagnosticPackageService
{
    Task<OperationResult<DiagnosticPackagePlan>> BuildPlanAsync(
        SessionId sessionId,
        AttemptId attemptId,
        CancellationToken cancellationToken);

    Task<OperationResult<DiagnosticPackageExportResult>> ExportAsync(
        DiagnosticPackagePlan plan,
        string requestedDestination,
        CancellationToken cancellationToken);
}

/// <summary>Builds one exact-attempt, default-deny support package plan.</summary>
public sealed class DiagnosticPackageService : IDiagnosticPackageService
{
    private readonly ISessionService _sessions;
    private readonly IEnvironmentDiagnostics _environment;
    private readonly IDiagnosticPackageEvidenceInspector _evidence;
    private readonly IDiagnosticPackageWriter _writer;
    private readonly DiagnosticPackageApplicationInfo _application;
    private readonly DiagnosticPackageStorageLocations _storage;
    private readonly TimeProvider _clock;

    public DiagnosticPackageService(
        ISessionService sessions,
        IEnvironmentDiagnostics environment,
        IDiagnosticPackageEvidenceInspector evidence,
        IDiagnosticPackageWriter writer,
        DiagnosticPackageApplicationInfo application,
        DiagnosticPackageStorageLocations storage,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(clock);
        _sessions = sessions;
        _environment = environment;
        _evidence = evidence;
        _writer = writer;
        _application = application;
        _storage = storage;
        _clock = clock;
    }

    public async Task<OperationResult<DiagnosticPackagePlan>> BuildPlanAsync(
        SessionId sessionId,
        AttemptId attemptId,
        CancellationToken cancellationToken)
    {
        OperationResult<ErrorDetailsView> loaded = await _sessions
            .LoadErrorDetailsAsync(sessionId, attemptId, cancellationToken)
            .ConfigureAwait(false);
        if (loaded.IsFailure)
            return OperationResult.Fail<DiagnosticPackagePlan>(loaded.Failure);

        ErrorDetailsView details = loaded.Value;
        EnvironmentReadinessReport readiness = _environment.Read();
        DiagnosticPackageEvidenceInspection screenshot = details.ScreenshotStatus == DiagnosticImageStatus.Available
            ? _evidence.InspectFailureScreenshot(details.ScreenshotPath)
            : new DiagnosticPackageEvidenceInspection(DiagnosticPackageEvidenceStatus.Unavailable, null);

        DiagnosticPackageItem screenshotItem = screenshot.Status switch
        {
            DiagnosticPackageEvidenceStatus.Available when screenshot.File is not null =>
                DiagnosticPackageItem.IncludedEvidence(
                    DiagnosticPackageItemRole.FailureScreenshot,
                    "failure-screenshot.png",
                    screenshot.File),
            DiagnosticPackageEvidenceStatus.ExcludedByPolicy =>
                DiagnosticPackageItem.ExcludedByPolicy(DiagnosticPackageItemRole.FailureScreenshot),
            _ => DiagnosticPackageItem.Unavailable(DiagnosticPackageItemRole.FailureScreenshot),
        };
        DiagnosticImageStatus packageScreenshotStatus = details.ScreenshotStatus == DiagnosticImageStatus.NotCaptured
            ? DiagnosticImageStatus.NotCaptured
            : screenshot.Status == DiagnosticPackageEvidenceStatus.Available
                ? DiagnosticImageStatus.Available
                : DiagnosticImageStatus.Unavailable;

        DiagnosticPackageLogStatus logStatus = details.LogStatus switch
        {
            DiagnosticLogStatus.Available => DiagnosticPackageLogStatus.Available,
            DiagnosticLogStatus.NotRecorded => DiagnosticPackageLogStatus.NotRecorded,
            _ => DiagnosticPackageLogStatus.Unavailable,
        };

        DiagnosticPackagePlan plan = DiagnosticPackagePlan.Create(
            _clock.GetUtcNow(),
            new DiagnosticPackageSubject(
                details.SessionId,
                details.AttemptId,
                details.ProcessingName,
                details.Workflow,
                details.Step,
                details.AttemptStatus,
                details.Operation,
                details.AdapterId,
                details.StartedAtUtc,
                details.EndedAtUtc,
                details.AttemptNumber,
                details.PreviousRetries,
                details.IsCurrent),
            new DiagnosticPackageFailureFacts(
                details.StableCode,
                details.MessageKey,
                details.TechnicalDetail,
                logStatus,
                details.LogEntryId,
                details.LogAtUtc,
                details.ManagedInputPath,
                details.InputPathStatus,
                details.ExpectedOutputPath,
                details.ExpectedOutputPathStatus,
                details.ScreenshotPath,
                packageScreenshotStatus),
            _application,
            _storage,
            new DiagnosticPackageEnvironment(
                readiness.Verified,
                readiness.PresetIdentity,
                readiness.ObservedAt,
                readiness.Checks.Select(check => new DiagnosticPackageEnvironmentCheck(
                    check.CheckKey, check.Status, check.IsBlocking)).ToArray()),
            [
                DiagnosticPackageItem.GeneratedManifest(),
                DiagnosticPackageItem.IncludedMetadata(DiagnosticPackageItemRole.StructuredFailureSummary),
                logStatus == DiagnosticPackageLogStatus.Available
                    ? DiagnosticPackageItem.IncludedMetadata(DiagnosticPackageItemRole.StructuredAutomationLog)
                    : DiagnosticPackageItem.Unavailable(DiagnosticPackageItemRole.StructuredAutomationLog),
                DiagnosticPackageItem.IncludedMetadata(DiagnosticPackageItemRole.EnvironmentSummary),
                DiagnosticPackageItem.IncludedMetadata(DiagnosticPackageItemRole.ApplicationInfo),
                DiagnosticPackageItem.IncludedMetadata(DiagnosticPackageItemRole.LocalPaths),
                screenshotItem,
                DiagnosticPackageItem.ExcludedByPolicy(DiagnosticPackageItemRole.CustomerSource),
                DiagnosticPackageItem.ExcludedByPolicy(DiagnosticPackageItemRole.InputSnapshot),
                DiagnosticPackageItem.ExcludedByPolicy(DiagnosticPackageItemRole.RevisionArtwork),
                DiagnosticPackageItem.ExcludedByPolicy(DiagnosticPackageItemRole.ApprovedOutput),
                DiagnosticPackageItem.ExcludedByPolicy(DiagnosticPackageItemRole.ProductionOutput),
                DiagnosticPackageItem.ExcludedByPolicy(DiagnosticPackageItemRole.ManualArtwork),
                DiagnosticPackageItem.ExcludedByPolicy(DiagnosticPackageItemRole.RecoveryEvidence),
                DiagnosticPackageItem.ExcludedByPolicy(DiagnosticPackageItemRole.UnknownEvidence),
                DiagnosticPackageItem.ExcludedByPolicy(DiagnosticPackageItemRole.DiagnosticDatabase),
            ]);

        return OperationResult.Ok(plan);
    }

    public Task<OperationResult<DiagnosticPackageExportResult>> ExportAsync(
        DiagnosticPackagePlan plan,
        string requestedDestination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedDestination);
        return _writer.WriteAsync(plan, requestedDestination, cancellationToken);
    }
}
