using Dapper;
using Microsoft.Data.Sqlite;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Infrastructure.Verification;

internal sealed record EnvironmentAutomationLease(string OwnerToken);

internal interface IEnvironmentAutomationLock
{
    Task<OperationResult<EnvironmentAutomationLease>> TryAcquireAsync(
        DateTimeOffset atUtc, CancellationToken cancellationToken);

    Task<OperationResult<Unit>> ReleaseAsync(
        EnvironmentAutomationLease lease, CancellationToken cancellationToken);

    Task<OperationResult<AutomationLockState>> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>Uses the same singleton database row that serialises production adapter work.</summary>
internal sealed class SqliteEnvironmentAutomationLock : IEnvironmentAutomationLock
{
    private readonly SqliteConnectionFactory _connections;
    private readonly int _processId;
    private readonly string _machineName;

    internal SqliteEnvironmentAutomationLock(
        SqliteConnectionFactory connections, int processId, string machineName)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _processId = processId;
        _machineName = machineName;
    }

    public async Task<OperationResult<EnvironmentAutomationLease>> TryAcquireAsync(
        DateTimeOffset atUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string token = Guid.NewGuid().ToString("N");
        try
        {
            using SqliteConnection connection = _connections.Open();
            int changed = await connection.ExecuteAsync(
                "UPDATE AutomationLock SET SessionId = NULL, AcquiredAtUtc = @atUtc, " +
                "ProcessId = @processId, MachineName = @machineName, " +
                "Purpose = 'ENVIRONMENT_VERIFICATION', OwnerToken = @token " +
                "WHERE Id = 1 AND SessionId IS NULL AND Purpose IS NULL AND OwnerToken IS NULL;",
                new
                {
                    atUtc = atUtc.ToUniversalTime().ToString("O"),
                    processId = _processId,
                    machineName = _machineName,
                    token,
                });
            return changed == 1
                ? OperationResult.Ok(new EnvironmentAutomationLease(token))
                : OperationResult.Fail<EnvironmentAutomationLease>(
                    FailureCode.AdapterUnavailable,
                    "Meitu or Photoshop is already controlled by another production operation. " +
                    "No live application check was started.");
        }
        catch (SqliteException ex)
        {
            return OperationResult.Fail<EnvironmentAutomationLease>(
                FailureCode.PersistenceError,
                $"The global automation lock could not be acquired: {ex.Message}");
        }
    }

    public async Task<OperationResult<Unit>> ReleaseAsync(
        EnvironmentAutomationLease lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using SqliteConnection connection = _connections.Open();
            int changed = await connection.ExecuteAsync(
                "UPDATE AutomationLock SET SessionId = NULL, AcquiredAtUtc = NULL, ProcessId = NULL, " +
                "MachineName = NULL, Purpose = NULL, OwnerToken = NULL " +
                "WHERE Id = 1 AND Purpose = 'ENVIRONMENT_VERIFICATION' AND OwnerToken = @token;",
                new { token = lease.OwnerToken });
            return changed == 1
                ? OperationResult.Ok()
                : OperationResult.Fail<Unit>(
                    FailureCode.PersistenceError,
                    "The live verification no longer owns the global automation lock; it was not released.");
        }
        catch (SqliteException ex)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.PersistenceError,
                $"The global automation lock could not be released: {ex.Message}");
        }
    }

    public async Task<OperationResult<AutomationLockState>> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using SqliteConnection connection = _connections.Open();
            dynamic row = await connection.QuerySingleAsync(
                "SELECT SessionId, AcquiredAtUtc, ProcessId, MachineName, Purpose, OwnerToken " +
                "FROM AutomationLock WHERE Id = 1;");
            string? purpose = row.Purpose as string;
            return OperationResult.Ok(new AutomationLockState(
                row.SessionId is string session ? Domain.Ids.SessionId.From(Guid.Parse(session)) : null,
                row.AcquiredAtUtc is string acquired
                    ? DateTimeOffset.Parse(acquired, null, System.Globalization.DateTimeStyles.RoundtripKind)
                    : null,
                row.ProcessId is long processId ? checked((int)processId) : null,
                row.MachineName as string,
                purpose switch
                {
                    "SESSION" => AutomationLockPurpose.Session,
                    "ENVIRONMENT_VERIFICATION" => AutomationLockPurpose.EnvironmentVerification,
                    _ => null,
                },
                row.OwnerToken as string));
        }
        catch (Exception ex) when (ex is SqliteException or FormatException or OverflowException)
        {
            return OperationResult.Fail<AutomationLockState>(
                FailureCode.PersistenceError,
                $"The global automation lock could not be read: {ex.Message}");
        }
    }
}
