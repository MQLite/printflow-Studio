using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Ports;

/// <summary>
/// Supplies the signed workstation preset the application was configured against.
/// </summary>
/// <remarks>
/// The seam only. Epic 11100 Part 1 defines the interface and the immutable reference type;
/// the real loader — read-only, SHA-256-verified against the value in configuration — lands
/// with the rest of Task 11100.0.
///
/// Rules that hold for every implementation, now and later:
/// <list type="bullet">
///   <item>it reads; it never writes, normalises, migrates or repairs the manifest;</item>
///   <item>it verifies the manifest SHA-256 and fails closed on mismatch
///         (<see cref="FailureCode.PresetHashMismatch"/>);</item>
///   <item>the manifest lives outside the repository and its contents are never committed;</item>
///   <item>tests use a synthetic fixture, never the signed production manifest.</item>
/// </list>
/// Production-environment enforcement is not part of this seam: full verification is
/// Epic 11500.
/// </remarks>
public interface IWorkstationPresetProvider
{
    /// <summary>
    /// Returns the verified preset reference, or a failure explaining why it cannot be trusted.
    /// </summary>
    OperationResult<ProductionPresetRef> GetVerifiedPreset();
}
