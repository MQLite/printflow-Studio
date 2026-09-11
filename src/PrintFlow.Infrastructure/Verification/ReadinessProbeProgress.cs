using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Verification;

/// <summary>One bounded probe result builder, local to the lease's complete unwind.</summary>
internal sealed class ReadinessProbeProgress(string operationId, ExternalProcessRef process)
{
    private readonly List<ReadinessProbeStage> _stages = [];
    private readonly List<ReadinessProbeFailure> _secondary = [];
    private ReadinessProbeFailure? _primary;
    private ReadinessProbeStage _attempted;
    private ReadinessProbeStage _confirmed;

    public string? ManagedPath { get; set; }
    public OperationFailure? Failure { get; private set; }
    public ProbeCleanupOutcome CleanupOutcome { get; set; }
    public string CleanupReason { get; set; } = "Cleanup was not reached.";

    public void Observe(ReadinessProbeStage stage)
    {
        // Cancellation may re-enter the exact close. Keep milestones, not a per-input transcript.
        if (!_stages.Contains(stage)) _stages.Add(stage);
        if (stage is ReadinessProbeStage.ProbeCreated or ReadinessProbeStage.OpenConfirmed or
            ReadinessProbeStage.IdentityConfirmed or ReadinessProbeStage.CloseConfirmed or
            ReadinessProbeStage.PriorStateRestored or ReadinessProbeStage.CleanupCompleted)
            _confirmed = stage;
        else
            _attempted = stage;
    }

    public void Fail(string phase, OperationFailure failure)
    {
        ReadinessProbeFailure diagnostic = ProjectFailure(phase, failure);
        if (Failure is null)
        {
            Failure = failure;
            _primary = diagnostic;
        }
        else
        {
            _secondary.Add(diagnostic);
        }
    }

    internal static ReadinessProbeFailure ProjectFailure(string phase, OperationFailure failure) =>
        new(phase, failure.Code, Limit(failure.TechnicalDetail, 1200),
            ContextFlag(failure, "inputSent"), ContextFlag(failure, "confirmPressed"),
            ContextFlag(failure, "discardInvoked"), ContextFlag(failure, "preExistingDialog"));

    // Keep only fixed, already-observed flags. Never copy arbitrary failure context to the report.
    private static string? ContextFlag(OperationFailure failure, string key) =>
        failure.Context.TryGetValue(key, out string? value) && value is "true" or "false" ? value : null;

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max];

    public ReadinessProbeDiagnostics Snapshot() => new(
        operationId, ManagedPath,
        new(process.ProcessId, process.ExecutablePath, process.StartedUtc),
        _stages.ToArray(), _attempted, _confirmed, CleanupOutcome, CleanupReason,
        _primary, _secondary.ToArray());
}
