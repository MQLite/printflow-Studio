using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Settings;

namespace PrintFlow.Workflow.Ports;

/// <summary>One operator approval of one OpenAI Images upload. It lives only in memory.</summary>
public sealed class ImageUploadConfirmation(ImageProcessingMethod method, RevisionId revision, Sha256 hash)
{
    private int _state;

    public static bool IsOpenAiMethod(ImageProcessingMethod candidate) => candidate is
        ImageProcessingMethod.OpenAiRestore or ImageProcessingMethod.OpenAiCutout or ImageProcessingMethod.OpenAiCombined;

    // Reserve once at the workflow boundary. Even a failed attempt needs a fresh confirmation.
    internal bool TryReserve(ImageProcessingMethod candidateMethod, RevisionId inputRevision, Sha256 inputHash) =>
        IsOpenAiMethod(method) && Matches(candidateMethod, inputRevision, inputHash)
        && Interlocked.CompareExchange(ref _state, 1, 0) == 0;

    // Consume once at the transport boundary, after checking the exact bytes to be sent.
    public bool TryConsume(ImageProcessingMethod candidateMethod, RevisionId inputRevision, Sha256 inputHash) =>
        Matches(candidateMethod, inputRevision, inputHash) && Interlocked.CompareExchange(ref _state, 2, 1) == 1;

    private bool Matches(ImageProcessingMethod candidateMethod, RevisionId inputRevision, Sha256 inputHash) =>
        candidateMethod == method && inputRevision == revision && inputHash == hash;
}
