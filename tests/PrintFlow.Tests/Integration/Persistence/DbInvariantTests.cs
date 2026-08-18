using Microsoft.Data.Sqlite;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// Jira 11105/11108: invariants enforced by the database itself, proven by attempting the
/// illegal statement directly — not through the repository, which never issues one
/// (task §29–§31, §48).
/// </summary>
public sealed class DbInvariantTests
{
    private const string SessionId = "11111111-1111-1111-1111-111111111111";
    private const string RevisionId = "22222222-2222-2222-2222-222222222222";
    private const string AttemptId = "33333333-3333-3333-3333-333333333333";
    private const string ReviewId = "44444444-4444-4444-4444-444444444444";

    private static void SeedSessionAndRevision(SqliteConnection connection)
    {
        Execute(connection,
            """
            INSERT INTO ProcessingSession
                (Id, WorkflowType, OutputName, CurrentStep, State, WorkspacePath, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                ($id, 'PREPARE_ASSET', 'test', 'Import', 'ACTIVE', 'Sessions/x', '2026-08-19T00:00:00.000Z', '2026-08-19T00:00:00.000Z');
            """, ("$id", SessionId));

        Execute(connection,
            $"""
            INSERT INTO Revision
                (Id, SessionId, SourceRevisionId, Operation, RelativePath, Format, ByteLength, Sha256,
                 ColourMode, CreatedAtUtc)
            VALUES
                ($id, $session, NULL, 'IMPORT', 'Sessions/x/Source/a.png', 'PNG', 10,
                 '{new string('0', 64)}', 'RGB', '2026-08-19T00:00:00.000Z');
            """,
            ("$id", RevisionId), ("$session", SessionId));
    }

    private static void Execute(SqliteConnection connection, string sql, params (string, object)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    [Fact]
    public void A_succeeded_attempt_requires_an_output_revision()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();
        SeedSessionAndRevision(connection);

        Should.Throw<SqliteException>(() => Execute(connection,
            """
            INSERT INTO ProcessingAttempt
                (Id, SessionId, StepKind, Operation, AdapterId, StartedAtUtc, ResultStatus, OutputRevisionId)
            VALUES
                ($id, $session, 'Import', 'IMPORT', 'test', '2026-08-19T00:00:00.000Z', 'SUCCEEDED', NULL);
            """, ("$id", AttemptId), ("$session", SessionId)));
    }

    [Fact]
    public void A_non_succeeded_attempt_must_not_carry_an_output_revision()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();
        SeedSessionAndRevision(connection);

        Should.Throw<SqliteException>(() => Execute(connection,
            """
            INSERT INTO ProcessingAttempt
                (Id, SessionId, StepKind, Operation, AdapterId, StartedAtUtc, ResultStatus, OutputRevisionId)
            VALUES
                ($id, $session, 'Import', 'IMPORT', 'test', '2026-08-19T00:00:00.000Z', 'FAILED', $revision);
            """, ("$id", AttemptId), ("$session", SessionId), ("$revision", RevisionId)));
    }

    [Fact]
    public void A_running_attempt_with_no_output_is_accepted()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();
        SeedSessionAndRevision(connection);

        Should.NotThrow(() => Execute(connection,
            """
            INSERT INTO ProcessingAttempt
                (Id, SessionId, StepKind, Operation, AdapterId, StartedAtUtc, ResultStatus, OutputRevisionId)
            VALUES
                ($id, $session, 'Import', 'IMPORT', 'test', '2026-08-19T00:00:00.000Z', 'RUNNING', NULL);
            """, ("$id", AttemptId), ("$session", SessionId)));
    }

    [Fact]
    public void Revision_identity_columns_cannot_be_updated_directly()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();
        SeedSessionAndRevision(connection);

        Should.Throw<SqliteException>(() => Execute(connection,
            $"UPDATE Revision SET Sha256 = '{new string('F', 64)}' WHERE Id = $id;", ("$id", RevisionId)));

        Should.Throw<SqliteException>(() => Execute(connection,
            "UPDATE Revision SET RelativePath = 'Sessions/x/Source/moved.png' WHERE Id = $id;", ("$id", RevisionId)));
    }

    [Fact]
    public void Revision_validity_and_review_state_can_still_be_updated()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();
        SeedSessionAndRevision(connection);

        Should.NotThrow(() => Execute(connection,
            "UPDATE Revision SET IsValid = 0, InvalidatedAtUtc = '2026-08-19T01:00:00.000Z', InvalidationReason = 'REJECTED' WHERE Id = $id;",
            ("$id", RevisionId)));
    }

    [Fact]
    public void ReviewDecision_is_append_only_update_is_rejected()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();
        SeedSessionAndRevision(connection);
        InsertReview(connection);

        Should.Throw<SqliteException>(() => Execute(connection,
            "UPDATE ReviewDecision SET Decision = 'REJECTED' WHERE Id = $id;", ("$id", ReviewId)));
    }

    [Fact]
    public void ReviewDecision_is_append_only_delete_is_rejected()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();
        SeedSessionAndRevision(connection);
        InsertReview(connection);

        Should.Throw<SqliteException>(() => Execute(connection,
            "DELETE FROM ReviewDecision WHERE Id = $id;", ("$id", ReviewId)));
    }

    private static void InsertReview(SqliteConnection connection) => Execute(connection,
        $"""
        INSERT INTO ReviewDecision
            (Id, SessionId, StepKind, SubjectKind, SubjectId, ReviewedSha256, Operator, DecidedAtUtc, Decision)
        VALUES
            ($id, $session, 'Import', 'REVISION', $revision, '{new string('0', 64)}', 'tester',
             '2026-08-19T00:00:00.000Z', 'APPROVED');
        """, ("$id", ReviewId), ("$session", SessionId), ("$revision", RevisionId));

    [Fact]
    public void AutomationLock_singleton_row_exists_after_migration()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM AutomationLock WHERE Id = 1;";
        Convert.ToInt64(command.ExecuteScalar()).ShouldBe(1L);
    }
}
