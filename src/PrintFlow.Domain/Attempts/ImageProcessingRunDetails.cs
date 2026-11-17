using PrintFlow.Domain.Files;
using PrintFlow.Domain.Settings;

namespace PrintFlow.Domain.Attempts;

/// <summary>
/// Immutable method and input evidence captured when an image-processing attempt starts.
/// The enclosing attempt already records the adapter, input Revision and operation parameters.
/// </summary>
public sealed record ImageProcessingRunDetails(
    ImageProcessingMethod Method,
    Sha256 InputSha256,
    string? AlgorithmVersion = null);
