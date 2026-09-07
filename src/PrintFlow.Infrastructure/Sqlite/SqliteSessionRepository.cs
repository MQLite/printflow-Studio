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
    public async Task<OperationResult<IReadOnlyList<SessionId>>> FindCompletedSessionsAsync(CancellationToken cancellationToken)
    {
        using SqliteConnection connection = _connectionFactory.Open();
        IEnumerable<string> ids = await connection.QueryAsync<string>(
            "SELECT Id FROM ProcessingSession WHERE State = 'COMPLETED' AND CompletedAtUtc IS NOT NULL ORDER BY Id;");
        return OperationResult.Ok<IReadOnlyList<SessionId>>(ids.Select(id => SessionId.From(Guid.Parse(id))).ToList());
    }

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
        IReadOnlyList<ProcessingAttempt> attempts = await PsdInspectionStorage.LoadAsync(
            connection, attemptRows.Select(Mappers.ToDomain).ToList());
        attempts = await PdfInspectionStorage.LoadAsync(connection, attempts);

        IEnumerable<ReviewRow> reviewRows = await connection.QueryAsync<ReviewRow>(
            "SELECT * FROM ReviewDecision WHERE SessionId = @sessionId ORDER BY DecidedAtUtc;", new { sessionId });

        IEnumerable<OutputRow> outputRows = await connection.QueryAsync<OutputRow>(
            "SELECT * FROM PrintOutput WHERE SessionId = @sessionId ORDER BY CreatedAtUtc;", new { sessionId });

        SessionAggregate aggregate = new(
            Mappers.ToDomain(sessionRow),
            snapshotRow is null ? null : Mappers.ToDomain(snapshotRow),
            stepRows.Select(Mappers.ToDomain).ToList(),
            revisionRows.Select(Mappers.ToDomain).ToList(),
            attempts,
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
            Mappers.ToStepKind(row.CurrentStep),
            Mappers.ToSessionState(row.State),
            Mappers.ToDateTimeOffset(row.UpdatedAtUtc))).ToList();

        return OperationResult.Ok(items);
    }

    /// <inheritdoc />
    public async Task<OperationResult<Unit>> CommitAsync(SessionMutation mutation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        if (mutation.IsRetentionMaintenance && (mutation.UpsertSteps.Count != 0 || mutation.RemoveSteps.Count != 0 ||
            mutation.NewRevisions.Count != 0 || mutation.RevisionInvalidations.Count != 0 ||
            mutation.UpsertAttempts.Count != 0 || mutation.NewReviews.Count != 0 || mutation.NewSnapshot is not null ||
            mutation.LockChange is not null))
            return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet,
                "Retention maintenance cannot change workflow, attempts, reviews, snapshots or automation ownership.");

        foreach (RevisionRetentionChange change in mutation.RevisionRetentionChanges)
        {
            string expectedPrefix = mutation.Session.Workspace.RelativePath + "/Working/";
            string expectedDestination = $"{mutation.Session.Workspace.RelativePath}/Revisions/{change.RevisionId}/{change.ExpectedFile.FileName}";
            if ((change.PromotedFile is null) == (change.ReleasedAtUtc is null) ||
                change.ExpectedFile.Area != Domain.Files.WorkspaceArea.Working ||
                !change.ExpectedFile.RelativePath.StartsWith(expectedPrefix, StringComparison.Ordinal) ||
                (change.PromotedFile is { } destination &&
                    (destination.Area != Domain.Files.WorkspaceArea.Revisions || destination.RelativePath != expectedDestination)))
                return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet, "Invalid Revision retention transition.");
        }

        using SqliteConnection connection = _connectionFactory.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();

        try
        {
            if (mutation.IsRetentionMaintenance)
            {
                int eligible = await connection.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*) FROM ProcessingSession s
                    WHERE s.Id = @id AND s.State = 'COMPLETED' AND s.CompletedAtUtc IS NOT NULL
                      AND NOT EXISTS (SELECT 1 FROM ProcessingAttempt a WHERE a.SessionId = s.Id AND a.ResultStatus = 'RUNNING')
                      AND NOT EXISTS (SELECT 1 FROM AutomationLock l WHERE l.SessionId = s.Id);
                    """, new { id = mutation.Session.Id.ToString() }, transaction);
                if (eligible != 1)
                    return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet,
                        "Retention refused: the session is no longer safely completed.");
            }
            else
            {
                if (mutation.RevisionRetentionChanges.Count != 0)
                    return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet,
                        "Revision retention requires a maintenance transaction.");
                await UpsertSessionAsync(connection, transaction, mutation.Session);
            }

            foreach (RevisionRetentionChange change in mutation.RevisionRetentionChanges)
            {
                int changed = await connection.ExecuteAsync(
                    """
                    UPDATE Revision SET
                      FormerWorkingPath = CASE WHEN @target IS NOT NULL THEN RelativePath ELSE FormerWorkingPath END,
                      RelativePath = COALESCE(@target, RelativePath),
                      RetentionReleasedAtUtc = COALESCE(@released, RetentionReleasedAtUtc),
                      IsValid = CASE WHEN @released IS NOT NULL THEN 0 ELSE IsValid END,
                      ReviewState = CASE WHEN @released IS NOT NULL THEN 'REJECTED' ELSE ReviewState END,
                      InvalidatedAtUtc = CASE WHEN @released IS NOT NULL THEN COALESCE(InvalidatedAtUtc, @released) ELSE InvalidatedAtUtc END,
                      InvalidationReason = CASE WHEN @released IS NOT NULL THEN COALESCE(InvalidationReason, 'REJECTED') ELSE InvalidationReason END
                    WHERE Id = @id AND SessionId = @sessionId AND RelativePath = @expected AND Sha256 = @hash
                      AND RetentionReleasedAtUtc IS NULL;
                    """, new
                    {
                        id = change.RevisionId.ToString(), sessionId = mutation.Session.Id.ToString(),
                        expected = change.ExpectedFile.RelativePath, hash = change.ExpectedHash.Value,
                        target = change.PromotedFile?.RelativePath,
                        released = change.ReleasedAtUtc is { } at ? Mappers.ToText(at) : null,
                    }, transaction);
                if (changed != 1)
                {
                    transaction.Rollback();
                    return OperationResult.Fail<Unit>(FailureCode.PersistenceError,
                        "Revision changed while retention was being planned; no authority was switched.");
                }
            }

            foreach (SessionStep step in mutation.UpsertSteps)
            {
                await UpsertStepAsync(connection, transaction, mutation.Session.Id, step);
            }

            // Steps the session no longer has, after a workflow re-shape. Inside the same
            // transaction as the upserts above, so the step list is never briefly a mixture
            // of the old workflow's rows and the new one's.
            foreach (StepKind step in mutation.RemoveSteps)
            {
                await DeleteStepAsync(connection, transaction, mutation.Session.Id, step);
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
                await PsdInspectionStorage.WriteAsync(connection, transaction, attempt);
                await PdfInspectionStorage.WriteAsync(connection, transaction, attempt);
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
                 DimensionsPreset, WhiteUnderbaseBranch,
                 TrimMode, TrimMarginTop, TrimMarginRight, TrimMarginBottom, TrimMarginLeft,
                 BackgroundRemovalDecision, BackgroundRemovalRevisionId, BackgroundRemovalReviewedSha,
                 DimensionSemantics,
                 PrintPlanSourceRevisionId, PrintPlanSourceSha256,
                 PrintPlanSourcePixelWidth, PrintPlanSourcePixelHeight,
                 PrintPlanMaxWidthMm, PrintPlanMaxHeightMm, PrintPlanLimitKind,
                 PrintPlanMode, PrintPlanLimitingEdge, PrintPlanLimitingValueMm,
                 PrintPlanProjectedPixelWidth, PrintPlanProjectedPixelHeight,
                 PrintPlanProductionDpi, PrintPlanResizePolicy,
                 SizingMode, SizingPreset, SizingRecommendationKind,
                 SizingRecommendationMaxWidthMm, SizingRecommendationMaxHeightMm,
                 SizingPresetOverridden, SizingTargetEdge, SizingRequestedMm,
                 TargetPlanSourceRevisionId, TargetPlanSourceSha256,
                 TargetPlanSourcePixelWidth, TargetPlanSourcePixelHeight,
                 TargetPlanPhotoshopEdge,
                 TargetPlanProjectedPixelWidth, TargetPlanProjectedPixelHeight,
                 TargetPlanScaleNumerator, TargetPlanScaleDenominator,
                 TargetPlanProductionDpi, TargetPlanDirection, TargetPlanResizePolicy,
                 EnlargementAuthoritySourceRevisionId, EnlargementAuthoritySourceSha256,
                 EnlargementAuthoritySizingMode, EnlargementAuthorityTargetEdge,
                 EnlargementAuthorityRequestedMm,
                 EnlargementAuthorityScaleNumerator, EnlargementAuthorityScaleDenominator,
                 EnlargementAuthorityProjectedPixelWidth, EnlargementAuthorityProjectedPixelHeight)
            VALUES
                (@Id, @WorkflowType, @OutputName, @CurrentStep, @State, @WorkspacePath, @CreatedAtUtc, @UpdatedAtUtc,
                 @CompletedAtUtc, @HandedOffAtUtc, @HandOffReason, @AbandonedAtUtc, @AbandonReason,
                 @DimensionsWidthMm, @DimensionsHeightMm, @DimensionsPixelWidth, @DimensionsPixelHeight,
                 @DimensionsPreset, @WhiteUnderbaseBranch,
                 @TrimMode, @TrimMarginTop, @TrimMarginRight, @TrimMarginBottom, @TrimMarginLeft,
                 @BackgroundRemovalDecision, @BackgroundRemovalRevisionId, @BackgroundRemovalReviewedSha,
                 @DimensionSemantics,
                 @PrintPlanSourceRevisionId, @PrintPlanSourceSha256,
                 @PrintPlanSourcePixelWidth, @PrintPlanSourcePixelHeight,
                 @PrintPlanMaxWidthMm, @PrintPlanMaxHeightMm, @PrintPlanLimitKind,
                 @PrintPlanMode, @PrintPlanLimitingEdge, @PrintPlanLimitingValueMm,
                 @PrintPlanProjectedPixelWidth, @PrintPlanProjectedPixelHeight,
                 @PrintPlanProductionDpi, @PrintPlanResizePolicy,
                 @SizingMode, @SizingPreset, @SizingRecommendationKind,
                 @SizingRecommendationMaxWidthMm, @SizingRecommendationMaxHeightMm,
                 @SizingPresetOverridden, @SizingTargetEdge, @SizingRequestedMm,
                 @TargetPlanSourceRevisionId, @TargetPlanSourceSha256,
                 @TargetPlanSourcePixelWidth, @TargetPlanSourcePixelHeight,
                 @TargetPlanPhotoshopEdge,
                 @TargetPlanProjectedPixelWidth, @TargetPlanProjectedPixelHeight,
                 @TargetPlanScaleNumerator, @TargetPlanScaleDenominator,
                 @TargetPlanProductionDpi, @TargetPlanDirection, @TargetPlanResizePolicy,
                 @EnlargementAuthoritySourceRevisionId, @EnlargementAuthoritySourceSha256,
                 @EnlargementAuthoritySizingMode, @EnlargementAuthorityTargetEdge,
                 @EnlargementAuthorityRequestedMm,
                 @EnlargementAuthorityScaleNumerator, @EnlargementAuthorityScaleDenominator,
                 @EnlargementAuthorityProjectedPixelWidth, @EnlargementAuthorityProjectedPixelHeight)
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
                WhiteUnderbaseBranch = excluded.WhiteUnderbaseBranch,

                -- The session's *pending* trim decision, so it does update: it is what the next
                -- run will use, and the operator is allowed to change their mind. The attempt's
                -- copy is the one that must never be rewritten (Epic 11200 Part C3 §14, §15).
                TrimMode = excluded.TrimMode,
                TrimMarginTop = excluded.TrimMarginTop,
                TrimMarginRight = excluded.TrimMarginRight,
                TrimMarginBottom = excluded.TrimMarginBottom,
                TrimMarginLeft = excluded.TrimMarginLeft,

                -- The session pending background-removal authority, so it updates for the same
                -- reason: it is what the next run would be allowed to do, and the operator may
                -- authorise different content. The attempt copy is the one that must never be
                -- rewritten (Epic 11300 Part C2B1 §10, §11).
                BackgroundRemovalDecision = excluded.BackgroundRemovalDecision,
                BackgroundRemovalRevisionId = excluded.BackgroundRemovalRevisionId,
                BackgroundRemovalReviewedSha = excluded.BackgroundRemovalReviewedSha,

                -- The reading of the millimetres above, updated with them: a session that
                -- reconfirms its limits under the current contract stops being a legacy row, and
                -- one rewound past PrintDimensions clears both together (Epic 11400 §4, §9).
                DimensionSemantics = excluded.DimensionSemantics,

                -- The session's *pending* preparation plan, so it updates for the same reason the
                -- trim margin and the background-removal authority do: it is what the next run
                -- would do, and the operator may record different limits. The attempt copy is the
                -- one that must never be rewritten (§12).
                PrintPlanSourceRevisionId = excluded.PrintPlanSourceRevisionId,
                PrintPlanSourceSha256 = excluded.PrintPlanSourceSha256,
                PrintPlanSourcePixelWidth = excluded.PrintPlanSourcePixelWidth,
                PrintPlanSourcePixelHeight = excluded.PrintPlanSourcePixelHeight,
                PrintPlanMaxWidthMm = excluded.PrintPlanMaxWidthMm,
                PrintPlanMaxHeightMm = excluded.PrintPlanMaxHeightMm,
                PrintPlanLimitKind = excluded.PrintPlanLimitKind,
                PrintPlanMode = excluded.PrintPlanMode,
                PrintPlanLimitingEdge = excluded.PrintPlanLimitingEdge,
                PrintPlanLimitingValueMm = excluded.PrintPlanLimitingValueMm,
                PrintPlanProjectedPixelWidth = excluded.PrintPlanProjectedPixelWidth,
                PrintPlanProjectedPixelHeight = excluded.PrintPlanProjectedPixelHeight,
                PrintPlanProductionDpi = excluded.PrintPlanProductionDpi,
                PrintPlanResizePolicy = excluded.PrintPlanResizePolicy,

                -- The session's *pending* flexible-size decision and its enlargement authority,
                -- so both update for exactly the same reason: they are what the next run would do
                -- and what it would be permitted to do, and the operator may record a different
                -- target or return upstream and clear it. The attempt copies are the ones that
                -- must never be rewritten (Epic 11400 Part B1A.2D §24, §31).
                --
                -- Clearing is a write of NULLs rather than a special case: ReturnToStep and
                -- AddAnotherSize produce a session with no selection, no plan and no authority,
                -- and this statement stores exactly that. What is never a write is *invalidation*
                -- — an authority stops applying because the exact target it names is no longer on
                -- offer, which is a comparison (§30).
                SizingMode = excluded.SizingMode,
                SizingPreset = excluded.SizingPreset,
                SizingRecommendationKind = excluded.SizingRecommendationKind,
                SizingRecommendationMaxWidthMm = excluded.SizingRecommendationMaxWidthMm,
                SizingRecommendationMaxHeightMm = excluded.SizingRecommendationMaxHeightMm,
                SizingPresetOverridden = excluded.SizingPresetOverridden,
                SizingTargetEdge = excluded.SizingTargetEdge,
                SizingRequestedMm = excluded.SizingRequestedMm,
                TargetPlanSourceRevisionId = excluded.TargetPlanSourceRevisionId,
                TargetPlanSourceSha256 = excluded.TargetPlanSourceSha256,
                TargetPlanSourcePixelWidth = excluded.TargetPlanSourcePixelWidth,
                TargetPlanSourcePixelHeight = excluded.TargetPlanSourcePixelHeight,
                TargetPlanPhotoshopEdge = excluded.TargetPlanPhotoshopEdge,
                TargetPlanProjectedPixelWidth = excluded.TargetPlanProjectedPixelWidth,
                TargetPlanProjectedPixelHeight = excluded.TargetPlanProjectedPixelHeight,
                TargetPlanScaleNumerator = excluded.TargetPlanScaleNumerator,
                TargetPlanScaleDenominator = excluded.TargetPlanScaleDenominator,
                TargetPlanProductionDpi = excluded.TargetPlanProductionDpi,
                TargetPlanDirection = excluded.TargetPlanDirection,
                TargetPlanResizePolicy = excluded.TargetPlanResizePolicy,
                EnlargementAuthoritySourceRevisionId = excluded.EnlargementAuthoritySourceRevisionId,
                EnlargementAuthoritySourceSha256 = excluded.EnlargementAuthoritySourceSha256,
                EnlargementAuthoritySizingMode = excluded.EnlargementAuthoritySizingMode,
                EnlargementAuthorityTargetEdge = excluded.EnlargementAuthorityTargetEdge,
                EnlargementAuthorityRequestedMm = excluded.EnlargementAuthorityRequestedMm,
                EnlargementAuthorityScaleNumerator = excluded.EnlargementAuthorityScaleNumerator,
                EnlargementAuthorityScaleDenominator = excluded.EnlargementAuthorityScaleDenominator,
                EnlargementAuthorityProjectedPixelWidth = excluded.EnlargementAuthorityProjectedPixelWidth,
                EnlargementAuthorityProjectedPixelHeight = excluded.EnlargementAuthorityProjectedPixelHeight;
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

    /// <summary>
    /// Removes one step row from a session that has been re-shaped onto another workflow.
    /// </summary>
    /// <remarks>
    /// The only delete in this repository. Metadata is otherwise append-or-update, and this is
    /// not an exception to that in spirit: a step that is not part of the chosen workflow is
    /// not history, it is a row that should never have outlived the choice. Nothing derived
    /// from it is touched — Revisions, attempts and reviews all remain, so the audit trail of
    /// what was actually done survives the change of workflow.
    /// </remarks>
    private static Task DeleteStepAsync(
        SqliteConnection connection, SqliteTransaction transaction, SessionId sessionId, StepKind step)
    {
        const string sql = "DELETE FROM SessionStep WHERE SessionId = @SessionId AND StepKind = @StepKind;";
        return connection.ExecuteAsync(
            sql,
            new { SessionId = sessionId.ToString(), StepKind = Mappers.ToText(step) },
            transaction);
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

    /// <summary>
    /// Inserts an attempt, or updates the fields that legitimately change when it ends.
    /// </summary>
    /// <remarks>
    /// The trim-parameter, background-removal, print-plan, flexible-size and enlargement-authority
    /// columns are deliberately absent from the <c>DO UPDATE</c> clause. They are written once,
    /// with the attempt's opening transaction, and describe what this attempt was asked to do,
    /// what authorised it and which geometry produced it — so leaving them out is what makes "a
    /// retry with a different margin never rewrites the first attempt's settings, a later decision
    /// never rewrites what an earlier cutout was authorised by, a later change of size never
    /// relabels an earlier output, and a later enlargement confirmation never makes an earlier run
    /// read as authorised" a property of the SQL rather than a promise about the caller
    /// (Epic 11200 Part C3 §15; Epic 11300 Part C2B1 §11, §18; Epic 11400 Part B1A.2A §12;
    /// Part B1A.2D §24).
    /// </remarks>
    private static Task UpsertAttemptAsync(SqliteConnection connection, SqliteTransaction transaction, ProcessingAttempt attempt)
    {
        AttemptRow row = Mappers.ToRow(attempt);
        const string sql =
            """
            INSERT INTO ProcessingAttempt
                (Id, SessionId, StepKind, InputRevisionId, Operation, AdapterId, StartedAtUtc, EndedAtUtc,
                 ResultStatus, OutputRevisionId, FailureCode, FailureDetailJson, RetryOfAttemptId, RetrySequence,
                 TrimMode, TrimMarginTop, TrimMarginRight, TrimMarginBottom, TrimMarginLeft,
                 BackgroundRemovalDecision, BackgroundRemovalRevisionId, BackgroundRemovalReviewedSha,
                 AdapterNotes,
                 TrimContentLeft, TrimContentTop, TrimContentRight, TrimContentBottom,
                 TrimAppliedLeft, TrimAppliedTop, TrimAppliedRight, TrimAppliedBottom,
                 ManualSelectedLeft, ManualSelectedTop, ManualSelectedRight, ManualSelectedBottom, ManualAppliedLeft, ManualAppliedTop, ManualAppliedRight, ManualAppliedBottom, ManualMarginTop, ManualMarginRight, ManualMarginBottom, ManualMarginLeft, ManualMarginMode,
                 PrintPlanSourceRevisionId, PrintPlanSourceSha256,
                 PrintPlanSourcePixelWidth, PrintPlanSourcePixelHeight,
                 PrintPlanMaxWidthMm, PrintPlanMaxHeightMm, PrintPlanLimitKind,
                 PrintPlanMode, PrintPlanLimitingEdge, PrintPlanLimitingValueMm,
                 PrintPlanProjectedPixelWidth, PrintPlanProjectedPixelHeight,
                 PrintPlanProductionDpi, PrintPlanResizePolicy,
                 SizingMode, SizingPreset, SizingRecommendationKind,
                 SizingRecommendationMaxWidthMm, SizingRecommendationMaxHeightMm,
                 SizingPresetOverridden, SizingTargetEdge, SizingRequestedMm,
                 TargetPlanSourceRevisionId, TargetPlanSourceSha256,
                 TargetPlanSourcePixelWidth, TargetPlanSourcePixelHeight,
                 TargetPlanPhotoshopEdge,
                 TargetPlanProjectedPixelWidth, TargetPlanProjectedPixelHeight,
                 TargetPlanScaleNumerator, TargetPlanScaleDenominator,
                 TargetPlanProductionDpi, TargetPlanDirection, TargetPlanResizePolicy,
                 EnlargementAuthoritySourceRevisionId, EnlargementAuthoritySourceSha256,
                 EnlargementAuthoritySizingMode, EnlargementAuthorityTargetEdge,
                 EnlargementAuthorityRequestedMm,
                 EnlargementAuthorityScaleNumerator, EnlargementAuthorityScaleDenominator,
                 EnlargementAuthorityProjectedPixelWidth, EnlargementAuthorityProjectedPixelHeight)
            VALUES
                (@Id, @SessionId, @StepKind, @InputRevisionId, @Operation, @AdapterId, @StartedAtUtc, @EndedAtUtc,
                 @ResultStatus, @OutputRevisionId, @FailureCode, @FailureDetailJson, @RetryOfAttemptId, @RetrySequence,
                 @TrimMode, @TrimMarginTop, @TrimMarginRight, @TrimMarginBottom, @TrimMarginLeft,
                 @BackgroundRemovalDecision, @BackgroundRemovalRevisionId, @BackgroundRemovalReviewedSha,
                 @AdapterNotes,
                 @TrimContentLeft, @TrimContentTop, @TrimContentRight, @TrimContentBottom,
                 @TrimAppliedLeft, @TrimAppliedTop, @TrimAppliedRight, @TrimAppliedBottom,
                 @ManualSelectedLeft, @ManualSelectedTop, @ManualSelectedRight, @ManualSelectedBottom, @ManualAppliedLeft, @ManualAppliedTop, @ManualAppliedRight, @ManualAppliedBottom, @ManualMarginTop, @ManualMarginRight, @ManualMarginBottom, @ManualMarginLeft, @ManualMarginMode,
                 @PrintPlanSourceRevisionId, @PrintPlanSourceSha256,
                 @PrintPlanSourcePixelWidth, @PrintPlanSourcePixelHeight,
                 @PrintPlanMaxWidthMm, @PrintPlanMaxHeightMm, @PrintPlanLimitKind,
                 @PrintPlanMode, @PrintPlanLimitingEdge, @PrintPlanLimitingValueMm,
                 @PrintPlanProjectedPixelWidth, @PrintPlanProjectedPixelHeight,
                 @PrintPlanProductionDpi, @PrintPlanResizePolicy,
                 @SizingMode, @SizingPreset, @SizingRecommendationKind,
                 @SizingRecommendationMaxWidthMm, @SizingRecommendationMaxHeightMm,
                 @SizingPresetOverridden, @SizingTargetEdge, @SizingRequestedMm,
                 @TargetPlanSourceRevisionId, @TargetPlanSourceSha256,
                 @TargetPlanSourcePixelWidth, @TargetPlanSourcePixelHeight,
                 @TargetPlanPhotoshopEdge,
                 @TargetPlanProjectedPixelWidth, @TargetPlanProjectedPixelHeight,
                 @TargetPlanScaleNumerator, @TargetPlanScaleDenominator,
                 @TargetPlanProductionDpi, @TargetPlanDirection, @TargetPlanResizePolicy,
                 @EnlargementAuthoritySourceRevisionId, @EnlargementAuthoritySourceSha256,
                 @EnlargementAuthoritySizingMode, @EnlargementAuthorityTargetEdge,
                 @EnlargementAuthorityRequestedMm,
                 @EnlargementAuthorityScaleNumerator, @EnlargementAuthorityScaleDenominator,
                 @EnlargementAuthorityProjectedPixelWidth, @EnlargementAuthorityProjectedPixelHeight)
            ON CONFLICT(Id) DO UPDATE SET
                EndedAtUtc = excluded.EndedAtUtc,
                ResultStatus = excluded.ResultStatus,
                OutputRevisionId = excluded.OutputRevisionId,
                FailureCode = excluded.FailureCode,
                FailureDetailJson = excluded.FailureDetailJson,
                AdapterNotes = excluded.AdapterNotes,

                -- The one audit group written by the CLOSING transaction rather than the opening
                -- one, and therefore the one that has to appear here (SCRUM-11081). The margin,
                -- the background-removal authority and the preparation plan are all decisions the
                -- attempt was started with, so leaving them out of this clause is what makes them
                -- unrewritable. Trim geometry is a result: it does not exist until the pixel work
                -- has run, and the row that opened the attempt necessarily carried nulls.
                --
                -- That is not a hole in the same guarantee. Migration 0009's
                -- ProcessingAttempt_TrimBounds_Immutable trigger aborts any update that changes a
                -- geometry already present, so this clause can fill nulls in exactly once and can
                -- never edit or erase what an earlier attempt recorded.
                TrimContentLeft = excluded.TrimContentLeft,
                TrimContentTop = excluded.TrimContentTop,
                TrimContentRight = excluded.TrimContentRight,
                TrimContentBottom = excluded.TrimContentBottom,
                TrimAppliedLeft = excluded.TrimAppliedLeft,
                TrimAppliedTop = excluded.TrimAppliedTop,
                TrimAppliedRight = excluded.TrimAppliedRight,
                TrimAppliedBottom = excluded.TrimAppliedBottom,
                ManualSelectedLeft = excluded.ManualSelectedLeft,
                ManualSelectedTop = excluded.ManualSelectedTop,
                ManualSelectedRight = excluded.ManualSelectedRight,
                ManualSelectedBottom = excluded.ManualSelectedBottom,
                ManualAppliedLeft = excluded.ManualAppliedLeft,
                ManualAppliedTop = excluded.ManualAppliedTop,
                ManualAppliedRight = excluded.ManualAppliedRight,
                ManualAppliedBottom = excluded.ManualAppliedBottom,
                ManualMarginTop = excluded.ManualMarginTop,
                ManualMarginRight = excluded.ManualMarginRight,
                ManualMarginBottom = excluded.ManualMarginBottom,
                ManualMarginLeft = excluded.ManualMarginLeft,
                ManualMarginMode = excluded.ManualMarginMode;
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
                 ByteLength, Sha256, ReviewState, IsValid, InvalidationReason, RecycledAtUtc,
                 PromotionReservedPath, CreatedAtUtc)
            VALUES
                (@Id, @SessionId, @SourceRevisionId, @TargetWidthMm, @TargetHeightMm, @PixelWidth, @PixelHeight, @Dpi,
                 @SizePresetId, @WhiteUnderbaseBranch, @ProductionPresetId, @ProductionPresetSha256, @RelativePath,
                 @ByteLength, @Sha256, @ReviewState, @IsValid, @InvalidationReason, @RecycledAtUtc,
                 @PromotionReservedPath, @CreatedAtUtc)
            ON CONFLICT(Id) DO UPDATE SET
                ReviewState = excluded.ReviewState,
                IsValid = excluded.IsValid,
                InvalidationReason = excluded.InvalidationReason,
                RecycledAtUtc = excluded.RecycledAtUtc,

                -- Where the deliverable now lives. This is the one identity-ish column an update
                -- may move, and only final approval moves it: Working\ while the TIFF awaits
                -- review, Approved\ once it has been approved and the promoted bytes have been
                -- independently re-hashed (Epic 11400 Part C2B §4, §6). The hash, byte length,
                -- source Revision and creation instant stay put, and PrintOutput_Identity_Immutable
                -- aborts the write if any of them is moved.
                RelativePath = excluded.RelativePath,
                PromotionReservedPath = excluded.PromotionReservedPath;
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
