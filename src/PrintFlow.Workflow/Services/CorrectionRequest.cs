using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Sessions;

namespace PrintFlow.Workflow.Services;

/// <summary>Where a colleague-correction request is in its life (SCRUM-11148, design §4.2).</summary>
public enum CorrectionRequestStatus
{
    /// <summary>Recorded; its files are not yet verified. Inert: grants nothing.</summary>
    Preparing,

    /// <summary>Files verified and the session handed off. Grants import only while eligible.</summary>
    Ready,

    /// <summary>A corrected picture was imported as <see cref="CorrectionRequest.ResultRevisionId"/>.</summary>
    Returned,

    /// <summary>Replaced by a later request. History only.</summary>
    Superseded,
}

/// <summary>
/// One persisted request to have a colleague correct a background-removal result
/// (SCRUM-11148, owner decision D1).
/// </summary>
/// <remarks>
/// Binds the exact result handed out (R) and its input (U) by id <i>and</i> hash, the managed
/// folder and file names, and the reason recorded on the session. The row is evidence and binding
/// only: whether a <see cref="CorrectionRequestStatus.Ready"/> row still grants an import is
/// decided by <see cref="CorrectionRequestEligibility"/>, never by the row alone, and never by a
/// file name, a folder or <c>HandOffReason</c> text.
/// </remarks>
public sealed record CorrectionRequest(
    Guid Id,
    SessionId SessionId,
    StepKind StepKind,
    RevisionId HandedOutRevisionId,
    Sha256 HandedOutSha256,
    RevisionId ReferenceRevisionId,
    Sha256 ReferenceSha256,
    WorkspaceDirRef Folder,
    string ReferenceFileName,
    string WorkingFileName,
    string SuggestedReturnName,
    string? Note,
    string EffectiveReason,
    CorrectionRequestStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadyAtUtc,
    DateTimeOffset? ClosedAtUtc,
    AttemptId? LastImportAttemptId,
    RevisionId? ResultRevisionId)
{
    /// <summary>Whether the row is PREPARING or READY — storage "open", not eligibility.</summary>
    public bool IsOpen => Status is CorrectionRequestStatus.Preparing or CorrectionRequestStatus.Ready;
}

/// <summary>
/// The operator-language names a new correction package uses, supplied by the screen once per
/// request and then persisted in the row, never re-localized (SCRUM-11148, design §5.2).
/// </summary>
/// <remarks>
/// Each pattern has one <c>{0}</c> for the job's output name; the service appends the extension.
/// <paramref name="Instructions"/> is the optional <c>Instructions.txt</c> text, already complete.
/// </remarks>
public sealed record CorrectionFileNaming(
    string ReferencePattern,
    string WorkingPattern,
    string ReturnPattern,
    string? Instructions = null)
{
    /// <summary>The English names, for callers with no operator language of their own.</summary>
    public static CorrectionFileNaming English { get; } = new(
        "{0} - REFERENCE (do not edit)", "{0} - CORRECT THIS", "{0} - CORRECTED");
}

/// <summary>
/// One change to a <see cref="CorrectionRequest"/> row, committed inside the same transaction as the
/// session, step and attempt changes it belongs to.
/// </summary>
/// <remarks>
/// Every change except <see cref="Insert"/> is a <b>conditional</b> update: the repository applies it
/// only when the row is in the state the change expects, and a change that matches no row rolls the
/// whole commit back — the same rule the automation-lock row follows. <see cref="AssertBound"/> writes
/// nothing at all: it exists so a closing transaction can only commit while the request it re-hands
/// off for is still exactly the one bound to the attempt being closed.
/// </remarks>
public abstract record CorrectionRequestChange(Guid RequestId)
{
    /// <summary>Records a new request as PREPARING.</summary>
    public sealed record Insert(CorrectionRequest Row) : CorrectionRequestChange(Row.Id);

    /// <summary>PREPARING or READY → SUPERSEDED, when a later request replaces an obsolete one.</summary>
    public sealed record Supersede(Guid Id, DateTimeOffset AtUtc) : CorrectionRequestChange(Id);

    /// <summary>PREPARING → READY, in the same commit that hands the session off.</summary>
    public sealed record MarkReady(Guid Id, DateTimeOffset AtUtc) : CorrectionRequestChange(Id);

    /// <summary>A READY row records the import attempt just opened.</summary>
    public sealed record SetLastImportAttempt(Guid Id, AttemptId Attempt) : CorrectionRequestChange(Id);

    /// <summary>
    /// Asserts the row is READY and bound to <paramref name="Attempt"/>; changes nothing
    /// (addendum §3.3). A mismatch rolls the closing transaction back.
    /// </summary>
    public sealed record AssertBound(Guid Id, AttemptId Attempt) : CorrectionRequestChange(Id);

    /// <summary>READY and bound to <paramref name="Attempt"/> → RETURNED with the imported Revision.</summary>
    public sealed record MarkReturned(Guid Id, AttemptId Attempt, RevisionId Result, DateTimeOffset AtUtc)
        : CorrectionRequestChange(Id);

    /// <summary>
    /// The row as it stands after this change, for the in-memory aggregate a live path keeps after
    /// its own commit. Returns the row unchanged when the change does not apply, exactly as the
    /// repository would refuse it.
    /// </summary>
    public CorrectionRequest? ApplyTo(CorrectionRequest? row) => (this, row) switch
    {
        (Insert insert, null) => insert.Row,
        (Supersede s, { IsOpen: true } open) => open with
        {
            Status = CorrectionRequestStatus.Superseded, ClosedAtUtc = s.AtUtc,
        },
        (MarkReady r, { Status: CorrectionRequestStatus.Preparing } preparing) => preparing with
        {
            Status = CorrectionRequestStatus.Ready, ReadyAtUtc = r.AtUtc,
        },
        (SetLastImportAttempt a, { Status: CorrectionRequestStatus.Ready } ready) => ready with
        {
            LastImportAttemptId = a.Attempt,
        },
        (MarkReturned m, { Status: CorrectionRequestStatus.Ready } ready) when ready.LastImportAttemptId == m.Attempt =>
            ready with
            {
                Status = CorrectionRequestStatus.Returned, ResultRevisionId = m.Result, ClosedAtUtc = m.AtUtc,
            },
        _ => row,
    };

    /// <summary>Applies <paramref name="changes"/> to <paramref name="rows"/>, in order.</summary>
    public static IReadOnlyList<CorrectionRequest> ApplyAll(
        IReadOnlyList<CorrectionRequest> rows, IReadOnlyList<CorrectionRequestChange> changes)
    {
        if (changes.Count == 0)
        {
            return rows;
        }

        List<CorrectionRequest> result = [.. rows];
        foreach (CorrectionRequestChange change in changes)
        {
            int index = result.FindIndex(row => row.Id == change.RequestId);
            CorrectionRequest? updated = change.ApplyTo(index < 0 ? null : result[index]);
            if (updated is null)
            {
                continue;
            }

            if (index < 0)
            {
                result.Add(updated);
            }
            else
            {
                result[index] = updated;
            }
        }

        return result;
    }
}
