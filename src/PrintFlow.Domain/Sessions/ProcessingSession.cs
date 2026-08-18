using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;

namespace PrintFlow.Domain.Sessions;

/// <summary>
/// One single-image processing flow: the aggregate root (MVP design §5.1).
/// </summary>
/// <remarks>
/// An active session has exactly one input image (design principle 1). The workflow type is
/// fixed once any derived Revision exists; from then on the engine rejects
/// <c>SelectWorkflow</c> and the operator must start a new session instead (design §6.1).
/// </remarks>
public sealed record ProcessingSession(
    SessionId Id,
    WorkflowType WorkflowType,
    OutputName OutputName,
    StepKind CurrentStep,
    SessionState State,
    WorkspaceDirRef Workspace,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? HandedOffAtUtc,
    string? HandOffReason,
    DateTimeOffset? AbandonedAtUtc,
    string? AbandonReason,
    PrintDimensions? Dimensions,
    WhiteUnderbaseBranch? WhiteUnderbaseBranch)
{
    /// <summary>Creates a new active session positioned at its first step.</summary>
    public static ProcessingSession Start(
        SessionId id,
        WorkflowType workflowType,
        OutputName outputName,
        WorkspaceDirRef workspace,
        DateTimeOffset nowUtc) =>
        new(id,
            workflowType,
            outputName,
            StepKind.Import,
            SessionState.Active,
            workspace,
            nowUtc,
            nowUtc,
            CompletedAtUtc: null,
            HandedOffAtUtc: null,
            HandOffReason: null,
            AbandonedAtUtc: null,
            AbandonReason: null,
            Dimensions: null,
            WhiteUnderbaseBranch: null);

    /// <summary>True while ordinary workflow progression is legal.</summary>
    public bool IsActive => State == SessionState.Active;
}
