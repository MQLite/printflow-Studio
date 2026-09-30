using System.Text.Json;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.WorkstationEntry.Scenarios;

/// <summary>
/// Separate real-manager proof for the explicit test lease DB/resource. Fake sessions do not
/// acquire this lease, so their session automation lock is never counted as this evidence.
/// </summary>
public static class LeaseChecks
{
    public static async Task<LeaseEvidence> RunAsync(OwnedPaths paths, string resourceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (string.IsNullOrWhiteSpace(resourceId) ||
            !resourceId.StartsWith("test.", StringComparison.Ordinal) ||
            resourceId == SqliteWorkstationAutomationLeaseManager.DefaultResourceId)
            throw new ArgumentException("An explicit per-run test resource is required.", nameof(resourceId));

        string database = paths.At("state", "automation-lease.db");
        string evidencePath = paths.At("evidence", "lease-checks.json");
        paths.ProtectDatabase(database, createNew: false);
        SqliteWorkstationAutomationLeaseManager first = new(database, resourceId);
        SqliteWorkstationAutomationLeaseManager foreign = new(database, resourceId);
        if (first.DatabasePath != database || first.ResourceId != resourceId ||
            foreign.DatabasePath != database || foreign.ResourceId != resourceId)
            throw new InvalidOperationException("Real lease managers did not bind the explicit run resource.");

        LeaseEvidence evidence = new(database, resourceId);
        IWorkstationAutomationLease? owner = null;
        IWorkstationAutomationLease? nested = null;
        IWorkstationAutomationLease? successor = null;
        try
        {
            paths.Require(database, "state");
            WorkstationAutomationLeaseObservation initial = await first.ObserveAsync(null, cancellationToken);
            evidence.Facts.Add(new("before acquisition", initial.Status.ToString(), null));

            paths.Require(database, "state");
            owner = Must(await first.TryAcquireAsync(null, cancellationToken), "acquire first owner");
            string firstToken = owner.OwnerToken;
            if (!owner.IsActive || owner.ResourceId != resourceId || string.IsNullOrWhiteSpace(firstToken))
                throw new InvalidOperationException("First lease lacked an active run-bound owner token.");
            evidence.Facts.Add(new("first owner", "Owned", firstToken));

            paths.Require(database, "state");
            RequireStatus(await first.ObserveAsync(owner, cancellationToken), WorkstationAutomationLeaseStatus.Owned);
            paths.Require(database, "state");
            RequireStatus(await foreign.ObserveAsync(null, cancellationToken), WorkstationAutomationLeaseStatus.Busy);
            evidence.Facts.Add(new("same-process Alive observation", "Busy", firstToken));

            paths.Require(database, "state");
            OperationResult<IWorkstationAutomationLease> competing =
                await foreign.TryAcquireAsync(null, cancellationToken);
            if (competing.IsSuccess) throw new InvalidOperationException("Foreign manager acquired a live owner row.");
            evidence.Facts.Add(new("foreign acquire refused", competing.Failure.Code.ToString(), firstToken));

            paths.Require(database, "state");
            nested = Must(await first.TryAcquireAsync(owner, cancellationToken), "acquire nested reference");
            if (!nested.IsActive || nested.OwnerToken != firstToken)
                throw new InvalidOperationException("Nested lease did not share the exact owner token.");
            evidence.Facts.Add(new("nested reference", "Owned", nested.OwnerToken));

            paths.Require(database, "state");
            OperationResult<IWorkstationAutomationLease> foreignNested =
                await foreign.TryAcquireAsync(owner, cancellationToken);
            if (foreignNested.IsSuccess)
                throw new InvalidOperationException("Foreign manager accepted a nested lease it did not issue.");
            evidence.Facts.Add(new("foreign nested reference refused",
                foreignNested.Failure.Code.ToString(), firstToken));

            paths.Require(database, "state");
            Must(await owner.ReleaseAsync(cancellationToken), "release outer reference");
            if (!nested.IsActive)
                throw new InvalidOperationException("Outer release ended an active nested reference.");
            paths.Require(database, "state");
            RequireStatus(await foreign.ObserveAsync(null, cancellationToken), WorkstationAutomationLeaseStatus.Busy);
            evidence.Facts.Add(new("nested still holds resource", "Busy", firstToken));

            paths.Require(database, "state");
            Must(await nested.ReleaseAsync(cancellationToken), "release final reference");
            paths.Require(database, "state");
            RequireStatus(await foreign.ObserveAsync(null, cancellationToken), WorkstationAutomationLeaseStatus.Free);
            evidence.Facts.Add(new("last reference released", "Free", null));

            paths.Require(database, "state");
            successor = Must(await foreign.TryAcquireAsync(null, cancellationToken), "acquire successor");
            if (successor.OwnerToken == firstToken)
                throw new InvalidOperationException("A new lease reused a previous owner token.");
            evidence.Facts.Add(new("new owner token", "Owned", successor.OwnerToken));
            evidence.Status = "VERIFIED_REAL_ISOLATED_MANAGER";
        }
        catch (Exception ex)
        {
            evidence.Status = "FAILED";
            evidence.Failure = ex.GetType().Name + ": " + ex.Message;
            throw;
        }
        finally
        {
            foreach (IWorkstationAutomationLease? lease in new[] { successor, nested, owner })
            {
                if (lease?.IsActive == true)
                {
                    try
                    {
                        paths.Require(database, "state");
                        OperationResult<Unit> released = await lease.ReleaseAsync(CancellationToken.None);
                        if (released.IsFailure)
                        {
                            evidence.Status = "FAILED_RELEASE_UNCERTAIN";
                            evidence.Failure = released.Failure.ToString();
                        }
                    }
                    catch (Exception ex)
                    {
                        evidence.Status = "FAILED_RELEASE_UNCERTAIN";
                        evidence.Failure = ex.GetType().Name + ": " + ex.Message;
                    }
                }
            }
            evidence.FinishedUtc = DateTimeOffset.UtcNow;
            // Dead/Unknown-owner liveness needs a genuinely dead owned child or the manager's
            // friend-only scripted-liveness fixture. An uninitialised authority's Unknown is a
            // different fact, so neither branch is filled with an invented result here.
            evidence.UnexecutedBranches.AddRange(["Dead owner reclaim: NOT_RUN",
                "Unknown owner refusal: NOT_RUN"]);
            paths.Require(evidencePath, "evidence");
            using FileStream stream = new(evidencePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, evidence, new JsonSerializerOptions { WriteIndented = true });
        }
        return evidence;
    }

    private static T Must<T>(OperationResult<T> result, string action) => result.IsSuccess
        ? result.Value
        : throw new InvalidOperationException(action + ": " + result.Failure);

    private static void RequireStatus(WorkstationAutomationLeaseObservation observation,
        WorkstationAutomationLeaseStatus expected)
    {
        if (observation.Status != expected)
            throw new InvalidOperationException("Lease observation was " + observation.Status +
                ", expected " + expected + ".");
    }
}

public sealed class LeaseEvidence(string databasePath, string resourceId)
{
    public string EvidenceKind { get; } = "REAL_ISOLATED_WORKSTATION_LEASE_MANAGER_NO_PROCESSOR";
    public string DatabasePath { get; } = databasePath;
    public string ResourceId { get; } = resourceId;
    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedUtc { get; set; }
    public string Status { get; set; } = "NOT_RUN";
    public string? Failure { get; set; }
    public List<LeaseFact> Facts { get; } = [];
    public List<string> UnexecutedBranches { get; } = [];
}

public sealed record LeaseFact(string Action, string Result, string? OwnerToken);
