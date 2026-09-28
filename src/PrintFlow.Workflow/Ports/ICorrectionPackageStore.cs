using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Ports;

/// <summary>What placing one correction-package file found (SCRUM-11148, design §6.2 step 4).</summary>
public enum CorrectionFileOutcome
{
    /// <summary>The file did not exist and was created and verified now.</summary>
    Created,

    /// <summary>The file already existed with exactly the expected bytes (a resumed preparation).</summary>
    AlreadyPresent,
}

/// <summary>
/// The file half of a colleague-correction package: independent verified copies inside the managed
/// session workspace, never links, never an overwrite (SCRUM-11148, design §5.3, §6.2).
/// </summary>
/// <remarks>
/// Every folder is a <see cref="WorkspaceDirRef"/> the service built under the session's own
/// workspace, and every name is a plain file name; the store resolves both through the workspace
/// root and refuses anything that would leave it. Nothing here deletes, overwrites, moves or edits
/// a previously present file, including a matching-suffix temporary. A colleague's edited working copy or
/// saved return is never touched.
/// </remarks>
public interface ICorrectionPackageStore
{
    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="folder"/>/<paramref name="fileName"/> via a
    /// fresh create-new temporary named with <paramref name="partialSuffix"/>, flushed, re-hashed
    /// against <paramref name="expected"/>, then moved into place without replacement.
    /// </summary>
    /// <remarks>
    /// An existing ordinary, standalone final file with the expected hash is accepted
    /// (<see cref="CorrectionFileOutcome.AlreadyPresent"/>); links and files with other bytes are
    /// left untouched and refused. A written copy that does not hash to
    /// <paramref name="expected"/> is refused with the context key <c>copyMismatch</c>, so the caller
    /// can re-verify the source itself. <paramref name="markReadOnly"/> sets the ReadOnly attribute
    /// as a courtesy hint; it is never authority.
    /// </remarks>
    Task<OperationResult<CorrectionFileOutcome>> PlaceVerifiedCopyAsync(
        WorkspaceDirRef folder, string fileName, WorkspaceFileRef source, Sha256 expected,
        string partialSuffix, bool markReadOnly, CancellationToken cancellationToken);

    /// <summary>Writes a small text file once with create-new; an existing file is kept as it is.</summary>
    Task<OperationResult<CorrectionFileOutcome>> WriteTextOnceAsync(
        WorkspaceDirRef folder, string fileName, string content, CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="folder"/>/<paramref name="fileName"/> currently exists. Read-only.</summary>
    bool Exists(WorkspaceDirRef folder, string fileName);

    /// <summary>The absolute folder, for display and for the operator's Open folder action.</summary>
    string ResolveFolder(WorkspaceDirRef folder);
}
