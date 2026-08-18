using Dapper;
using Microsoft.Data.Sqlite;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Infrastructure.Sqlite;

/// <summary>
/// The real, Dapper-backed <see cref="ISessionRepository"/> (Epic 11100 Task 11108).
/// </summary>
/// <remarks>
/// <see cref="CommitAsync"/> is the only write path and always runs inside one
/// <see cref="SqliteTransaction"/>: one operator or system command produces one
/// <see cref="SessionMutation"/>, and either the whole batch lands or none of it does
/// (plan §33). The workflow layer never sees <see cref="SqliteConnection"/> or SQL — every
/// method here takes and returns domain types.
/// </remarks>
public sealed class SqliteSessionRepository : ISessionRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteSessionRepository(SqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<OperationResult<SessionAggregate?>> LoadAsync(SessionId id, CancellationToken cancellationToken)
    {
        using SqliteConnection connection = _connectionFactory.Open();
        string sessionId = id.ToString();

        SessionRow? sessionRow = await connection.QuerySingleOrDefaultAsync<SessionRow>(
            "SELECT * FROM ProcessingSession WHERE Id = @sessionId;", new { sessionId });

        if (sessionRow is null)
        {
            return OperationResult.Ok<SessionAggregate?>(null);
        }

        SnapshotRow? snapshotRow = await connection.QuerySingleOrDefaultAsync<SnapshotRow>(
            "SELECT * FROM InputSnapshot WHERE SessionId = @sessionId;", new { sessionId });

        IEnumerable<StepRow> stepRows = await connection.QueryAsync<StepRow>(
            "SELECT * FROM SessionStep WHERE SessionId = @sessionId ORDER BY Ordinal;", new { sessionId });

        IEnumerable<RevisionRow> revisionRows = await connection.QueryAsync<RevisionRow>(
            "SELECT * FROM Revision WHERE SessionId = @sessionId ORDER BY CreatedAtUtc;", new { sessionId });

        IEnumerable<AttemptRow> attemptRows = await connection.QueryAsync<AttemptRow>(
            "SELECT * FROM ProcessingAttempt WHERE SessionId = @sessionId ORDER BY StartedAtUtc;", new { sessionId });

        IEnumerable<ReviewRow> reviewRows = await connection.QueryAsync<ReviewRow>(
            "SELECT * FROM ReviewDecision WHERE SessionId = @sessionId ORDER BY DecidedAtUtc;", new { sessionId });

        IEnumerable<OutputRow> outputRows = await connection.QueryAsync<OutputRow>(
            "SELECT * FROM PrintOutput WHERE SessionId = @sessionId ORDER BY CreatedAtUtc;", new { sessionId });

        SessionAggregate aggregate = new(
            Mappers.ToDomain(sessionRow),
            snapshotRow is null ? null : Mappers.ToDomain(snapshotRow),
            stepRows.Select(Mappers.ToDomain).ToList(),
            revisionRows.Select(Mappers.ToDomain).ToList(),
            attemptRows.Select(Mappers.ToDomain).ToList(),
            reviewRows.Select(Mappers.ToDomain).ToList(),
            outputRows.Select(Mappers.ToDomain).ToList());

        return OperationResult.Ok<SessionAggregate?>(aggregate);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<SessionListItem>>> ListRecentAsync(
        int maxCount, DateTimeOffset since, CancellationToken cancellationToken)
    {
        using SqliteConnection connection = _connectionFactory.Open();

        IEnumerable<SessionRow> rows = await connection.QueryAsync<SessionRow>(
            "SELECT * FROM ProcessingSession WHERE UpdatedAtUtc >= @since ORDER BY UpdatedAtUtc DESC LIMIT @maxCount;",
            new { since = Mappers.ToText(since), maxCount });

        IReadOnlyList<SessionListItem> items = rows.Select(row => new SessionListItem(
            SessionId.From(Guid.Parse(row.Id)),
            Mappers.ToWorkflowType(row.WorkflowType),
            Domain.Files.OutputName.Parse(row.OutputName),
            Mappers.ToSessionState(row.State),
            Mappers.ToDateTimeOffset(row.UpdatedAtUtc))).ToList();

        return OperationResult.Ok(items);
    }

    /// <inheritdoc />
    public async Task<OperationResult<Unit>> CommitAsync(SessionMutation mutation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        using SqliteConnection connection = _connectionFactory.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();

        try
        {
            await UpsertSessionAsync(connection, transaction, mutation.Session);

            foreach (SessionStep step in mutation.UpsertSteps)
            {
                await UpsertStepAsync(connection, transaction, mutation.Session.Id, step);
            }

            if (mutation.NewSnapshot is { } snapshot)
            {
                await InsertSnapshotAsync(connection, transaction, snapshot);
            }

            foreach (Domain.Revisions.Revision revision in mutation.NewRevisions)
            {
                await InsertRevisionAsync(connection, transaction, revision);
            }

            foreach (RevisionInvalidation invalidation in mutation.RevisionInvalidations)
            {
                await InvalidateRevisionAsync(connection, transaction, invalidation);
            }

            foreach (ProcessingAttempt attempt in mutation.UpsertAttempts)
            {
                await UpsertAttemptAsync(connection, transaction, attempt);
            }

            foreach (ReviewDecision review in mutation.NewReviews)
            {
                await InsertReviewAsync(connection, transaction, review);
            }

            foreach (Domain.Outputs.PrintOutput output in mutation.UpsertOutputs)
            {
                await UpsertOutputAsync(connection, transaction, output);
            }

            if (mutation.LockChange is { } lockChange)
            {
                await ApplyLockChangeAsync(connection, transaction, lockChange);
            }

            transaction.Commit();
            return OperationResult.Ok();
        }
        catch (SqliteException ex)
        {
            transaction.Rollback();
            return OperationResult.Fail<Unit>(
                FailureCode.PersistenceError, $"Session metadata commit failed and was rolled back: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<ProcessingAttempt>>> FindRunningAttemptsAsync(
        CancellationToken cancellationToken)
    {
        using SqliteConnection connection = _connectionFactory.Open();

        IEnumerable<AttemptRow> rows = await connection.QueryAsync<AttemptRow>(
            "SELECT * FROM ProcessingAttempt WHERE ResultStatus = 'RUNNING';");

        return OperationResult.Ok<IReadOnlyList<ProcessingAttempt>>(rows.Select(Mappers.ToDomain).ToList());
    }

    /// <inheritdoc />
    public async Task<OperationResult<AutomationLockState>> GetAutomationLockAsync(CancellationToken cancellationToken)
    {
        using SqliteConnection connection = _connectionFactory.Open();

        AutomationLockRow row = await connection.QuerySingleAsync<AutomationLockRow>(
            "SELECT SessionId, AcquiredAtUtc, ProcessId, MachineName FROM AutomationLock WHERE Id = 1;");

        AutomationLockState state = new(
            row.SessionId is string sid ? SessionId.From(Guid.Parse(sid)) : null,
            Mappers.ToDateTimeOffsetOrNull(row.AcquiredAtUtc),
            row.ProcessId,
            row.MachineName);

        return OperationResult.Ok(state);
    }

    // -------------------------------------------------------------------------------------
    // Per-row writes. Every statement runs against the shared transaction so CommitAsync's
    // rollback covers all of them.
    // -------------------------------------------------------------------------------------

    private static Task UpsertSessionAsync(SqliteConnection connection, SqliteTransaction transaction, ProcessingSession session)
    {
        SessionRow row = Mappers.ToRow(session);
        const string sql =
            """
            INSERT INTO ProcessingSession
                (Id, WorkflowType, OutputName, CurrentStep, State, WorkspacePath, CreatedAtUtc, UpdatedAtUtc,
                 CompletedAtUtc, HandedOffAtUtc, HandOffReason, AbandonedAtUtc, AbandonReason,
                 DimensionsWidthMm, DimensionsHeightMm, DimensionsPixelWidth, DimensionsPixelHeight,
                 DimensionsPreset, WhiteUnderbaseBranch)
            VALUES
                (@Id, @WorkflowType, @OutputName, @CurrentStep, @State, @WorkspacePath, @CreatedAtUtc, @UpdatedAtUtc,
                 @CompletedAtUtc, @HandedOffAtUtc, @HandOffReason, @AbandonedAtUtc, @AbandonReason,
                 @DimensionsWidthMm, @DimensionsHeightMm, @DimensionsPixelWidth, @DimensionsPixelHeight,
                 @DimensionsPreset, @WhiteUnderbaseBranch)
            ON CONFLICT(Id) DO UPDATE SET
                WorkflowType = excluded.WorkflowType,
                OutputName = excluded.OutputName,
                CurrentStep = excluded.CurrentStep,
                State = excluded.State,
                UpdatedAtUtc = excluded.UpdatedAtUtc,
                CompletedAtUtc = excluded.CompletedAtUtc,
                HandedOffAtUtc = excluded.HandedOffAtUtc,
                HandOffReason = excluded.HandOffReason,
                AbandonedAtUtc = excluded.AbandonedAtUtc,
                AbandonReason = excluded.AbandonReason,
                DimensionsWidthMm = excluded.DimensionsWidthMm,
                DimensionsHeightMm = excluded.DimensionsHeightMm,
                DimensionsPixelWidth = excluded.DimensionsPixelWidth,
                DimensionsPixelHeight = excluded.DimensionsPixelHeight,
                DimensionsPreset = excluded.DimensionsPreset,
                WhiteUnderbaseBranch = excluded.WhiteUnderbaseBranch;
            """;
        return connection.ExecuteAsync(sql, row, transaction);
    }

    private static Task UpsertStepAsync(
        SqliteConnection connection, SqliteTransaction transaction, SessionId sessionId, SessionStep step)
    {
        StepRow row = Mappers.ToRow(sessionId, step);
        const string sql =
            """
            INSERT INTO SessionStep
                (SessionId, StepKind, Ordinal, State, CurrentRevisionId, CurrentRevisionSha,
                 SkipReason, AttemptCount, EnteredStateAtUtc)
            VALUES
                (@SessionId, @StepKind, @Ordinal, @State, @CurrentRevisionId, @CurrentRevisionSha,
                 @SkipReason, @AttemptCount, @EnteredStateAtUtc)
            ON CONFLICT(SessionId, StepKind) DO UPDATE SET
                Ordinal = excluded.Ordinal,
                State = excluded.State,
                CurrentRevisionId = excluded.CurrentRevisionId,
                CurrentRevisionSha = excluded.CurrentRevisionSha,
                SkipReason = excluded.SkipReason,
                AttemptCount = excluded.AttemptCount,
                EnteredStateAtUtc = excluded.EnteredStateAtUtc;
            """;
        return connection.ExecuteAsync(sql, row, transaction);
    }

    private static Task InsertSnapshotAsync(
        SqliteConnection connection, SqliteTransaction transaction, Domain.Sessions.InputSnapshot snapshot)
    {
        SnapshotRow row = Mappers.ToRow(snapshot);
        const string sql =
            """
            INSERT INTO InputSnapshot (Id, SessionId, RootRevisionId, OriginalSourcePath, OriginalFileName, ImportedAtUtc)
            VALUES (@Id, @SessionId, @RootRevisionId, @OriginalSourcePath, @OriginalFileName, @ImportedAtUtc);
            """;
        return connection.ExecuteAsync(sql, row, transaction);
    }

    private static Task InsertRevisionAsync(
        SqliteConnection connection, SqliteTransaction transaction, Domain.Revisions.Revision revision)
    {
        RevisionRow row = Mappers.ToRow(revision);
        const string sql =
            """
            INSERT INTO Revision
                (Id, SessionId, SourceRevisionId, Operation, RelativePath, Format, ByteLength, Sha256,
                 PixelWidth, PixelHeight, DpiX, DpiY, ColourMode, HasAlpha, CreatedAtUtc,
                 IsValid, InvalidatedAtUtc, InvalidationReason, ReviewState)
            VALUES
                (@Id, @SessionId, @SourceRevisionId, @Operation, @RelativePath, @Format, @ByteLength, @Sha256,
                 @PixelWidth, @PixelHeight, @DpiX, @DpiY, @ColourMode, @HasAlpha, @CreatedAtUtc,
                 @IsValid, @InvalidatedAtUtc, @InvalidationReason, @ReviewState);
            """;
        return connection.ExecuteAsync(sql, row, transaction);
    }

    private static Task InvalidateRevisionAsync(
        SqliteConnection connection, SqliteTransaction transaction, RevisionInvalidation invalidation)
    {
        const string sql =
            """
            UPDATE Revision
               SET IsValid = 0, InvalidatedAtUtc = @atUtc, InvalidationReason = @reason
             WHERE Id = @id;
            """;
        return connection.ExecuteAsync(sql, new
        {
            id = invalidation.RevisionId.ToString(),
            atUtc = Mappers.ToText(invalidation.AtUtc),
            reason = Mappers.ToText(invalidation.Reason),
        }, transaction);
    }

    private static Task UpsertAttemptAsync(SqliteConnection connection, SqliteTransaction transaction, ProcessingAttempt attempt)
    {
        AttemptRow row = Mappers.ToRow(attempt);
        const string sql =
            """
            INSERT INTO ProcessingAttempt
                (Id, SessionId, StepKind, InputRevisionId, Operation, AdapterId, StartedAtUtc, EndedAtUtc,
                 ResultStatus, OutputRevisionId, FailureCode, FailureDetailJson, RetryOfAttemptId, RetrySequence)
            VALUES
                (@Id, @SessionId, @StepKind, @InputRevisionId, @Operation, @AdapterId, @StartedAtUtc, @EndedAtUtc,
                 @ResultStatus, @OutputRevisionId, @FailureCode, @FailureDetailJson, @RetryOfAttemptId, @RetrySequence)
            ON CONFLICT(Id) DO UPDATE SET
                EndedAtUtc = excluded.EndedAtUtc,
                ResultStatus = excluded.ResultStatus,
                OutputRevisionId = excluded.OutputRevisionId,
                FailureCode = excluded.FailureCode,
                FailureDetailJson = excluded.FailureDetailJson;
            """;
        return connection.ExecuteAsync(sql, row, transaction);
    }

    private static Task InsertReviewAsync(SqliteConnection connection, SqliteTransaction transaction, ReviewDecision review)
    {
        ReviewRow row = Mappers.ToRow(review);
        const string sql =
            """
            INSERT INTO ReviewDecision
                (Id, SessionId, StepKind, SubjectKind, SubjectId, ReviewedSha256, Operator, DecidedAtUtc,
                 Decision, QuickReason, Notes)
            VALUES
                (@Id, @SessionId, @StepKind, @SubjectKind, @SubjectId, @ReviewedSha256, @Operator, @DecidedAtUtc,
                 @Decision, @QuickReason, @Notes);
            """;
        return connection.ExecuteAsync(sql, row, transaction);
    }

    private static Task UpsertOutputAsync(SqliteConnection connection, SqliteTransaction transaction, Domain.Outputs.PrintOutput output)
    {
        OutputRow row = Mappers.ToRow(output);
        const string sql =
            """
            INSERT INTO PrintOutput
                (Id, SessionId, SourceRevisionId, TargetWidthMm, TargetHeightMm, PixelWidth, PixelHeight, Dpi,
                 SizePresetId, WhiteUnderbaseBranch, ProductionPresetId, ProductionPresetSha256, RelativePath,
                 ByteLength, Sha256, ReviewState, IsValid, InvalidationReason, RecycledAtUtc, CreatedAtUtc)
            VALUES
                (@Id, @SessionId, @SourceRevisionId, @TargetWidthMm, @TargetHeightMm, @PixelWidth, @PixelHeight, @Dpi,
                 @SizePresetId, @WhiteUnderbaseBranch, @ProductionPresetId, @ProductionPresetSha256, @RelativePath,
                 @ByteLength, @Sha256, @ReviewState, @IsValid, @InvalidationReason, @RecycledAtUtc, @CreatedAtUtc)
            ON CONFLICT(Id) DO UPDATE SET
                ReviewState = excluded.ReviewState,
                IsValid = excluded.IsValid,
                InvalidationReason = excluded.InvalidationReason,
                RecycledAtUtc = excluded.RecycledAtUtc;
            """;
        return connection.ExecuteAsync(sql, row, transaction);
    }

    private static Task ApplyLockChangeAsync(
        SqliteConnection connection, SqliteTransaction transaction, AutomationLockChange change)
    {
        string sql = change.Action == AutomationLockAction.Acquire
            ? "UPDATE AutomationLock SET SessionId = @sessionId, AcquiredAtUtc = @atUtc, ProcessId = @processId, MachineName = @machineName WHERE Id = 1;"
            : "UPDATE AutomationLock SET SessionId = NULL, AcquiredAtUtc = NULL, ProcessId = NULL, MachineName = NULL WHERE Id = 1;";

        return connection.ExecuteAsync(sql, new
        {
            sessionId = change.SessionId.ToString(),
            atUtc = Mappers.ToText(change.AtUtc),
            processId = change.ProcessId,
            machineName = change.MachineName,
        }, transaction);
    }
}
