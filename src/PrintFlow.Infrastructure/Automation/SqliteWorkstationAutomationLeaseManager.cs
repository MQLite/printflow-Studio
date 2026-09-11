using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Automation;

/// <summary>
/// One application-owned SQLite authority for the workstation's Meitu/Photoshop automation
/// domain. It is deliberately independent of every session and regression-run database.
/// </summary>
public sealed class SqliteWorkstationAutomationLeaseManager : IWorkstationAutomationLeaseManager
{
    public const string DefaultResourceId = "printflow-studio.external-automation.v1";

    private readonly ControllerIdentity _controller;
    private readonly ILeaseControllerLiveness _liveness;

    public SqliteWorkstationAutomationLeaseManager(
        string? databaseAbsolutePath = null,
        string? resourceId = null)
        : this(
            databaseAbsolutePath,
            resourceId,
            ControllerIdentity.Current(),
            SystemLeaseControllerLiveness.Instance)
    {
    }

    internal SqliteWorkstationAutomationLeaseManager(
        string? databaseAbsolutePath,
        string? resourceId,
        ControllerIdentity controller,
        ILeaseControllerLiveness liveness)
    {
        DatabasePath = Path.GetFullPath(string.IsNullOrWhiteSpace(databaseAbsolutePath)
            ? DefaultDatabasePath
            : databaseAbsolutePath);
        ResourceId = string.IsNullOrWhiteSpace(resourceId) ? DefaultResourceId : resourceId;
        _controller = controller;
        _liveness = liveness ?? throw new ArgumentNullException(nameof(liveness));
    }

