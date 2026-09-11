using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Ports;

/// <summary>Bounded milestones, not an input transcript or an authorization capability.</summary>
public enum ReadinessProbeStage
{
    None, ProbeCreation, ProbeCreated, OpenGuard, OpenRequested, OpenConfirmed,
    IdentityCheck, IdentityConfirmed, CloseGuard, CloseRequested, CloseConfirmed,
    PriorStateCheck, PriorStateRestored, CleanupAttempted, CleanupCompleted,
}

public enum ProbeCleanupOutcome { NotRun, Succeeded, Failed, Unknown }

public sealed record ReadinessProcessIdentity(int ProcessId, string ExecutablePath, DateTimeOffset? StartedAt);

/// <summary>Only selected failure facts; never arbitrary window/document inventories.</summary>
public sealed record ReadinessProbeFailure(
    string Phase, FailureCode Code, string Detail, string? InputSent, string? ConfirmPressed,
    string? DiscardInvoked = null, string? PreExistingDialog = null);

public sealed record ReadinessProbeDiagnostics(
    string OperationId,
    string? ManagedPath,
    ReadinessProcessIdentity Process,
    IReadOnlyList<ReadinessProbeStage> Stages,
    ReadinessProbeStage LastAttemptedStage,
    ReadinessProbeStage LastConfirmedStage,
    ProbeCleanupOutcome CleanupOutcome,
    string CleanupReason,
    ReadinessProbeFailure? PrimaryFailure,
    IReadOnlyList<ReadinessProbeFailure> SecondaryFailures);

/// <summary>Historical observations are never a cached admission decision.</summary>
public sealed record ReadinessEvidenceLifecycle(
    DateTimeOffset? LastSuccessfulLiveAt,
    ReadinessProcessIdentity? Meitu,
    ReadinessProcessIdentity? Photoshop,
    bool EvidenceAvailable,
    bool CurrentObservationDeferred,
    DateTimeOffset? LatestAttemptAt,
    ReadinessProbeDiagnostics? LatestProbe);
