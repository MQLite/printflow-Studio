using Dapper;
using System.Globalization;
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
/// <see cref="CommitAsync"/> writes session mutations and always runs inside one
/// <see cref="SqliteTransaction"/>: one operator or system command produces one
/// <see cref="SessionMutation"/>, and either the whole batch lands or none of it does
/// (plan §33). The workflow layer never sees <see cref="SqliteConnection"/> or SQL — every
/// method here takes and returns domain types.
/// </remarks>
public sealed class SqliteSessionRepository : ISessionRepository
{
    public async Task<OperationResult<IReadOnlyList<SessionId>>> FindRecoveryCandidatesAsync(CancellationToken cancellationToken)
    {
        using SqliteConnection connection = _connectionFactory.Open();
        IEnumerable<string> ids = await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT s.Id FROM ProcessingSession s
            JOIN SessionStep t ON t.SessionId = s.Id AND t.StepKind = s.CurrentStep
            WHERE s.State IN ('ACTIVE', 'HANDED_OFF')
              AND t.State IN ('INTERRUPTED', 'FAILED', 'RETRY_REQUIRED', 'PROCESSING')
              AND EXISTS (SELECT 1 FROM ProcessingAttempt a WHERE a.SessionId = s.Id
                          AND a.StepKind = t.StepKind AND a.ResultStatus = 'INTERRUPTED')
            ORDER BY s.UpdatedAtUtc DESC, s.Id;
            """, cancellationToken: cancellationToken));
        return OperationResult.Ok<IReadOnlyList<SessionId>>(ids.Select(id => SessionId.From(Guid.Parse(id))).ToList());
    }

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

        IEnumerable<CorrectionRequestRow> correctionRows = await connection.QueryAsync<CorrectionRequestRow>(
            "SELECT * FROM CorrectionRequest WHERE SessionId = @sessionId ORDER BY CreatedAtUtc, Id;", new { sessionId });

        SessionAggregate aggregate = new(
            Mappers.ToDomain(sessionRow),
            snapshotRow is null ? null : Mappers.ToDomain(snapshotRow),
            stepRows.Select(Mappers.ToDomain).ToList(),
            revisionRows.Select(Mappers.ToDomain).ToList(),
            attempts,
            reviewRows.Select(Mappers.ToDomain).ToList(),
            outputRows.Select(Mappers.ToDomain).ToList())
        {
            CorrectionRequests = correctionRows.Select(CorrectionRequestRow.ToDomain).ToList(),
        };

        return OperationResult.Ok<SessionAggregate?>(aggregate);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<SessionListItem>>> ListRecentAsync(
        int maxCount, DateTimeOffset since, CancellationToken cancellationToken)
    {
        using SqliteConnection connection = _connectionFactory.Open();
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);

        // Removed records are filtered before the limit, not after: a job the operator took off
        // the list must not occupy one of the hundred places Home has to offer (Jira 11602).
        IEnumerable<SessionRow> rows = await connection.QueryAsync<SessionRow>(new CommandDefinition(
            """
            SELECT s.*, t.State AS CurrentStepState,
                   EXISTS (SELECT 1 FROM ProcessingAttempt a
                           WHERE a.SessionId = s.Id AND a.StepKind = s.CurrentStep
                             AND a.ResultStatus = 'RUNNING') AS HasRunningCurrentAttempt
            FROM ProcessingSession s
            LEFT JOIN SessionStep t ON t.SessionId = s.Id AND t.StepKind = s.CurrentStep
            WHERE s.UpdatedAtUtc >= @since AND s.RemovedFromRecentAtUtc IS NULL
            ORDER BY s.UpdatedAtUtc DESC LIMIT @maxCount;
            """,
            new { since = Mappers.ToText(since), maxCount }, transaction, cancellationToken: cancellationToken));

        List<SessionRow> selected = rows.ToList();
        Dictionary<string, int>? savedCounts = null;
        if (selected.Count != 0)
        {
            try
            {
                // A single metadata-only read for the bounded list. Artifact ID, latest current
                // approval subject, hash and length must agree. A TIFF sibling has a different
                // PrintOutputId even when its content hash happens to match.
                IEnumerable<RecentSaveCountRow> history = await connection.QueryAsync<RecentSaveCountRow>(
                    new CommandDefinition("""
                    WITH RECURSIVE roots(SessionId, RootId) AS (
                      SELECT DISTINCT d.SessionId, promoted.SourceRevisionId
                      FROM ArtifactDelivery d JOIN Revision promoted ON promoted.Id=d.RevisionId
                      WHERE d.SessionId IN @ids AND d.Kind='ApprovedAssetPng' AND d.Status='Delivered'
                      UNION
                      SELECT DISTINCT d.SessionId, output.SourceRevisionId
                      FROM ArtifactDelivery d JOIN PrintOutput output ON output.Id=d.PrintOutputId
                      WHERE d.SessionId IN @ids AND d.Kind='ApprovedPrintTiff' AND d.Status='Delivered'
                    ),
                    lineage(SessionId, RootId, Id, ParentId, NodeSessionId, IsValid, ReleasedAt, ByteLength, Path) AS (
                      SELECT roots.SessionId, roots.RootId, revision.Id, revision.SourceRevisionId,
                             revision.SessionId, revision.IsValid, revision.RetentionReleasedAtUtc,
                             revision.ByteLength, ',' || revision.Id || ','
                      FROM roots JOIN Revision revision ON revision.Id=roots.RootId
                      UNION ALL
                      SELECT lineage.SessionId, lineage.RootId, parent.Id, parent.SourceRevisionId,
                             parent.SessionId, parent.IsValid, parent.RetentionReleasedAtUtc,
                             parent.ByteLength, lineage.Path || parent.Id || ','
                      FROM lineage JOIN Revision parent ON parent.Id=lineage.ParentId
                      WHERE instr(lineage.Path, ',' || parent.Id || ',')=0
                    ),
                    valid_roots AS (
                      SELECT SessionId, RootId FROM lineage GROUP BY SessionId, RootId
                      HAVING MIN(CASE WHEN NodeSessionId=SessionId AND IsValid=1
                                           AND ReleasedAt IS NULL AND ByteLength>0 THEN 1 ELSE 0 END)=1
                         AND MAX(CASE WHEN ParentId IS NULL THEN 1 ELSE 0 END)=1
                    )
                    SELECT SessionId, SUM(SavedCount) AS SavedCount FROM (
                      SELECT d.SessionId, COUNT(DISTINCT d.RevisionId) AS SavedCount
                      FROM ArtifactDelivery d
                      JOIN ProcessingSession s ON s.Id=d.SessionId AND s.WorkflowType='PREPARE_ASSET'
                      JOIN SessionStep e ON e.SessionId=s.Id AND e.StepKind='ApprovedPngExport'
                      JOIN Revision r ON r.Id=d.RevisionId AND r.SessionId=s.Id
                      JOIN Revision source ON source.Id=r.SourceRevisionId AND source.SessionId=s.Id
                      JOIN ReviewDecision review ON review.Id=d.ReviewId AND review.SessionId=s.Id
                      JOIN ProcessingAttempt promotion ON promotion.SessionId=s.Id
                        AND promotion.OutputRevisionId=r.Id AND promotion.InputRevisionId=source.Id
                        AND promotion.ResultStatus='SUCCEEDED' AND promotion.Operation='PROMOTE_APPROVED'
                      WHERE d.SessionId IN @ids AND d.Kind='ApprovedAssetPng' AND d.Status='Delivered'
                        AND (s.State='COMPLETED' OR s.CurrentStep='ApprovedPngExport')
                        AND e.State='APPROVED' AND e.CurrentRevisionId=r.Id AND e.CurrentRevisionSha=r.Sha256
                        AND r.IsValid=1 AND r.RetentionReleasedAtUtc IS NULL AND r.Operation='PROMOTE_APPROVED'
                        AND source.IsValid=1 AND source.RetentionReleasedAtUtc IS NULL
                        AND source.ReviewState='APPROVED' AND source.Sha256=r.Sha256
                        AND source.ByteLength=r.ByteLength
                        AND EXISTS (SELECT 1 FROM valid_roots v WHERE v.SessionId=s.Id AND v.RootId=source.Id)
                        AND d.ApprovedSha256=r.Sha256 AND d.ApprovedLength=r.ByteLength
                        AND d.ApprovalSubjectKind='Revision' AND d.ApprovalSubjectId=source.Id
                        AND d.PromotionSourceRevisionId=source.Id
                        AND review.SubjectKind='REVISION' AND review.SubjectId=source.Id
                        AND review.Decision='APPROVED' AND review.ReviewedSha256=source.Sha256
                        AND review.Id=(SELECT latest.Id FROM ReviewDecision latest
                          WHERE latest.SessionId=s.Id AND latest.SubjectKind='REVISION'
                            AND latest.SubjectId=source.Id
                          ORDER BY latest.DecidedAtUtc DESC, latest.Id DESC LIMIT 1)
                      GROUP BY d.SessionId
                      UNION ALL
                      SELECT d.SessionId, COUNT(DISTINCT d.PrintOutputId) AS SavedCount
                      FROM ArtifactDelivery d
                      JOIN PrintOutput o ON o.Id=d.PrintOutputId AND o.SessionId=d.SessionId
                      JOIN Revision twin ON twin.Id=o.Id AND twin.SessionId=o.SessionId
                      JOIN ProcessingAttempt producing ON producing.SessionId=o.SessionId
                        AND producing.OutputRevisionId=twin.Id AND producing.InputRevisionId=o.SourceRevisionId
                        AND producing.ResultStatus='SUCCEEDED' AND producing.Operation='PHOTOSHOP_OUTPUT'
                      JOIN ReviewDecision review ON review.Id=d.ReviewId AND review.SessionId=d.SessionId
                      WHERE d.SessionId IN @ids AND d.Kind='ApprovedPrintTiff' AND d.Status='Delivered'
                        AND o.IsValid=1 AND o.RecycledAtUtc IS NULL AND o.ReviewState='APPROVED'
                        AND twin.IsValid=1 AND twin.RetentionReleasedAtUtc IS NULL
                        AND twin.Operation='PHOTOSHOP_OUTPUT' AND twin.SourceRevisionId=o.SourceRevisionId
                        AND twin.Sha256=o.Sha256 AND twin.ByteLength=o.ByteLength AND twin.Format='TIFF'
                        AND EXISTS (SELECT 1 FROM valid_roots v WHERE v.SessionId=o.SessionId
                              AND v.RootId=o.SourceRevisionId)
                        AND d.ApprovedSha256=o.Sha256 AND d.ApprovedLength=o.ByteLength
                        AND d.ApprovalSubjectKind='PrintOutput' AND d.ApprovalSubjectId=o.Id
                        AND review.SubjectKind='PRINT_OUTPUT' AND review.SubjectId=o.Id
                        AND review.Decision='APPROVED' AND review.ReviewedSha256=o.Sha256
                        AND review.Id=(SELECT latest.Id FROM ReviewDecision latest
                          WHERE latest.SessionId=o.SessionId AND latest.SubjectKind='PRINT_OUTPUT'
                            AND latest.SubjectId=o.Id
                          ORDER BY latest.DecidedAtUtc DESC, latest.Id DESC LIMIT 1)
                      GROUP BY d.SessionId
                    ) GROUP BY SessionId;
                    """, new { ids = selected.Select(row => row.Id).ToArray() }, transaction,
                        cancellationToken: cancellationToken));
                savedCounts = history.ToDictionary(row => row.SessionId, row => row.SavedCount);
            }
            catch (SqliteException)
            {
                // A failed history read is unknown, never evidence that there was no save.
            }
        }

        IReadOnlyList<SessionListItem> items = selected.Select(row => new SessionListItem(
            SessionId.From(Guid.Parse(row.Id)),
            Mappers.ToWorkflowType(row.WorkflowType),
            Domain.Files.OutputName.Parse(row.OutputName),
            Mappers.ToStepKind(row.CurrentStep),
            Mappers.ToSessionState(row.State),
            Mappers.ToDateTimeOffset(row.UpdatedAtUtc))
        {
            CurrentStepState = row.CurrentStepState is null ? null : Mappers.ToStepState(row.CurrentStepState),
            HasRunningCurrentAttempt = row.HasRunningCurrentAttempt,
            PreviouslySavedOutputCount = savedCounts is null ? null :
                savedCounts.GetValueOrDefault(row.Id),
        }).ToList();

        transaction.Commit();

        return OperationResult.Ok(items);
    }

    private sealed class RecentSaveCountRow
    {
        public string SessionId { get; set; } = "";
        public int SavedCount { get; set; }
    }

    /// <inheritdoc />
    public async Task<OperationResult<Unit>> CommitAsync(SessionMutation mutation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        if (mutation.IsRetentionMaintenance && (mutation.UpsertSteps.Count != 0 || mutation.RemoveSteps.Count != 0 ||
            mutation.NewRevisions.Count != 0 || mutation.RevisionInvalidations.Count != 0 ||
            mutation.RevisionReviewStateChanges.Count != 0 ||
            mutation.UpsertAttempts.Count != 0 || mutation.NewReviews.Count != 0 || mutation.NewSnapshot is not null ||
            mutation.LockChange is not null || mutation.CorrectionRequestChanges.Count != 0))
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

            foreach (RevisionReviewStateChange change in mutation.RevisionReviewStateChanges)
            {
                int changed = await UpdateRevisionReviewStateAsync(
                    connection, transaction, mutation.Session.Id, change);
                if (changed != 1)
                {
                    transaction.Rollback();
                    return OperationResult.Fail<Unit>(
                        FailureCode.PersistenceError,
                        "The reviewed Revision changed before its cached review state could be recorded.");
                }
            }

            foreach (Domain.Outputs.PrintOutput output in mutation.UpsertOutputs)
            {
                await UpsertOutputAsync(connection, transaction, output);
            }

            // Inside the same transaction as the attempt whose failure each entry describes, so
            // a stopped attempt and the durable record of why it stopped can never disagree
            // (Jira 11108; SessionMutation.NewAutomationLog).
            foreach (Domain.Automation.AutomationLogEntry entry in mutation.NewAutomationLog)
            {
                await InsertAutomationLogAsync(connection, transaction, entry);
            }

            // After the attempts and Revisions they may name, in the order the service built them
            // (a supersede before the insert that replaces it). Every change but an insert is
            // conditional: one that matches no row means the request is no longer the one this
            // transaction was built for, and nothing of the transaction may land (SCRUM-11148).
            foreach (CorrectionRequestChange change in mutation.CorrectionRequestChanges)
            {
                int changed = await ApplyCorrectionRequestChangeAsync(
                    connection, transaction, mutation.Session.Id, change);
                if (changed != 1)
                {
                    transaction.Rollback();
                    return OperationResult.Fail<Unit>(
                        FailureCode.PreconditionNotMet,
                        $"The correction request changed before this transaction could {change.GetType().Name}; nothing was recorded.");
                }
            }

            if (mutation.LockChange is { } lockChange)
            {
                int changed = await ApplyLockChangeAsync(connection, transaction, lockChange);
                if (changed != 1)
                {
                    transaction.Rollback();
                    return OperationResult.Fail<Unit>(
                        FailureCode.AdapterUnavailable,
                        "The per-database automation correlation row changed ownership before this session could update it.");
                }
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
    public async Task<OperationResult<IReadOnlyList<Domain.Automation.AutomationLogEntry>>> LoadAutomationLogAsync(
        SessionId sessionId, CancellationToken cancellationToken)
    {
        try
        {
            using SqliteConnection connection = _connectionFactory.Open();
            IEnumerable<AutomationLogRow> rows = await connection.QueryAsync<AutomationLogRow>(new CommandDefinition(
                "SELECT * FROM AutomationLogEntry WHERE SessionId = @sessionId ORDER BY AtUtc, Id;",
                new { sessionId = sessionId.ToString() }, cancellationToken: cancellationToken));

            return OperationResult.Ok<IReadOnlyList<Domain.Automation.AutomationLogEntry>>(
                rows.Select(Mappers.ToDomain).ToList());
        }
        catch (SqliteException ex)
        {
            return OperationResult.Fail<IReadOnlyList<Domain.Automation.AutomationLogEntry>>(
                FailureCode.PersistenceError, $"The automation log could not be read: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<AutomationLockState>> GetAutomationLockAsync(CancellationToken cancellationToken)
    {
        using SqliteConnection connection = _connectionFactory.Open();

        AutomationLockRow row = await connection.QuerySingleAsync<AutomationLockRow>(
            "SELECT SessionId, AcquiredAtUtc, ProcessId, MachineName, Purpose, OwnerToken " +
            "FROM AutomationLock WHERE Id = 1;");

        AutomationLockState state = new(
            row.SessionId is string sid ? SessionId.From(Guid.Parse(sid)) : null,
            // SCRUM-11110 verification leases already persist round-trip timestamps;
            // session leases use the repository's millisecond UTC representation.
            row.Purpose == "ENVIRONMENT_VERIFICATION" && row.AcquiredAtUtc is string verificationAt
                ? DateTimeOffset.ParseExact(verificationAt, "O", CultureInfo.InvariantCulture, DateTimeStyles.None)
                : Mappers.ToDateTimeOffsetOrNull(row.AcquiredAtUtc),
            row.ProcessId,
            row.MachineName,
            row.Purpose switch
            {
                "SESSION" => AutomationLockPurpose.Session,
                "ENVIRONMENT_VERIFICATION" => AutomationLockPurpose.EnvironmentVerification,
                _ => null,
            },
            row.OwnerToken);

        return OperationResult.Ok(state);
    }

    /// <inheritdoc />
    public async Task<OperationResult<Unit>> ReleaseEnvironmentVerificationLockAsync(
        AutomationLockState observed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (observed.Purpose != AutomationLockPurpose.EnvironmentVerification ||
            observed.SessionId is not null || string.IsNullOrWhiteSpace(observed.OwnerToken))
            return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet,
                "An environment-verification owner token is required for startup release.");

        try
        {
            using SqliteConnection connection = _connectionFactory.Open();
            int changed = await connection.ExecuteAsync(
                "UPDATE AutomationLock SET SessionId = NULL, AcquiredAtUtc = NULL, ProcessId = NULL, " +
                "MachineName = NULL, Purpose = NULL, OwnerToken = NULL " +
                "WHERE Id = 1 AND SessionId IS NULL AND Purpose = 'ENVIRONMENT_VERIFICATION' " +
                "AND OwnerToken = @OwnerToken AND ProcessId IS @ProcessId AND MachineName IS @MachineName;",
                new { observed.OwnerToken, observed.ProcessId, observed.MachineName });
            return changed == 1
                ? OperationResult.Ok()
                : OperationResult.Fail<Unit>(FailureCode.PersistenceError,
                    "The observed verification lock owner changed; the lock was not released.");
        }
        catch (SqliteException ex)
        {
            return OperationResult.Fail<Unit>(FailureCode.PersistenceError,
                $"The stale verification lock could not be released: {ex.Message}");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// One UPDATE of one nullable column, and every safety condition is part of its WHERE clause
    /// rather than a separate read before it. That is what makes the guarantee unraceable: a
    /// session that resumed, acquired the automation lock, or started an attempt between the
    /// caller's check and this write simply matches no row and nothing is written.
    /// <para>
    /// The statement names one column of one table. There is no DELETE here and no way to add
    /// one without rewriting the method: the customer source, the InputSnapshot bytes, every
    /// Revision file, every approved PNG and every production TIFF are unreachable from this
    /// SQL, and so are the session's own steps, attempts, reviews and outputs.
    /// </para>
    /// <para>
    /// Already-removed is refused rather than treated as success. Removing a record is an
    /// operator action with a visible result, and "nothing changed" and "the record was taken
    /// off the list" must not report the same thing.
    /// </para>
    /// </remarks>
    public async Task<OperationResult<Unit>> RemoveFromRecentAsync(
        SessionId id, DateTimeOffset atUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using SqliteConnection connection = _connectionFactory.Open();
            int changed = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE ProcessingSession SET RemovedFromRecentAtUtc = @at
                WHERE Id = @id
                  AND RemovedFromRecentAtUtc IS NULL
                  AND State IN ('COMPLETED', 'ABANDONED')
                  AND NOT EXISTS (SELECT 1 FROM ProcessingAttempt a
                                  WHERE a.SessionId = @id AND a.ResultStatus = 'RUNNING')
                  AND NOT EXISTS (SELECT 1 FROM AutomationLock l WHERE l.SessionId = @id);
                """,
                new { id = id.ToString(), at = Mappers.ToText(atUtc) },
                cancellationToken: cancellationToken));

            return changed == 1
                ? OperationResult.Ok()
                : OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet,
                    $"Session {id} is not a finished, idle record that can be taken off Recent Processing.");
        }
        catch (SqliteException ex)
        {
            return OperationResult.Fail<Unit>(FailureCode.PersistenceError,
                $"The record could not be taken off Recent Processing: {ex.Message}");
        }
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

    private static Task<int> UpdateRevisionReviewStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionId sessionId,
        RevisionReviewStateChange change)
    {
        if (change.ReviewState is not (Domain.Revisions.ReviewState.Approved or
            Domain.Revisions.ReviewState.Rejected))
        {
            throw new InvalidOperationException("A review decision cannot cache NotReviewed.");
        }

        const string sql =
            """
            UPDATE Revision
               SET ReviewState = @reviewState
             WHERE Id = @id AND SessionId = @sessionId AND Sha256 = @reviewedHash AND IsValid = 1;
            """;
        return connection.ExecuteAsync(sql, new
        {
            id = change.RevisionId.ToString(),
            sessionId = sessionId.ToString(),
            reviewedHash = change.ReviewedHash.Value,
            reviewState = Mappers.ToText(change.ReviewState),
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
                  AdapterNotes, ManualResultSourcePath,
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
                  @AdapterNotes, @ManualResultSourcePath,
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

    /// <summary>Appends one structured automation error (Jira 11108; MVP design §17.6).</summary>
    /// <remarks>
    /// A plain INSERT, never an upsert: the log is a record of what happened, and an error that
    /// could be rewritten in place would be a record of what someone last said happened.
    /// </remarks>
    private static Task InsertAutomationLogAsync(
        SqliteConnection connection, SqliteTransaction transaction, Domain.Automation.AutomationLogEntry entry)
    {
        AutomationLogRow row = Mappers.ToRow(entry);
        const string sql =
            """
            INSERT INTO AutomationLogEntry
                (Id, SessionId, StepKind, AtUtc, FailureCode, MessageKey, TechnicalDetail,
                 ContextJson, ScreenshotPath)
            VALUES
                (@Id, @SessionId, @StepKind, @AtUtc, @FailureCode, @MessageKey, @TechnicalDetail,
                 @ContextJson, @ScreenshotPath);
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

    /// <summary>
    /// Applies one correction-request change and returns how many rows it matched (SCRUM-11148).
    /// </summary>
    /// <remarks>
    /// Every update names the session and the exact prior state it expects in its WHERE clause, so
    /// a row that moved since the caller read it simply matches nothing and the caller rolls back.
    /// <see cref="CorrectionRequestChange.AssertBound"/> sets a column to itself: SQLite still
    /// reports the matched row, which is exactly the "still READY and still bound to this attempt"
    /// answer the closing transaction needs, and nothing is written.
    /// </remarks>
    private static Task<int> ApplyCorrectionRequestChangeAsync(
        SqliteConnection connection, SqliteTransaction transaction, SessionId sessionId, CorrectionRequestChange change)
    {
        string session = sessionId.ToString();
        return change switch
        {
            CorrectionRequestChange.Insert insert => insert.Row.Status != CorrectionRequestStatus.Preparing ||
                insert.Row.SessionId != sessionId
                ? Task.FromResult(0)
                : connection.ExecuteAsync(
                    """
                    INSERT INTO CorrectionRequest
                        (Id, SessionId, StepKind, HandedOutRevisionId, HandedOutSha256, ReferenceRevisionId,
                         ReferenceSha256, FolderRelativePath, ReferenceFileName, WorkingFileName,
                         SuggestedReturnName, Note, EffectiveReason, Status, CreatedAtUtc)
                    VALUES
                        (@Id, @SessionId, @StepKind, @HandedOutRevisionId, @HandedOutSha256, @ReferenceRevisionId,
                         @ReferenceSha256, @FolderRelativePath, @ReferenceFileName, @WorkingFileName,
                         @SuggestedReturnName, @Note, @EffectiveReason, 'PREPARING', @CreatedAtUtc);
                    """,
                    new
                    {
                        Id = insert.Row.Id.ToString("D"),
                        SessionId = session,
                        StepKind = Mappers.ToText(insert.Row.StepKind),
                        HandedOutRevisionId = insert.Row.HandedOutRevisionId.ToString(),
                        HandedOutSha256 = insert.Row.HandedOutSha256.Value,
                        ReferenceRevisionId = insert.Row.ReferenceRevisionId.ToString(),
                        ReferenceSha256 = insert.Row.ReferenceSha256.Value,
                        FolderRelativePath = insert.Row.Folder.RelativePath,
                        insert.Row.ReferenceFileName,
                        insert.Row.WorkingFileName,
                        insert.Row.SuggestedReturnName,
                        insert.Row.Note,
                        insert.Row.EffectiveReason,
                        CreatedAtUtc = Mappers.ToText(insert.Row.CreatedAtUtc),
                    },
                    transaction),
            CorrectionRequestChange.Supersede supersede => connection.ExecuteAsync(
                "UPDATE CorrectionRequest SET Status = 'SUPERSEDED', ClosedAtUtc = @at " +
                "WHERE Id = @id AND SessionId = @session AND Status IN ('PREPARING', 'READY');",
                new { id = supersede.Id.ToString("D"), session, at = Mappers.ToText(supersede.AtUtc) }, transaction),
            CorrectionRequestChange.MarkReady ready => connection.ExecuteAsync(
                "UPDATE CorrectionRequest SET Status = 'READY', ReadyAtUtc = @at " +
                "WHERE Id = @id AND SessionId = @session AND Status = 'PREPARING';",
                new { id = ready.Id.ToString("D"), session, at = Mappers.ToText(ready.AtUtc) }, transaction),
            CorrectionRequestChange.SetLastImportAttempt opened => connection.ExecuteAsync(
                "UPDATE CorrectionRequest SET LastImportAttemptId = @attempt " +
                "WHERE Id = @id AND SessionId = @session AND Status = 'READY';",
                new { id = opened.Id.ToString("D"), session, attempt = opened.Attempt.ToString() }, transaction),
            CorrectionRequestChange.AssertBound bound => connection.ExecuteAsync(
                "UPDATE CorrectionRequest SET Status = Status " +
                "WHERE Id = @id AND SessionId = @session AND Status = 'READY' AND LastImportAttemptId = @attempt;",
                new { id = bound.Id.ToString("D"), session, attempt = bound.Attempt.ToString() }, transaction),
            CorrectionRequestChange.MarkReturned returned => connection.ExecuteAsync(
                "UPDATE CorrectionRequest SET Status = 'RETURNED', ResultRevisionId = @result, ClosedAtUtc = @at " +
                "WHERE Id = @id AND SessionId = @session AND Status = 'READY' AND LastImportAttemptId = @attempt;",
                new
                {
                    id = returned.Id.ToString("D"), session, attempt = returned.Attempt.ToString(),
                    result = returned.Result.ToString(), at = Mappers.ToText(returned.AtUtc),
                },
                transaction),
            _ => Task.FromResult(0),
        };
    }

    private static Task<int> ApplyLockChangeAsync(
        SqliteConnection connection, SqliteTransaction transaction, AutomationLockChange change)
    {
        string sql = change.Action == AutomationLockAction.Acquire
            ? "UPDATE AutomationLock SET SessionId = @sessionId, AcquiredAtUtc = @atUtc, " +
              "ProcessId = @processId, MachineName = @machineName, Purpose = 'SESSION', OwnerToken = NULL " +
              "WHERE Id = 1 AND (SessionId IS NULL OR SessionId = @sessionId) " +
              "AND (Purpose IS NULL OR Purpose = 'SESSION') AND OwnerToken IS NULL;"
            : "UPDATE AutomationLock SET SessionId = NULL, AcquiredAtUtc = NULL, ProcessId = NULL, " +
              "MachineName = NULL, Purpose = NULL, OwnerToken = NULL " +
              "WHERE Id = 1 AND ((SessionId = @sessionId AND (Purpose IS NULL OR Purpose = 'SESSION')) " +
              "OR (SessionId IS NULL AND Purpose IS NULL AND OwnerToken IS NULL));";

        return connection.ExecuteAsync(sql, new
        {
            sessionId = change.SessionId.ToString(),
            atUtc = Mappers.ToText(change.AtUtc),
            processId = change.ProcessId,
            machineName = change.MachineName,
        }, transaction);
    }
}

/// <summary>One <c>CorrectionRequest</c> row as stored (SCRUM-11148, migration 0019).</summary>
internal sealed class CorrectionRequestRow
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string StepKind { get; set; } = "";
    public string HandedOutRevisionId { get; set; } = "";
    public string HandedOutSha256 { get; set; } = "";
    public string ReferenceRevisionId { get; set; } = "";
    public string ReferenceSha256 { get; set; } = "";
    public string FolderRelativePath { get; set; } = "";
    public string ReferenceFileName { get; set; } = "";
    public string WorkingFileName { get; set; } = "";
    public string SuggestedReturnName { get; set; } = "";
    public string? Note { get; set; }
    public string EffectiveReason { get; set; } = "";
    public string Status { get; set; } = "";
    public string CreatedAtUtc { get; set; } = "";
    public string? ReadyAtUtc { get; set; }
    public string? ClosedAtUtc { get; set; }
    public string? LastImportAttemptId { get; set; }
    public string? ResultRevisionId { get; set; }

    public static CorrectionRequest ToDomain(CorrectionRequestRow row) => new(
        Guid.Parse(row.Id),
        Domain.Ids.SessionId.From(Guid.Parse(row.SessionId)),
        Mappers.ToStepKind(row.StepKind),
        RevisionId.From(Guid.Parse(row.HandedOutRevisionId)),
        Domain.Files.Sha256.Parse(row.HandedOutSha256),
        RevisionId.From(Guid.Parse(row.ReferenceRevisionId)),
        Domain.Files.Sha256.Parse(row.ReferenceSha256),
        Domain.Files.WorkspaceDirRef.Create(row.FolderRelativePath),
        row.ReferenceFileName,
        row.WorkingFileName,
        row.SuggestedReturnName,
        row.Note,
        row.EffectiveReason,
        row.Status switch
        {
            "PREPARING" => CorrectionRequestStatus.Preparing,
            "READY" => CorrectionRequestStatus.Ready,
            "RETURNED" => CorrectionRequestStatus.Returned,
            "SUPERSEDED" => CorrectionRequestStatus.Superseded,
            _ => throw new InvalidOperationException($"Unknown correction request status '{row.Status}'."),
        },
        Mappers.ToDateTimeOffset(row.CreatedAtUtc),
        Mappers.ToDateTimeOffsetOrNull(row.ReadyAtUtc),
        Mappers.ToDateTimeOffsetOrNull(row.ClosedAtUtc),
        row.LastImportAttemptId is { } attempt ? AttemptId.From(Guid.Parse(attempt)) : null,
        row.ResultRevisionId is { } result ? RevisionId.From(Guid.Parse(result)) : null);
}
