using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Preset;

/// <summary>
/// Returns the preset identity the application was configured with, without reading the
/// signed manifest.
/// </summary>
/// <remarks>
/// This is the smallest implementation that lets the composition root wire a real object
/// graph in Epic 11100 Part 1. It performs no file access at all, so it cannot touch — let
/// alone modify — anything under the Epic 11000 baseline.
///
/// It deliberately does <b>not</b> claim the preset is verified. Because the manifest is
/// never opened, no SHA-256 comparison has happened, so this provider fails closed: it
/// returns <see cref="FailureCode.EnvironmentNotVerified"/> unless it is constructed with
/// an already-verified reference (which only tests, using a synthetic fixture, currently
/// do). The real read-only, hash-verifying loader lands with Task 11100.0; full
/// environment verification is Epic 11500.
/// </remarks>
public sealed class ConfiguredPresetProvider : IWorkstationPresetProvider
{
    private readonly ProductionPresetRef? _verified;

    /// <summary>Creates a provider that has verified nothing and therefore trusts nothing.</summary>
    public ConfiguredPresetProvider()
        : this(null)
    {
    }

    /// <summary>Creates a provider that returns an already-verified reference.</summary>
    /// <param name="verified">
    /// A preset reference whose manifest hash was checked elsewhere. Tests pass a synthetic
    /// fixture; the signed production manifest is never copied into the repository.
    /// </param>
    public ConfiguredPresetProvider(ProductionPresetRef? verified) => _verified = verified;

    /// <inheritdoc />
    public OperationResult<ProductionPresetRef> GetVerifiedPreset() =>
        _verified is null
            ? OperationResult.Fail<ProductionPresetRef>(
                FailureCode.EnvironmentNotVerified,
                "No workstation preset has been loaded or hash-verified. Preset loading is Task 11100.0; environment verification is Epic 11500.")
            : OperationResult.Ok(_verified);

    /// <inheritdoc />
    public OperationResult<NamingPatternSet> GetNamingPatterns() =>
        _verified is null
            ? OperationResult.Fail<NamingPatternSet>(
                FailureCode.EnvironmentNotVerified,
                "No workstation preset has been loaded or hash-verified; naming patterns are unavailable.")
            : OperationResult.Ok(NamingPatternSet.DesignDefault);

    /// <summary>Builds a reference from configuration values, without touching the manifest.</summary>
    public static OperationResult<ProductionPresetRef> Describe(
        string presetId, string presetVersion, string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(presetId) || string.IsNullOrWhiteSpace(presetVersion))
        {
            return OperationResult.Fail<ProductionPresetRef>(
                FailureCode.PreconditionNotMet, "A preset reference needs both an id and a version.");
        }

        if (!Sha256.TryParse(expectedSha256, out Sha256 hash))
        {
            return OperationResult.Fail<ProductionPresetRef>(
                FailureCode.PresetHashMismatch,
                $"'{expectedSha256}' is not a valid SHA-256 digest.");
        }

        return OperationResult.Ok(new ProductionPresetRef(presetId, presetVersion, hash));
    }
}
