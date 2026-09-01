using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// A test-only <see cref="IEnvironmentGate"/> standing in for an unverified workstation
/// (Epic 11500 Part B §24).
/// </summary>
/// <remarks>
/// Epic 11100's <c>FoundationEnvironmentGate</c> had exactly this behaviour and shipped, because
/// at the time "Production is always denied" was the whole of the contract. Part B replaced it
/// with <c>VerifiedEnvironmentGate</c>, which decides from the workstation verifier — and the
/// suite still needs a gate that refuses Production on any machine, for the many tests whose
/// subject is what the workflow does when it is refused rather than why.
/// <para>
/// <b>It is test-only, and refuses.</b> §24 forbids a permissive gate shipping in product code;
/// this is the opposite of permissive and it is not product code. Nothing in <c>src</c>
/// references it, and an architecture test asserts <c>VerifiedEnvironmentGate</c> is the one
/// gate implementation that ships.
/// </para>
/// </remarks>
internal sealed class UnverifiedEnvironmentGate : IEnvironmentGate
{
    /// <summary>How many times authorisation was asked for, in each mode.</summary>
    public List<AdapterExecutionMode> Requests { get; } = [];

    public OperationResult<PrintFlow.Domain.Results.Unit> Verify(AdapterExecutionMode mode)
    {
        Requests.Add(mode);

        return mode == AdapterExecutionMode.Fake
            ? OperationResult.Ok()
            : OperationResult.Fail<PrintFlow.Domain.Results.Unit>(
                FailureCode.EnvironmentNotVerified,
                "This test gate stands in for a workstation that has not verified.");
    }
}