    public static string DefaultDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PrintFlow Studio",
        "workstation-automation-v1.db");

    public string DatabasePath { get; }

    public string ResourceId { get; }

    public async Task<OperationResult<IWorkstationAutomationLease>> TryAcquireAsync(
        IWorkstationAutomationLease? enclosingLease,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (enclosingLease is not null)
        {
            if (enclosingLease is LeaseHandle nestedFrom &&
                ReferenceEquals(nestedFrom.Manager, this) &&
                nestedFrom.TryAddReference())
            {
                return OperationResult.Ok<IWorkstationAutomationLease>(
                    new LeaseHandle(this, nestedFrom.State));
            }

            return OperationResult.Fail<IWorkstationAutomationLease>(
                FailureCode.AdapterUnavailable,
                "The supplied workstation automation scope is not an active owner of this resource.");
        }

        string token = Guid.NewGuid().ToString("N");
        DateTimeOffset acquiredAtUtc = DateTimeOffset.UtcNow;

        try
        {
            // The writable factory creates the parent directory, so keep it on the
            // acquisition path. Constructing/composing the manager must be inert.
            using SqliteConnection connection = OpenWritableConnection();
            using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
            await EnsureSchemaAsync(connection, transaction).ConfigureAwait(false);

            LeaseRow row = await connection.QuerySingleAsync<LeaseRow>(
                "SELECT * FROM WorkstationAutomationLease WHERE ResourceId = @ResourceId;",
                new { ResourceId }, transaction).ConfigureAwait(false);

            if (row.OwnerToken is not null)
            {
                ProcessLiveness owner = _liveness.Check(row.ToControllerIdentity());
                if (owner != ProcessLiveness.Dead)
                {
                    transaction.Rollback();
                    return OperationResult.Fail<IWorkstationAutomationLease>(
                        FailureCode.AdapterUnavailable,
                        owner == ProcessLiveness.Alive
                            ? "Meitu or Photoshop is already controlled by another PrintFlow operation."
                            : "The workstation automation owner could not be verified, so external automation was refused.");
                }
            }

            int changed = await connection.ExecuteAsync(
                """
                UPDATE WorkstationAutomationLease SET
                    OwnerToken = @OwnerToken,
                    ProcessId = @ProcessId,
                    MachineName = @MachineName,
                    ProcessName = @ProcessName,
                    ProcessStartedAtUtc = @ProcessStartedAtUtc,
                    AcquiredAtUtc = @AcquiredAtUtc
                WHERE ResourceId = @ResourceId
                  AND OwnerToken IS @PreviousOwnerToken;
                """,
                new
                {
                    ResourceId,
                    OwnerToken = token,
                    _controller.ProcessId,
                    _controller.MachineName,
                    _controller.ProcessName,
                    ProcessStartedAtUtc = _controller.ProcessStartedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                    AcquiredAtUtc = acquiredAtUtc.ToString("O", CultureInfo.InvariantCulture),
                    PreviousOwnerToken = row.OwnerToken,
                }, transaction).ConfigureAwait(false);

            if (changed != 1)
            {
                transaction.Rollback();
                return OperationResult.Fail<IWorkstationAutomationLease>(
                    FailureCode.AdapterUnavailable,
                    "The workstation automation owner changed before this operation could acquire it.");
            }

            transaction.Commit();
            LeaseState state = new(this, token);
            return OperationResult.Ok<IWorkstationAutomationLease>(new LeaseHandle(this, state));
        }
        catch (SqliteException ex)
        {
            return OperationResult.Fail<IWorkstationAutomationLease>(
                FailureCode.PersistenceError,
                $"The workstation automation lease could not be acquired: {ex.Message}");
        }
        catch (IOException ex)
        {
            return OperationResult.Fail<IWorkstationAutomationLease>(
                FailureCode.PersistenceError,
                $"The workstation automation lease store could not be opened: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail<IWorkstationAutomationLease>(
                FailureCode.PersistenceError,
                $"The workstation automation lease store is not accessible: {ex.Message}");
        }
        catch (FormatException ex)
        {
            return OperationResult.Fail<IWorkstationAutomationLease>(
                FailureCode.AdapterUnavailable,
                $"The recorded workstation automation owner could not be verified: {ex.Message}");
        }
    }

    public async Task<WorkstationAutomationLeaseObservation> ObserveAsync(
        IWorkstationAutomationLease? ownLease,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ownLease is LeaseHandle own &&
            ReferenceEquals(own.Manager, this) &&
            own.IsActive)
        {
            return new WorkstationAutomationLeaseObservation(
                WorkstationAutomationLeaseStatus.Owned,
                ResourceId,
                "This explicit operation scope owns the workstation automation lease.");
        }

        if (!File.Exists(DatabasePath))
        {
            return Unknown("The workstation automation authority has not been initialized.");
        }

        try
        {
            // Observation is a passive readiness operation. Read-only mode prevents
            // a file from being created if it disappears after the existence check.
            using SqliteConnection connection = OpenReadOnlyConnection();
            LeaseRow? row = await connection.QuerySingleOrDefaultAsync<LeaseRow>(
                "SELECT * FROM WorkstationAutomationLease WHERE ResourceId = @ResourceId;",
                new { ResourceId }).ConfigureAwait(false);

            if (row is null)
            {
                return Unknown("The workstation automation resource has no authority row.");
            }

            if (row.OwnerToken is null)
            {
                return new WorkstationAutomationLeaseObservation(
                    WorkstationAutomationLeaseStatus.Free,
                    ResourceId,
                    "The workstation automation authority has no owner.");
            }

            return _liveness.Check(row.ToControllerIdentity()) switch
            {
                ProcessLiveness.Alive => new WorkstationAutomationLeaseObservation(
                    WorkstationAutomationLeaseStatus.Busy,
                    ResourceId,
                    "Another live PrintFlow controller owns the workstation automation lease."),
                ProcessLiveness.Dead => Unknown(
                    "The recorded controller is no longer running; acquisition must recover the stale owner atomically."),
                _ => Unknown("The recorded workstation automation owner could not be verified."),
            };
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or FormatException)
        {
            return Unknown($"The workstation automation authority could not be read: {ex.Message}");
        }
    }

    private WorkstationAutomationLeaseObservation Unknown(string description) => new(
        WorkstationAutomationLeaseStatus.Unknown, ResourceId, description);

    private SqliteConnection OpenWritableConnection() =>
        new SqliteConnectionFactory(DatabasePath).Open();

    private SqliteConnection OpenReadOnlyConnection()
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private async Task EnsureSchemaAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        await connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS WorkstationAutomationLease (
                ResourceId TEXT NOT NULL PRIMARY KEY,
                OwnerToken TEXT NULL,
                ProcessId INTEGER NULL,
                MachineName TEXT NULL,
                ProcessName TEXT NULL,
                ProcessStartedAtUtc TEXT NULL,
                AcquiredAtUtc TEXT NULL,
                CHECK ((OwnerToken IS NULL AND ProcessId IS NULL AND MachineName IS NULL
                        AND ProcessName IS NULL AND ProcessStartedAtUtc IS NULL AND AcquiredAtUtc IS NULL)
                    OR (OwnerToken IS NOT NULL AND ProcessId IS NOT NULL AND MachineName IS NOT NULL
                        AND ProcessName IS NOT NULL AND ProcessStartedAtUtc IS NOT NULL AND AcquiredAtUtc IS NOT NULL))
            );
            """, transaction: transaction).ConfigureAwait(false);

        await connection.ExecuteAsync(
            """
            INSERT OR IGNORE INTO WorkstationAutomationLease
                (ResourceId, OwnerToken, ProcessId, MachineName, ProcessName, ProcessStartedAtUtc, AcquiredAtUtc)
            VALUES (@ResourceId, NULL, NULL, NULL, NULL, NULL, NULL);
            """,
            new { ResourceId }, transaction).ConfigureAwait(false);
    }

    private async Task<OperationResult<Unit>> ReleaseStoreAsync(string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using SqliteConnection connection = OpenWritableConnection();
            int changed = await connection.ExecuteAsync(
                """
                UPDATE WorkstationAutomationLease SET
                    OwnerToken = NULL,
                    ProcessId = NULL,
                    MachineName = NULL,
                    ProcessName = NULL,
                    ProcessStartedAtUtc = NULL,
                    AcquiredAtUtc = NULL
                WHERE ResourceId = @ResourceId AND OwnerToken = @OwnerToken;
                """,
                new { ResourceId, OwnerToken = token }).ConfigureAwait(false);

            return changed == 1
                ? OperationResult.Ok()
                : OperationResult.Fail<Unit>(
                    FailureCode.PersistenceError,
                    "The workstation automation lease is no longer owned by this token; no owner was released.");
        }
        catch (SqliteException ex)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.PersistenceError,
                $"The workstation automation lease could not be released: {ex.Message}");
        }
        catch (IOException ex)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.PersistenceError,
                $"The workstation automation lease store could not be opened for release: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.PersistenceError,
                $"The workstation automation lease store is not accessible for release: {ex.Message}");
        }
    }

    private sealed class LeaseState(SqliteWorkstationAutomationLeaseManager manager, string token)
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private int _references = 1;
        private bool _released;
        private bool _lastReleaseInProgress;

        internal SqliteWorkstationAutomationLeaseManager Manager { get; } = manager;
        internal string Token { get; } = token;

        internal bool IsActive => !_released && Volatile.Read(ref _references) > 0;

        internal bool TryAddReference()
        {
            lock (this)
            {
                if (_released || _lastReleaseInProgress || _references <= 0) return false;
                _references++;
                return true;
            }
        }

        internal async Task<OperationResult<Unit>> ReleaseReferenceAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_released) return OperationResult.Ok();

                lock (this)
                {
                    if (_references > 1)
                    {
                        _references--;
                        return OperationResult.Ok();
                    }

                    _lastReleaseInProgress = true;
                }

                OperationResult<Unit> released;
                try
                {
                    released = await Manager
                        .ReleaseStoreAsync(Token, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    lock (this) _lastReleaseInProgress = false;
                    throw;
                }
                if (released.IsSuccess)
                {
                    lock (this)
                    {
                        _references = 0;
                        _released = true;
                        _lastReleaseInProgress = false;
                    }
                }
                else
                {
                    lock (this) _lastReleaseInProgress = false;
                }

                return released;
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private sealed class LeaseHandle(
        SqliteWorkstationAutomationLeaseManager manager,
        LeaseState state) : IWorkstationAutomationLease
    {
        private int _released;
        private readonly SemaphoreSlim _releaseGate = new(1, 1);

        internal SqliteWorkstationAutomationLeaseManager Manager { get; } = manager;
        internal LeaseState State { get; } = state;

        public string ResourceId => Manager.ResourceId;
        public string OwnerToken => State.Token;
        public bool IsActive => Volatile.Read(ref _released) == 0 && State.IsActive;

        internal bool TryAddReference() => IsActive && State.TryAddReference();

        public async Task<OperationResult<Unit>> ReleaseAsync(CancellationToken cancellationToken)
        {
            await _releaseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref _released) != 0) return OperationResult.Ok();
                OperationResult<Unit> result = await State.ReleaseReferenceAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (result.IsSuccess) Interlocked.Exchange(ref _released, 1);
                return result;
            }
            finally
            {
                _releaseGate.Release();
            }
        }
    }

    internal sealed record ControllerIdentity(
        int ProcessId,
        string MachineName,
        string ProcessName,
        DateTimeOffset ProcessStartedAtUtc)
    {
        internal static ControllerIdentity Current()
        {
            using Process process = Process.GetCurrentProcess();
            return new ControllerIdentity(
                Environment.ProcessId,
                Environment.MachineName,
                process.ProcessName,
                process.StartTime.ToUniversalTime());
        }
    }

    internal interface ILeaseControllerLiveness
    {
        ProcessLiveness Check(ControllerIdentity owner);
    }

    private sealed class SystemLeaseControllerLiveness : ILeaseControllerLiveness
    {
        internal static readonly SystemLeaseControllerLiveness Instance = new();

        public ProcessLiveness Check(ControllerIdentity owner)
        {
            if (string.IsNullOrWhiteSpace(owner.MachineName) ||
                !string.Equals(owner.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
                owner.ProcessId <= 0 ||
                string.IsNullOrWhiteSpace(owner.ProcessName))
            {
                return ProcessLiveness.Unknown;
            }

            try
            {
                using Process process = Process.GetProcessById(owner.ProcessId);
                if (process.HasExited) return ProcessLiveness.Dead;
                if (!string.Equals(process.ProcessName, owner.ProcessName, StringComparison.OrdinalIgnoreCase))
                    return ProcessLiveness.Dead;

                DateTimeOffset started = process.StartTime.ToUniversalTime();
                return started == owner.ProcessStartedAtUtc
                    ? ProcessLiveness.Alive
                    : ProcessLiveness.Dead;
            }
            catch (ArgumentException)
            {
                return ProcessLiveness.Dead;
            }
            catch (InvalidOperationException)
            {
                return ProcessLiveness.Dead;
            }
            catch (Win32Exception)
            {
                return ProcessLiveness.Unknown;
            }
        }
    }

    private sealed class LeaseRow
    {
        public required string ResourceId { get; init; }
        public string? OwnerToken { get; init; }
        public int? ProcessId { get; init; }
        public string? MachineName { get; init; }
        public string? ProcessName { get; init; }
        public string? ProcessStartedAtUtc { get; init; }
        public string? AcquiredAtUtc { get; init; }

        internal ControllerIdentity ToControllerIdentity() => new(
            ProcessId ?? 0,
            MachineName ?? string.Empty,
            ProcessName ?? string.Empty,
            DateTimeOffset.Parse(ProcessStartedAtUtc ?? string.Empty, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind));
    }
}
