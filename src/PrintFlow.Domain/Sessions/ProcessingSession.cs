using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Trimming;

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
    /// <summary>
    /// The margin the next deterministic Trim attempt will run with
    /// (Epic 11200 Part C3 §13).
    /// </summary>
    /// <remarks>
    /// The <i>pending</i> decision, and only that: it says what "Run Trim" would do next, never
    /// what an earlier trim did. The record of what a trim actually did belongs to
    /// <c>ProcessingAttempt.TrimParameters</c>, because this value changes whenever the operator
    /// changes their mind and an audit record must not (§14, §15).
    /// <para>
    /// It lives on the session rather than only in a view model so that "Run Trim uses the
    /// persisted decision" survives a restart, the way the print size and the W1 branch do
    /// (Epic 11100 plan §17.3). Its starting value is <see cref="TrimMargin.Tight"/> — zero
    /// margin, the behaviour every trim has had since Part B — and nothing ever adds a non-zero
    /// safety margin the operator did not ask for (§10).
    /// </para>
    /// </remarks>
    public TrimMargin TrimMargin { get; init; } = TrimMargin.Tight;

    /// <summary>
    /// The reviewed-content authority the next Background Removal attempt would run with
    /// (Epic 11300 Part C2B1 §4, §10).
    /// </summary>
    /// <remarks>
    /// The <i>pending</i> decision, and only that. It says what "Run Background Removal" would
    /// be allowed to do next, never what an earlier attempt did — that belongs to
    /// <c>ProcessingAttempt.BackgroundRemovalAuthority</c>, which is written once and never
    /// rewritten (§11), exactly as the trim parameters are.
    /// <para>
    /// Null is the starting value and the honest spelling of "no decision has been made".
    /// Contrast <see cref="TrimMargin"/>, which does default: a trim margin is an operational
    /// parameter of a deterministic algorithm and zero is the honest zero, whereas authorising
    /// an automatic selection over content is a judgement only a human who looked at that
    /// content can make (MVP design §12).
    /// </para>
    /// <para>
    /// It lives on the session rather than only in a view model so a decision made before a
    /// restart is still there afterwards (§20). It is retained rather than cleared when its
    /// bound Revision is replaced: usability is decided by an exact match against the artefact
    /// Background Removal will actually consume, so a stale record cannot authorise anything
    /// and does not need to be hunted down and deleted (§9).
    /// </para>
    /// </remarks>
    public BackgroundRemovalAuthority? BackgroundRemovalAuthority { get; init; }

    /// <summary>
    /// What <see cref="Dimensions"/> means on this session (Epic 11400 Part B1A.2A §4, §9).
    /// </summary>
    /// <remarks>
    /// Null exactly when <see cref="Dimensions"/> is null. The two move together because the
    /// reading is not metadata <i>about</i> the pair — it is half of what the pair says, and a
    /// size whose reading is unknown is a size nothing may act on.
    /// <para>
    /// Rows written before the maximum-bound migration read back as
    /// <see cref="PrintDimensionSemantics.LegacyExactPair"/>, which is what they were: two
    /// independently exact dimensions. They stay readable for audit and display and are never
    /// executable as a Photoshop plan until the operator reconfirms the limits under the new
    /// semantics (§10).
    /// </para>
    /// </remarks>
    public PrintDimensionSemantics? DimensionSemantics { get; init; }

    /// <summary>
    /// The source-bound plan the next Photoshop attempt would run with
    /// (Epic 11400 Part B1A.2A §5, §11).
    /// </summary>
    /// <remarks>
    /// The <i>pending</i> plan, and only that: it says what "Run Photoshop Output" would do
    /// next, never what an earlier attempt did — that belongs to
    /// <c>ProcessingAttempt.PrintPreparationPlan</c>, which is written once and never rewritten
    /// (§12), exactly as the trim parameters and the background-removal authority are.
    /// <para>
    /// Null on a legacy session and until the operator records maximum bounds. Holding a
    /// non-null value is not the same as being runnable: the plan names the artefact it was
    /// calculated from, and only <c>WorkflowSnapshot.UsablePrintPreparationPlan</c> answers
    /// whether that is still the artefact Photoshop will consume (§7, §15).
    /// </para>
    /// <para>
    /// Retained rather than cleared when its bound Revision is replaced, for the same reason the
    /// background-removal authority is: usability is decided by an exact match, so a stale plan
    /// cannot authorise anything and does not need hunting down (§16).
    /// </para>
    /// </remarks>
    public PrintPreparationPlan? PrintPreparationPlan { get; init; }

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
