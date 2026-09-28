using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;

namespace PrintFlow.Workflow.Ports;

/// <summary>SCRUM-11092 / SCRUM-11112: copies and validates operator-produced evidence.</summary>
public interface IManualResultImporter
{
    Task<OperationResult<ManualResult>> ImportAsync(
        WorkspaceDirRef session, AttemptId attempt, StepKind step, FileFacts upstream, string selectedPath,
        CancellationToken cancellationToken);

    /// <summary>
    /// The same import, refused before anything is copied when the selected file no longer hashes
    /// to <paramref name="expectedSelectedHash"/> (SCRUM-11148, design §6.5 step 3).
    /// </summary>
    /// <remarks>
    /// The default serves importers that cannot check first: it imports, then refuses a managed
    /// copy whose bytes are not the expected ones. The real importer checks the held source before
    /// copying.
    /// </remarks>
    async Task<OperationResult<ManualResult>> ImportAsync(
        WorkspaceDirRef session, AttemptId attempt, StepKind step, FileFacts upstream, string selectedPath,
        Sha256? expectedSelectedHash, CancellationToken cancellationToken)
    {
        OperationResult<ManualResult> imported =
            await ImportAsync(session, attempt, step, upstream, selectedPath, cancellationToken).ConfigureAwait(false);
        return imported.IsSuccess && expectedSelectedHash is { } expected && imported.Value.Facts.Sha256 != expected
            ? OperationResult.Fail<ManualResult>(OperationFailure.Create(
                FailureCode.OutputValidationFailed,
                "The selected picture changed after it was checked; the managed copy was not accepted.",
                isRetryable: true,
                messageKey: "Session_CorrectionChanged"))
            : imported;
    }

    /// <summary>
    /// Checks a selected file read-only with exactly the limits <see cref="ImportAsync(WorkspaceDirRef, AttemptId, StepKind, FileFacts, string, CancellationToken)"/>
    /// applies, and writes nothing (SCRUM-11148, design §6.5 step 1).
    /// </summary>
    /// <remarks>
    /// The default refuses: an importer that cannot check a picture without importing it must not
    /// be taken to have checked it.
    /// </remarks>
    Task<OperationResult<ManualResultPreflight>> PreflightAsync(
        StepKind step, FileFacts upstream, string selectedPath, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Fail<ManualResultPreflight>(OperationFailure.Create(
            FailureCode.PreconditionNotMet,
            "This importer cannot check a picture before importing it.",
            messageKey: "Failure_ManualResultImport")));

    /// <summary>The SHA-256 of the selected file, read-only, or a refusal.</summary>
    Task<OperationResult<Sha256>> HashSelectedAsync(string selectedPath, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Fail<Sha256>(OperationFailure.Create(
            FailureCode.PreconditionNotMet,
            "This importer cannot hash a picture before importing it.",
            messageKey: "Failure_ManualResultImport")));
}

public sealed record ManualResult(WorkspaceFileRef File, FileFacts Facts);

/// <summary>What a read-only check established about a selected file (SCRUM-11148).</summary>
public sealed record ManualResultPreflight(Sha256 SelectedSha256, long ByteLength, int PixelWidth, int PixelHeight);
