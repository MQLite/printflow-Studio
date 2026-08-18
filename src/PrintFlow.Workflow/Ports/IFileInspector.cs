using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Ports;

/// <summary>
/// Reads a file exactly once and returns its hash and structural metadata together
/// (Epic 11100 Task 11106a; plan §14.1).
/// </summary>
/// <remarks>
/// One call, one file read: hash and metadata come from the same pass, so they cannot end up
/// describing different bytes. Hashing doubles as the readability proof (plan §10.1) — a file
/// that cannot be streamed to the end cannot be hashed, so there is no separate, weaker
/// "can it be opened" check to fall out of step with the hash.
///
/// Nullable metadata fields are honest: for formats or files WIC cannot reliably decode, the
/// inspector returns <c>null</c> rather than guessing.
/// </remarks>
public interface IFileInspector
{
    /// <summary>
    /// Reads <paramref name="absolutePath"/> completely and returns its facts, or a structured
    /// failure if the file does not exist, is empty, or cannot be read to the end.
    /// </summary>
    Task<OperationResult<FileFacts>> InspectAsync(string absolutePath, CancellationToken cancellationToken);
}
