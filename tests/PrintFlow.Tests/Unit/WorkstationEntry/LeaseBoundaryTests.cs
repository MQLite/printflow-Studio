using System.IO;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Workflow.Ports;
using Xunit;

namespace PrintFlow.Tests.Unit.WorkstationEntry;

/// <summary>
/// Scripted liveness is separate from the workstation entry's real-manager lease proof.
/// Requires an explicit existing task-owned run root and must only run after isolation review.
/// </summary>
[Collection("WorkstationEntryRuntimeLease")]
public sealed class LeaseBoundaryTests
{
    private readonly OwnedRuntimeFixture fixture;
    public LeaseBoundaryTests(OwnedRuntimeFixture fixture) => this.fixture = fixture;

    [Fact]
    [Trait("WorkstationEntryPhase", "Runtime")]
    public async Task Alive_owner_is_busy_and_cannot_be_reclaimed()
    {
        string database = fixture.CreateLeaseDatabase();
        string resource = "test.entry.alive." + Guid.NewGuid().ToString("N");
        SqliteWorkstationAutomationLeaseManager owner = Manager(database, resource, 101,
            ProcessLiveness.Alive);
        SqliteWorkstationAutomationLeaseManager competitor = Manager(database, resource, 102,
            ProcessLiveness.Alive);
        IWorkstationAutomationLease held = Acquired(await owner.TryAcquireAsync(null, CancellationToken.None));
        try
        {
            Assert.Equal(WorkstationAutomationLeaseStatus.Busy,
                (await competitor.ObserveAsync(null, CancellationToken.None)).Status);
            OperationResult<IWorkstationAutomationLease> refused =
                await competitor.TryAcquireAsync(null, CancellationToken.None);
            Assert.True(refused.IsFailure);
            Assert.Equal(FailureCode.AdapterUnavailable, refused.Failure.Code);
            Assert.True(held.IsActive);
        }
        finally { Assert.True((await held.ReleaseAsync(CancellationToken.None)).IsSuccess); }
    }

    [Fact]
    [Trait("WorkstationEntryPhase", "Runtime")]
    public async Task Unknown_owner_is_refused_without_eviction()
    {
        string database = fixture.CreateLeaseDatabase();
        string resource = "test.entry.unknown." + Guid.NewGuid().ToString("N");
        SqliteWorkstationAutomationLeaseManager owner = Manager(database, resource, 201,
            ProcessLiveness.Unknown);
        SqliteWorkstationAutomationLeaseManager competitor = Manager(database, resource, 202,
            ProcessLiveness.Unknown);
        IWorkstationAutomationLease held = Acquired(await owner.TryAcquireAsync(null, CancellationToken.None));
        string token = held.OwnerToken;
        try
        {
            Assert.Equal(WorkstationAutomationLeaseStatus.Unknown,
                (await competitor.ObserveAsync(null, CancellationToken.None)).Status);
            OperationResult<IWorkstationAutomationLease> refused =
                await competitor.TryAcquireAsync(null, CancellationToken.None);
            Assert.True(refused.IsFailure);
            Assert.Equal(FailureCode.AdapterUnavailable, refused.Failure.Code);
            Assert.Equal(token, held.OwnerToken);
            Assert.True(held.IsActive);
        }
        finally { Assert.True((await held.ReleaseAsync(CancellationToken.None)).IsSuccess); }
    }

    [Fact]
    [Trait("WorkstationEntryPhase", "Runtime")]
    public async Task Dead_owner_can_be_reclaimed_but_old_token_cannot_release_successor()
    {
        string database = fixture.CreateLeaseDatabase();
        string resource = "test.entry.dead." + Guid.NewGuid().ToString("N");
        SqliteWorkstationAutomationLeaseManager original = Manager(database, resource, 301,
            ProcessLiveness.Alive);
        SqliteWorkstationAutomationLeaseManager successorManager = Manager(database, resource, 302,
            ProcessLiveness.Dead);
        IWorkstationAutomationLease stale = Acquired(await original.TryAcquireAsync(null, CancellationToken.None));
        IWorkstationAutomationLease? successor = null;
        try
        {
            Assert.Equal(WorkstationAutomationLeaseStatus.Unknown,
                (await successorManager.ObserveAsync(null, CancellationToken.None)).Status);
            successor = Acquired(await successorManager.TryAcquireAsync(null, CancellationToken.None));
            Assert.NotEqual(stale.OwnerToken, successor.OwnerToken);
            Assert.True((await stale.ReleaseAsync(CancellationToken.None)).IsFailure);
            Assert.True(successor.IsActive);
            Assert.Equal(WorkstationAutomationLeaseStatus.Owned,
                (await successorManager.ObserveAsync(successor, CancellationToken.None)).Status);
        }
        finally
        {
            if (successor is not null)
                Assert.True((await successor.ReleaseAsync(CancellationToken.None)).IsSuccess);
        }
    }

    private static SqliteWorkstationAutomationLeaseManager Manager(string database, string resource,
        int identity, ProcessLiveness liveness) => new(database, resource,
        new SqliteWorkstationAutomationLeaseManager.ControllerIdentity(
            identity, "synthetic-entry-machine", "synthetic-entry-controller",
            DateTimeOffset.UnixEpoch.AddSeconds(identity)),
        new ConstantLiveness(liveness));

    private static IWorkstationAutomationLease Acquired(
        OperationResult<IWorkstationAutomationLease> result)
    {
        Assert.True(result.IsSuccess, result.IsFailure ? result.Failure.ToString() : "");
        return result.Value;
    }

    private sealed class ConstantLiveness(ProcessLiveness result)
        : SqliteWorkstationAutomationLeaseManager.ILeaseControllerLiveness
    {
        public ProcessLiveness Check(
            SqliteWorkstationAutomationLeaseManager.ControllerIdentity owner) => result;
    }
}
