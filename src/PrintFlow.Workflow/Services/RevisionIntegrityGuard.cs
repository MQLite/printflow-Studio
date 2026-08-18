using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

/// <summary>
/// Re-verifies a <see cref="Revision"/> against the actual bytes on disk before anything is
/// allowed to consume it (Epic 11100 Task 11105; plan §10.3).
/// </summary>
/// <remarks>
/// Approval is never a mutable flag on a file — it is the derived predicate
/// <c>revision.IsValid AND latest ReviewDecision = Approved AND ReviewedSha256 == SHA-256(current bytes)</c>.
/// The cached <see cref="Revision.ReviewState"/> exists only for list/query performance; it is
/// never sufficient proof before consumption. This type performs the one check that makes that
/// true: it re-reads the file and re-hashes it, every time, rather than trusting anything
/// recorded earlier.
/// </remarks>
public sealed class RevisionIntegrityGuard
{
    private readonly IWorkspace _workspace;
    private readonly IFileInspector _fileInspector;

    public RevisionIntegrityGuard(IWorkspace workspace, IFileInspector fileInspector)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(fileInspector);
        _workspace = workspace;
        _fileInspector = fileInspector;
    }

    /// <summary>
    /// Re-reads and re-hashes <paramref name="revision"/>'s file and confirms it still matches
    /// the hash recorded at creation. A mismatched or unreadable file returns
    /// <see cref="FailureCode.RevisionIntegrityMismatch"/> — the caller is responsible for
    /// invalidating the revision in the same metadata transaction as the failure it reports.
    /// </summary>
    public async Task<OperationResult<Sha256>> VerifyAsync(Revision revision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(revision);

        if (!revision.IsValid)
        {
            return OperationResult.Fail<Sha256>(
                FailureCode.RevisionIntegrityMismatch,
                $"Revision {revision.Id} is already invalid ({revision.InvalidationReason}); it cannot be consumed.");
        }

        string absolutePath = _workspace.ResolveAbsolute(revision.File);
        OperationResult<FileFacts> inspected = await _fileInspector.InspectAsync(absolutePath, cancellationToken);
        if (inspected.IsFailure)
        {
            return OperationResult.Fail<Sha256>(
                FailureCode.RevisionIntegrityMismatch,
                $"Revision {revision.Id} could not be re-read from '{absolutePath}': {inspected.Failure.TechnicalDetail}");
        }

        Sha256 actual = inspected.Value.Sha256;
        if (!actual.Equals(revision.Sha256))
        {
            return OperationResult.Fail<Sha256>(
                FailureCode.RevisionIntegrityMismatch,
                $"Revision {revision.Id} hash mismatch: recorded {revision.Sha256}, found {actual}. " +
                "The approved file was modified outside PrintFlow.");
        }

        return OperationResult.Ok(actual);
    }
}
