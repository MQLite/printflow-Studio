using System.IO;
using Microsoft.Data.Sqlite;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// Jira 11108: forward-only, <c>PRAGMA user_version</c>-gated migrations (task §27, §48).
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class MigrationTests
{
    [Fact]
    public void Empty_database_migrates_successfully()
    {
        using TempDatabase database = new();

        using SqliteConnection connection = database.Factory.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";

        // Against the newest script this build carries rather than a literal, so adding a
        // migration cannot leave this passing while meaning something weaker.
        Convert.ToInt64(command.ExecuteScalar()).ShouldBe(MigrationRunner.NewestKnownVersion);

        using SqliteCommand tables = connection.CreateCommand();
        tables.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'Revision';";
        tables.ExecuteScalar().ShouldNotBeNull();
    }

    [Fact]
    public void Repeated_migration_is_a_no_op()
    {
        using TempDatabase database = new();

        using SqliteConnection connection = database.Factory.Open();
        var second = MigrationRunner.Migrate(connection);

        second.IsSuccess.ShouldBeTrue();

        // One audit row per script, and re-running adds none: the second Migrate call above
        // found every version already applied and did nothing.
        using SqliteCommand count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM SchemaMigration;";
        Convert.ToInt64(count.ExecuteScalar()).ShouldBe(MigrationRunner.NewestKnownVersion);
    }

    [Fact]
    public void A_database_user_version_ahead_of_this_build_fails_closed()
    {
        using TempDatabase database = new();

        using (SqliteConnection connection = database.Factory.Open())
        using (SqliteCommand bump = connection.CreateCommand())
        {
            bump.CommandText = "PRAGMA user_version = 999;";
            bump.ExecuteNonQuery();
        }

        using SqliteConnection reopened = database.Factory.Open();
        var result = MigrationRunner.Migrate(reopened);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(PrintFlow.Domain.Results.FailureCode.PersistenceError);
    }

    [Fact]
    public void Required_pragmas_are_applied_to_every_connection()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();

        AssertPragma(connection, "journal_mode", "wal");
        AssertPragma(connection, "synchronous", "2"); // FULL
        AssertPragma(connection, "foreign_keys", "1");
    }

    /// <summary>
    /// A database written before 0002 gains the trim parameter columns and keeps its rows
    /// (Epic 11200 release gate §18).
    /// </summary>
    /// <remarks>
    /// The upgrade path no other test covered. <c>Empty_database_migrates_successfully</c> only
    /// ever exercises a database that had every script applied in one pass, so it would still
    /// pass if 0002 were unrunnable against an existing schema — an <c>ALTER TABLE</c> that only
    /// ever runs against a table created moments earlier is not evidence that it runs against
    /// the operator's real database.
    /// <para>
    /// The pre-0002 state is built from the 0001 script itself rather than a hand-copied
    /// <c>CREATE TABLE</c>, so the starting point cannot drift away from what shipped. The
    /// session row written before the upgrade is the point of the test: it must survive, and its
    /// new columns must read NULL — "this row recorded no margin", never "the default was used"
    /// (migration 0002).
    /// </para>
    /// </remarks>
    [Fact]
    public void A_pre_0002_database_upgrades_and_keeps_its_rows()
    {
        using TempDatabase database = new(migrate: false);

        using (SqliteConnection seeded = database.OpenRaw())
        {
            Execute(seeded, ReadMigrationScript("0001_initial_schema.sql"));
            Execute(
                seeded,
                "INSERT INTO SchemaMigration (Version, Name, AppliedAtUtc, ScriptSha256) " +
                "VALUES (1, 'initial_schema', '2026-01-01T00:00:00.000Z', 'SEED');");
            Execute(seeded, "PRAGMA user_version = 1;");

            // A session that existed before the trim columns did.
            Execute(
                seeded,
                """
                INSERT INTO ProcessingSession
                    (Id, WorkflowType, OutputName, CurrentStep, State, WorkspacePath,
                     CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    ('legacy-session', 'PREPARE_ASSET', 'legacy', 'Trim', 'ACTIVE', 'Sessions/legacy',
                     '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.000Z');
                """);
        }

        using SqliteConnection upgraded = database.OpenRaw();
        var result = MigrationRunner.Migrate(upgraded);

        result.IsSuccess.ShouldBeTrue();
        ReadUserVersion(upgraded).ShouldBe(MigrationRunner.NewestKnownVersion);

        foreach (string table in new[] { "ProcessingSession", "ProcessingAttempt" })
        {
            IReadOnlyList<string> columns = ColumnsOf(upgraded, table);
            foreach (string column in new[]
                     {
                         "TrimMode", "TrimMarginTop", "TrimMarginRight",
                         "TrimMarginBottom", "TrimMarginLeft",
                     })
            {
                columns.ShouldContain(column, $"{table} is missing {column} after the upgrade.");
            }
        }

        // The row is still there, and says nothing about a margin nobody recorded.
        using SqliteCommand row = upgraded.CreateCommand();
        row.CommandText =
            "SELECT TrimMode, TrimMarginTop FROM ProcessingSession WHERE Id = 'legacy-session';";
        using SqliteDataReader reader = row.ExecuteReader();
        reader.Read().ShouldBeTrue();
        reader.IsDBNull(0).ShouldBeTrue();
        reader.IsDBNull(1).ShouldBeTrue();
    }

    /// <summary>
    /// A database written before 0003 gains the background-removal columns and keeps its rows
    /// (Epic 11300 Part C2B1 §28).
    /// </summary>
    /// <remarks>
    /// The starting point is built by replaying 0001 and 0002 off the shipped assembly, so it is
    /// the schema that actually shipped rather than a copy that can fall behind it. The session
    /// row written before the columns existed is the point: it must survive, and its new columns
    /// must read NULL -- "this session recorded no decision", never "the default was used". There
    /// is no default, and a session that recorded nothing still needs an explicit authority
    /// before background removal can run (§7).
    /// </remarks>
    [Fact]
    public void A_pre_0003_database_upgrades_and_keeps_its_rows()
    {
        using TempDatabase database = new(migrate: false);

        using (SqliteConnection seeded = database.OpenRaw())
        {
            Execute(seeded, ReadMigrationScript("0001_initial_schema.sql"));
            Execute(seeded, ReadMigrationScript("0002_trim_parameters.sql"));
            Execute(
                seeded,
                "INSERT INTO SchemaMigration (Version, Name, AppliedAtUtc, ScriptSha256) " +
                "VALUES (1, 'initial_schema', '2026-01-01T00:00:00.000Z', 'SEED'), " +
                "       (2, 'trim_parameters', '2026-01-01T00:00:00.000Z', 'SEED');");
            Execute(seeded, "PRAGMA user_version = 2;");

            Execute(
                seeded,
                """
                INSERT INTO ProcessingSession
                    (Id, WorkflowType, OutputName, CurrentStep, State, WorkspacePath,
                     CreatedAtUtc, UpdatedAtUtc, TrimMode, TrimMarginTop, TrimMarginRight,
                     TrimMarginBottom, TrimMarginLeft)
                VALUES
                    ('pre-0003-session', 'PREPARE_ASSET', 'legacy', 'BackgroundRemoval', 'ACTIVE',
                     'Sessions/pre-0003', '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.000Z',
                     'UNIFORM_MARGIN', 3, 3, 3, 3);
                """);
        }

        using SqliteConnection upgraded = database.OpenRaw();
        var result = MigrationRunner.Migrate(upgraded);

        result.IsSuccess.ShouldBeTrue();
        ReadUserVersion(upgraded).ShouldBe(MigrationRunner.NewestKnownVersion);

        foreach (string table in new[] { "ProcessingSession", "ProcessingAttempt" })
        {
            IReadOnlyList<string> columns = ColumnsOf(upgraded, table);
            foreach (string column in new[]
                     {
                         "BackgroundRemovalDecision", "BackgroundRemovalRevisionId",
                         "BackgroundRemovalReviewedSha",
                     })
            {
                columns.ShouldContain(column, $"{table} is missing {column} after the upgrade.");
            }
        }

        // The row survived with the value it did record, and says nothing about a decision it
        // never made.
        using SqliteCommand row = upgraded.CreateCommand();
        row.CommandText =
            "SELECT TrimMode, BackgroundRemovalDecision, BackgroundRemovalRevisionId, " +
            "       BackgroundRemovalReviewedSha " +
            "FROM ProcessingSession WHERE Id = 'pre-0003-session';";
        using SqliteDataReader reader = row.ExecuteReader();
        reader.Read().ShouldBeTrue();
        reader.GetString(0).ShouldBe("UNIFORM_MARGIN");
        reader.IsDBNull(1).ShouldBeTrue();
        reader.IsDBNull(2).ShouldBeTrue();
        reader.IsDBNull(3).ShouldBeTrue();
    }

    /// <summary>
    /// A database written before 0005 gains the plan columns, keeps its rows, and has its
    /// existing print sizes marked as the legacy exact pairs they were
    /// (Epic 11400 Part B1A.2A §9, §21).
    /// </summary>
    /// <remarks>
    /// The upgrade this slice turns on, and the assertion that matters is the backfill. Two rows
    /// are seeded on purpose: one that recorded a size under the old reading, and one that
    /// recorded none. The first must come back saying <c>LEGACY_EXACT_PAIR</c> — stated rather
    /// than inferred, because this migration is the last moment at which "that pair was written
    /// under the old contract" is still certain — and the second must stay NULL, because a session
    /// that never chose a size has no reading to record.
    /// <para>
    /// Neither row gains a plan. That is the point of §10: a legacy pair is readable and is not
    /// executable, and nothing here converts one into a fit box because its numbers happen to
    /// suit.
    /// </para>
    /// <para>
    /// The starting point is built by replaying 0001–0004 off the shipped assembly, so it is the
    /// schema that actually shipped rather than a copy that can fall behind it.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_pre_0005_database_upgrades_and_marks_existing_sizes_legacy()
    {
        using TempDatabase database = new(migrate: false);

        using (SqliteConnection seeded = database.OpenRaw())
        {
            Execute(seeded, ReadMigrationScript("0001_initial_schema.sql"));
            Execute(seeded, ReadMigrationScript("0002_trim_parameters.sql"));
            Execute(seeded, ReadMigrationScript("0003_background_removal_decision.sql"));
            Execute(seeded, ReadMigrationScript("0004_attempt_adapter_notes.sql"));
            Execute(
                seeded,
                "INSERT INTO SchemaMigration (Version, Name, AppliedAtUtc, ScriptSha256) " +
                "VALUES (1, 'initial_schema', '2026-01-01T00:00:00.000Z', 'SEED'), " +
                "       (2, 'trim_parameters', '2026-01-01T00:00:00.000Z', 'SEED'), " +
                "       (3, 'background_removal_decision', '2026-01-01T00:00:00.000Z', 'SEED'), " +
                "       (4, 'attempt_adapter_notes', '2026-01-01T00:00:00.000Z', 'SEED');");
            Execute(seeded, "PRAGMA user_version = 4;");

            // One session that recorded a size under the old exact-pair reading, and one that
            // never recorded a size at all.
            Execute(
                seeded,
                """
                INSERT INTO ProcessingSession
                    (Id, WorkflowType, OutputName, CurrentStep, State, WorkspacePath,
                     CreatedAtUtc, UpdatedAtUtc,
                     DimensionsWidthMm, DimensionsHeightMm, DimensionsPixelWidth,
                     DimensionsPixelHeight, DimensionsPreset)
                VALUES
                    ('sized-session', 'GENERATE_PRINT_TIFF', 'sized', 'PhotoshopOutput', 'ACTIVE',
                     'Sessions/sized', '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.000Z',
                     200.0, 150.0, 2362, 1772, 'CUSTOM'),
                    ('unsized-session', 'GENERATE_PRINT_TIFF', 'unsized', 'PrintDimensions', 'ACTIVE',
                     'Sessions/unsized', '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.000Z',
                     NULL, NULL, NULL, NULL, NULL);
                """);
        }

        using SqliteConnection upgraded = database.OpenRaw();
        var result = MigrationRunner.Migrate(upgraded);

        result.IsSuccess.ShouldBeTrue();
        ReadUserVersion(upgraded).ShouldBe(MigrationRunner.NewestKnownVersion);

        // Every plan column exists on both tables; the semantics marker only on the session,
        // because an attempt's plan is a maximum-bound plan by construction.
        string[] planColumns =
        [
            "PrintPlanSourceRevisionId", "PrintPlanSourceSha256",
            "PrintPlanSourcePixelWidth", "PrintPlanSourcePixelHeight",
            "PrintPlanMaxWidthMm", "PrintPlanMaxHeightMm", "PrintPlanLimitKind",
            "PrintPlanMode", "PrintPlanLimitingEdge", "PrintPlanLimitingValueMm",
            "PrintPlanProjectedPixelWidth", "PrintPlanProjectedPixelHeight",
            "PrintPlanProductionDpi", "PrintPlanResizePolicy",
        ];

        foreach (string table in new[] { "ProcessingSession", "ProcessingAttempt" })
        {
            IReadOnlyList<string> columns = ColumnsOf(upgraded, table);
            foreach (string column in planColumns)
            {
                columns.ShouldContain(column, $"{table} is missing {column} after the upgrade.");
            }
        }

        ColumnsOf(upgraded, "ProcessingSession").ShouldContain("DimensionSemantics");
        ColumnsOf(upgraded, "ProcessingAttempt").ShouldNotContain("DimensionSemantics");

        // The sized row survived with the millimetres it recorded, is explicitly marked legacy,
        // and gained no plan.
        using (SqliteCommand sized = upgraded.CreateCommand())
        {
            sized.CommandText =
                "SELECT DimensionsWidthMm, DimensionSemantics, PrintPlanSourceRevisionId, " +
                "       PrintPlanLimitingEdge, PrintPlanMode " +
                "FROM ProcessingSession WHERE Id = 'sized-session';";
            using SqliteDataReader reader = sized.ExecuteReader();
            reader.Read().ShouldBeTrue();
            reader.GetDouble(0).ShouldBe(200.0);
            reader.GetString(1).ShouldBe("LEGACY_EXACT_PAIR");
            reader.IsDBNull(2).ShouldBeTrue("a legacy pair is not a plan and gains none");
            reader.IsDBNull(3).ShouldBeTrue("nothing infers which old dimension was the limiting edge");
            reader.IsDBNull(4).ShouldBeTrue();
        }

        // The row that never chose a size has no reading to record, which is not the same as
        // being legacy.
        using SqliteCommand unsized = upgraded.CreateCommand();
        unsized.CommandText =
            "SELECT DimensionSemantics FROM ProcessingSession WHERE Id = 'unsized-session';";
        using SqliteDataReader unsizedReader = unsized.ExecuteReader();
        unsizedReader.Read().ShouldBeTrue();
        unsizedReader.IsDBNull(0).ShouldBeTrue();
    }

    /// <summary>
    /// A complete maximum-bound plan round-trips through the columns exactly (§21).
    /// </summary>
    [Fact]
    public void A_complete_maximum_bound_plan_round_trips_through_its_columns()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();

        InsertSession(connection, "planned", CompletePlanColumns);

        using SqliteCommand read = connection.CreateCommand();
        read.CommandText =
            "SELECT DimensionSemantics, PrintPlanSourceSha256, PrintPlanSourcePixelWidth, " +
            "       PrintPlanMaxWidthMm, PrintPlanMode, PrintPlanLimitingEdge, " +
            "       PrintPlanLimitingValueMm, PrintPlanProjectedPixelWidth, " +
            "       PrintPlanProductionDpi, PrintPlanResizePolicy " +
            "FROM ProcessingSession WHERE Id = 'planned';";

        using SqliteDataReader reader = read.ExecuteReader();
        reader.Read().ShouldBeTrue();
        reader.GetString(0).ShouldBe("MAX_BOUNDS_V1");
        reader.GetString(1).ShouldBe(new string('a', 64));
        reader.GetInt32(2).ShouldBe(2000);
        reader.GetDouble(3).ShouldBe(50.0);
        reader.GetString(4).ShouldBe("PROPORTIONAL_SHRINK");
        reader.GetString(5).ShouldBe("WIDTH");
        reader.GetDouble(6).ShouldBe(50.0);
        reader.GetInt32(7).ShouldBe(591);
        reader.GetInt32(8).ShouldBe(300);
        reader.GetString(9).ShouldBe("BICUBIC_SHARPER");
    }

    /// <summary>
    /// A half-written plan is refused by the database itself (§13, §21).
    /// </summary>
    /// <remarks>
    /// The all-or-nothing rule, held by a CHECK rather than only by the mapper. Every column named
    /// here is one a reader would otherwise have to invent, and an invented limiting edge is an
    /// edge Photoshop would be given that nobody calculated.
    /// <para>
    /// <c>PrintPlanLimitingValueMm</c> is deliberately absent from this list: its absence is
    /// meaningful — a resolution-only plan writes no millimetre value — and the mode is what
    /// pairs with it, which the domain type enforces on the way out of the database.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("PrintPlanSourceRevisionId")]
    [InlineData("PrintPlanSourceSha256")]
    [InlineData("PrintPlanSourcePixelWidth")]
    [InlineData("PrintPlanMaxWidthMm")]
    [InlineData("PrintPlanLimitKind")]
    [InlineData("PrintPlanMode")]
    [InlineData("PrintPlanLimitingEdge")]
    [InlineData("PrintPlanProjectedPixelWidth")]
    [InlineData("PrintPlanProjectedPixelHeight")]
    [InlineData("PrintPlanProductionDpi")]
    [InlineData("PrintPlanResizePolicy")]
    public void A_partial_maximum_bound_plan_row_is_refused(string omitted)
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();

        (string Column, string Value)[] partial =
            [.. CompletePlanColumns.Where(c => c.Column != omitted)];

        Should.Throw<SqliteException>(() => InsertSession(connection, "partial", partial))
            .Message.ShouldContain("CHECK constraint failed");
    }

    /// <summary>
    /// A production resolution other than the fixed 300 ppi cannot be stored (§13).
    /// </summary>
    [Fact]
    public void A_plan_row_claiming_another_production_resolution_is_refused()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();

        (string Column, string Value)[] wrongDpi =
        [
            .. CompletePlanColumns.Select(c =>
                c.Column == "PrintPlanProductionDpi" ? (c.Column, "150") : c),
        ];

        Should.Throw<SqliteException>(() => InsertSession(connection, "wrong-dpi", wrongDpi));
    }

    /// <summary>An unrecognised semantics marker cannot be stored (§13).</summary>
    [Fact]
    public void An_unsupported_dimension_semantics_value_is_refused()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();

        Should.Throw<SqliteException>(() => InsertSession(
            connection, "future", [("DimensionSemantics", "'MAX_BOUNDS_V2'")]));
    }

    // -------------------------------------------------------------------------------------
    // 0006: the flexible-size selection, the TargetEdgeV1 plan and the enlargement authority
    // (Epic 11400 Part B1A.2D §20, §21, §22, §34)
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A 0005 database upgrades, and every row it already held keeps exactly what it meant
    /// (§20, §21).
    /// </summary>
    /// <remarks>
    /// This is the test that guards the table rebuild. 0006 has to widen a CHECK constraint, which
    /// SQLite can only do by recreating <c>ProcessingSession</c> — so the risk is not a missing
    /// column, it is a column, a value or a constraint silently dropped on the way through. Three
    /// kinds of row are seeded and read back afterwards: a legacy exact pair, a complete
    /// MaxBoundsV1 plan, and a session that never chose a size.
    /// <para>
    /// None of them gains flexible-size meaning. A valid MaxBoundsV1 session stays a MaxBoundsV1
    /// session under its original accepted semantics rather than being upgraded to a target edge
    /// because v1.11.0 is now configured (§18, §21).
    /// </para>
    /// </remarks>
    [Fact]
    public void A_pre_0006_database_upgrades_and_leaves_every_existing_row_as_it_was()
    {
        using TempDatabase database = new(migrate: false);

        using (SqliteConnection seeded = database.OpenRaw())
        {
            Execute(seeded, ReadMigrationScript("0001_initial_schema.sql"));
            Execute(seeded, ReadMigrationScript("0002_trim_parameters.sql"));
            Execute(seeded, ReadMigrationScript("0003_background_removal_decision.sql"));
            Execute(seeded, ReadMigrationScript("0004_attempt_adapter_notes.sql"));
            Execute(seeded, ReadMigrationScript("0005_maximum_bound_print_plan.sql"));
            Execute(
                seeded,
                "INSERT INTO SchemaMigration (Version, Name, AppliedAtUtc, ScriptSha256) " +
                "VALUES (1, 'initial_schema', '2026-01-01T00:00:00.000Z', 'SEED'), " +
                "       (2, 'trim_parameters', '2026-01-01T00:00:00.000Z', 'SEED'), " +
                "       (3, 'background_removal_decision', '2026-01-01T00:00:00.000Z', 'SEED'), " +
                "       (4, 'attempt_adapter_notes', '2026-01-01T00:00:00.000Z', 'SEED'), " +
                "       (5, 'maximum_bound_print_plan', '2026-01-01T00:00:00.000Z', 'SEED');");
            Execute(seeded, "PRAGMA user_version = 5;");

            InsertSession(seeded, "legacy-session",
            [
                ("DimensionsWidthMm", "200.0"),
                ("DimensionsHeightMm", "150.0"),
                ("DimensionsPreset", "'CUSTOM'"),
                ("DimensionSemantics", "'LEGACY_EXACT_PAIR'"),
                ("TrimMode", "'UNIFORM_MARGIN'"),
                ("TrimMarginTop", "4"),
                ("WhiteUnderbaseBranch", "'W1_2PX'"),
            ]);
            InsertSession(seeded, "bounded-session", CompletePlanColumns);
            InsertSession(seeded, "unsized-session", []);

            // A child row, so the rebuild's DROP is proved not to have cascaded it away.
            Execute(
                seeded,
                """
                INSERT INTO SessionStep
                    (SessionId, StepKind, Ordinal, State, AttemptCount, EnteredStateAtUtc)
                VALUES
                    ('bounded-session', 'PrintDimensions', 2, 'APPROVED', 0,
                     '2026-01-01T00:00:00.000Z');
                """);
        }

        using SqliteConnection upgraded = database.OpenRaw();
        MigrationRunner.Migrate(upgraded).IsSuccess.ShouldBeTrue();
        ReadUserVersion(upgraded).ShouldBe(MigrationRunner.NewestKnownVersion);

        // Nothing was lost by the rebuild: every table still holds its rows, and the child row
        // whose foreign key points at the recreated table is still there.
        ScalarOf(upgraded, "SELECT COUNT(*) FROM ProcessingSession;").ShouldBe(3L);
        ScalarOf(upgraded, "SELECT COUNT(*) FROM SessionStep WHERE SessionId = 'bounded-session';")
            .ShouldBe(1L);

        // Foreign keys are back on for ordinary work. The runner suspends them for the migration
        // pass — a rebuild's DROP would otherwise cascade the child rows away — and restores them
        // before the connection is used for anything else, so no application query ever runs
        // unenforced (§20).
        ScalarOf(upgraded, "PRAGMA foreign_keys;").ShouldBe(1L);

        IReadOnlyList<string> sessionColumns = ColumnsOf(upgraded, "ProcessingSession");
        foreach (string column in FlexibleColumns)
        {
            sessionColumns.ShouldContain(column, $"ProcessingSession is missing {column}.");
            ColumnsOf(upgraded, "ProcessingAttempt")
                .ShouldContain(column, $"ProcessingAttempt is missing {column}.");
        }

        // Every column the earlier migrations added is still present and still typed.
        foreach (string column in new[]
                 {
                     "DimensionsWidthMm", "DimensionsPreset", "WhiteUnderbaseBranch", "TrimMode",
                     "TrimMarginTop", "BackgroundRemovalDecision", "DimensionSemantics",
                     "PrintPlanLimitingEdge", "PrintPlanResizePolicy",
                 })
        {
            sessionColumns.ShouldContain(column, $"the rebuild dropped {column}.");
        }

        // The legacy row is untouched, and gains nothing flexible.
        using (SqliteCommand legacy = upgraded.CreateCommand())
        {
            legacy.CommandText =
                "SELECT DimensionSemantics, DimensionsWidthMm, TrimMarginTop, WhiteUnderbaseBranch, " +
                "       SizingMode, TargetPlanResizePolicy, EnlargementAuthoritySourceRevisionId " +
                "FROM ProcessingSession WHERE Id = 'legacy-session';";
            using SqliteDataReader reader = legacy.ExecuteReader();
            reader.Read().ShouldBeTrue();
            reader.GetString(0).ShouldBe("LEGACY_EXACT_PAIR");
            reader.GetDouble(1).ShouldBe(200.0);
            reader.GetInt32(2).ShouldBe(4);
            reader.GetString(3).ShouldBe("W1_2PX");
            reader.IsDBNull(4).ShouldBeTrue("a legacy row acquires no sizing mode");
            reader.IsDBNull(5).ShouldBeTrue("a legacy row acquires no target-edge plan");
            reader.IsDBNull(6).ShouldBeTrue("no historical row acquires enlargement authority");
        }

        // The maximum-bound row keeps its own semantics and its whole plan.
        using SqliteCommand bounded = upgraded.CreateCommand();
        bounded.CommandText =
            "SELECT DimensionSemantics, PrintPlanLimitingEdge, PrintPlanLimitingValueMm, " +
            "       PrintPlanProjectedPixelWidth, PrintPlanResizePolicy, SizingMode, " +
            "       TargetPlanResizePolicy " +
            "FROM ProcessingSession WHERE Id = 'bounded-session';";
        using SqliteDataReader boundedReader = bounded.ExecuteReader();
        boundedReader.Read().ShouldBeTrue();
        boundedReader.GetString(0).ShouldBe("MAX_BOUNDS_V1");
        boundedReader.GetString(1).ShouldBe("WIDTH");
        boundedReader.GetDouble(2).ShouldBe(50.0);
        boundedReader.GetInt32(3).ShouldBe(591);
        boundedReader.GetString(4).ShouldBe("BICUBIC_SHARPER");
        boundedReader.IsDBNull(5).ShouldBeTrue("a MaxBoundsV1 row is not upgraded to TargetEdgeV1");
        boundedReader.IsDBNull(6).ShouldBeTrue();
    }

    /// <summary>The widened semantics vocabulary accepts exactly three readings (§5).</summary>
    /// <remarks>
    /// The rebuild's whole purpose, asserted directly: TARGET_EDGE_V1 is storable and a fourth
    /// value still is not. A build that widened the CHECK to anything would have lost the
    /// fail-closed property 0005 established.
    /// </remarks>
    [Fact]
    public void The_target_edge_reading_is_storable_and_a_fourth_reading_is_not()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();

        InsertSession(connection, "target-edge", CompleteTargetEdgeColumns);

        Should.Throw<SqliteException>(() => InsertSession(
            connection, "future", [("DimensionSemantics", "'TARGET_EDGE_V2'")]));
    }

    /// <summary>
    /// A complete TargetEdgeV1 selection, plan and authority round-trip through their columns
    /// exactly — the operator's decimal and the reduced ratio included (§7, §34).
    /// </summary>
    [Fact]
    public void A_complete_target_edge_plan_and_authority_round_trip_through_their_columns()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();

        InsertSession(connection, "flexible",
            [.. CompleteTargetEdgeColumns, .. CompleteAuthorityColumns]);

        using SqliteCommand read = connection.CreateCommand();
        read.CommandText =
            "SELECT SizingMode, SizingPreset, SizingRecommendationKind, " +
            "       SizingRecommendationMaxWidthMm, SizingPresetOverridden, SizingTargetEdge, " +
            "       SizingRequestedMm, TargetPlanPhotoshopEdge, TargetPlanProjectedPixelWidth, " +
            "       TargetPlanScaleNumerator, TargetPlanScaleDenominator, TargetPlanDirection, " +
            "       TargetPlanResizePolicy, EnlargementAuthorityRequestedMm, " +
            "       EnlargementAuthorityScaleNumerator " +
            "FROM ProcessingSession WHERE Id = 'flexible';";

        using SqliteDataReader reader = read.ExecuteReader();
        reader.Read().ShouldBeTrue();
        reader.GetString(0).ShouldBe("CUSTOM_TARGET_EDGE");
        reader.GetString(1).ShouldBe("A5");
        reader.GetString(2).ShouldBe("MAXIMUM_LONG_EDGE");
        reader.GetString(3).ShouldBe("135");
        reader.GetInt32(4).ShouldBe(1);
        reader.GetString(5).ShouldBe("LONG_EDGE");

        // The exact decimal, character for character. A REAL column would have returned
        // 200.02499999999999 here, and the projected pixels below turn on the difference (§7).
        reader.GetString(6).ShouldBe("200.025");

        reader.GetString(7).ShouldBe("WIDTH");
        reader.GetInt32(8).ShouldBe(2363);
        reader.GetInt32(9).ShouldBe(2363);
        reader.GetInt32(10).ShouldBe(2000);
        reader.GetString(11).ShouldBe("ENLARGE");
        reader.GetString(12).ShouldBe("PRESERVE_DETAILS");
        reader.GetString(13).ShouldBe("200.025");
        reader.GetInt32(14).ShouldBe(2363);
    }

    /// <summary>A half-written flexible-size row cannot exist (§22).</summary>
    /// <remarks>
    /// Each column of the group is removed in turn. Every one of them is a value a reader would
    /// otherwise have to invent, and a plan with an invented resolved edge or an invented scale is
    /// a plan that describes an operation nobody calculated.
    /// </remarks>
    [Theory]
    [InlineData("SizingMode")]
    [InlineData("SizingTargetEdge")]
    [InlineData("SizingRequestedMm")]
    [InlineData("TargetPlanSourceRevisionId")]
    [InlineData("TargetPlanSourceSha256")]
    [InlineData("TargetPlanSourcePixelWidth")]
    [InlineData("TargetPlanSourcePixelHeight")]
    [InlineData("TargetPlanPhotoshopEdge")]
    [InlineData("TargetPlanProjectedPixelWidth")]
    [InlineData("TargetPlanProjectedPixelHeight")]
    [InlineData("TargetPlanScaleNumerator")]
    [InlineData("TargetPlanScaleDenominator")]
    [InlineData("TargetPlanProductionDpi")]
    [InlineData("TargetPlanDirection")]
    [InlineData("TargetPlanResizePolicy")]
    public void A_partial_target_edge_row_is_refused(string omitted)
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();

        (string Column, string Value)[] partial =
            [.. CompleteTargetEdgeColumns.Where(c => c.Column != omitted)];

        Should.Throw<SqliteException>(() => InsertSession(connection, "partial-target", partial))
            .Message.ShouldContain("CHECK constraint failed");
    }

    /// <summary>A half-written enlargement authority cannot exist (§9, §22).</summary>
    [Theory]
    [InlineData("EnlargementAuthoritySourceRevisionId")]
    [InlineData("EnlargementAuthoritySourceSha256")]
    [InlineData("EnlargementAuthoritySizingMode")]
    [InlineData("EnlargementAuthorityTargetEdge")]
    [InlineData("EnlargementAuthorityRequestedMm")]
    [InlineData("EnlargementAuthorityScaleNumerator")]
    [InlineData("EnlargementAuthorityScaleDenominator")]
    [InlineData("EnlargementAuthorityProjectedPixelWidth")]
    [InlineData("EnlargementAuthorityProjectedPixelHeight")]
    public void A_partial_enlargement_authority_row_is_refused(string omitted)
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();

        (string Column, string Value)[] partial =
        [
            .. CompleteTargetEdgeColumns,
            .. CompleteAuthorityColumns.Where(c => c.Column != omitted),
        ];

        Should.Throw<SqliteException>(() => InsertSession(connection, "partial-authority", partial))
            .Message.ShouldContain("CHECK constraint failed");
    }

    /// <summary>
    /// The direction and the resampling policy are one decision, and the database says so (§22).
    /// </summary>
    /// <remarks>
    /// The accepted contract fixes exactly one policy per direction, so an enlargement claiming
    /// BicubicSharper or a shrink claiming PreserveDetails is a row that contradicts itself. It
    /// is refused here as well as by the mapper: a row that cannot exist needs no reader to guard
    /// against it.
    /// </remarks>
    [Theory]
    [InlineData("ENLARGE", "BICUBIC_SHARPER")]
    [InlineData("SHRINK", "PRESERVE_DETAILS")]
    [InlineData("RESOLUTION_ONLY", "BICUBIC_SHARPER")]
    [InlineData("SHRINK", "NONE")]
    public void A_direction_and_policy_that_disagree_are_refused(string direction, string policy)
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();

        (string Column, string Value)[] mismatched =
        [
            .. CompleteTargetEdgeColumns.Select(c => c.Column switch
            {
                "TargetPlanDirection" => (c.Column, $"'{direction}'"),
                "TargetPlanResizePolicy" => (c.Column, $"'{policy}'"),
                _ => c,
            }),
        ];

        Should.Throw<SqliteException>(() => InsertSession(connection, "mismatch", mismatched));
    }

    /// <summary>
    /// Permission to enlarge cannot be attached to a run that does not enlarge (§9, §22).
    /// </summary>
    /// <remarks>
    /// An authority beside a shrink is not a harmless leftover: it is a record saying a human
    /// agreed to add pixels to a job that removes them, and no reader could honestly interpret it.
    /// </remarks>
    [Fact]
    public void An_enlargement_authority_beside_a_shrink_is_refused()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();

        (string Column, string Value)[] shrinking =
        [
            .. CompleteTargetEdgeColumns.Select(c => c.Column switch
            {
                "TargetPlanDirection" => (c.Column, "'SHRINK'"),
                "TargetPlanResizePolicy" => (c.Column, "'BICUBIC_SHARPER'"),
                "TargetPlanSourcePixelWidth" => (c.Column, "4000"),
                _ => c,
            }),
            .. CompleteAuthorityColumns,
        ];

        Should.Throw<SqliteException>(() => InsertSession(connection, "shrink-authority", shrinking));
    }

    /// <summary>
    /// A requested edge that is not a positive size cannot be stored (§22).
    /// </summary>
    /// <remarks>
    /// The millimetres are TEXT so the operator's decimal survives exactly, and TEXT is the one
    /// column type that will happily hold "0", "-5" or "banana". The CAST here is a validity check
    /// on the stored text and never the arithmetic — the exact calculation reads the decimal
    /// itself, and a second numeric implementation is precisely what §7 forbids.
    /// </remarks>
    [Theory]
    [InlineData("'0'")]
    [InlineData("'-5'")]
    [InlineData("'banana'")]
    public void A_requested_edge_that_is_not_a_positive_size_is_refused(string stored)
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();

        (string Column, string Value)[] invalid =
        [
            .. CompleteTargetEdgeColumns.Select(c =>
                c.Column == "SizingRequestedMm" ? (c.Column, stored) : c),
        ];

        Should.Throw<SqliteException>(() => InsertSession(connection, "bad-mm", invalid));
    }

    /// <summary>A row cannot claim both accepted sizing contracts at once (§5).</summary>
    [Fact]
    public void A_row_holding_both_a_fit_box_and_a_target_edge_is_refused()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();

        (string Column, string Value)[] both =
        [
            .. CompletePlanColumns.Where(c => c.Column != "DimensionSemantics"),
            .. CompleteTargetEdgeColumns,
        ];

        Should.Throw<SqliteException>(() => InsertSession(connection, "both", both));
    }

    /// <summary>Every column migration 0006 adds, on both tables.</summary>
    // -------------------------------------------------------------------------------------
    // 0007 — the approved-TIFF promotion columns (Epic 11400 Final Gate §25)
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A database written before 0007 upgrades to the newest schema and keeps every session,
    /// revision, attempt and output it already held (Final Gate §25).
    /// </summary>
    /// <remarks>
    /// The pre-0006 case above proves the twelve-step table rebuild does not lose rows. This one
    /// covers the step after it, and covers the table the earlier cases never populated: 0007 is
    /// the first migration to touch <c>PrintOutput</c>, and a production database reaching it will
    /// already hold approved outputs from before the promotion columns existed. Those rows must
    /// arrive with no promotion in flight rather than with an empty string or a default path.
    /// </remarks>
    [Fact]
    public void A_pre_0007_database_upgrades_and_keeps_its_outputs()
    {
        using TempDatabase database = new(migrate: false);

        using (SqliteConnection seeded = database.OpenRaw())
        {
            foreach (string script in new[]
                     {
                         "0001_initial_schema.sql", "0002_trim_parameters.sql",
                         "0003_background_removal_decision.sql", "0004_attempt_adapter_notes.sql",
                         "0005_maximum_bound_print_plan.sql",
                         "0006_flexible_size_and_enlargement_authority.sql",
                     })
            {
                Execute(seeded, ReadMigrationScript(script));
            }

            Execute(
                seeded,
                "INSERT INTO SchemaMigration (Version, Name, AppliedAtUtc, ScriptSha256) " +
                "VALUES (1, 'initial_schema', '2026-01-01T00:00:00.000Z', 'SEED'), " +
                "       (2, 'trim_parameters', '2026-01-01T00:00:00.000Z', 'SEED'), " +
                "       (3, 'background_removal_decision', '2026-01-01T00:00:00.000Z', 'SEED'), " +
                "       (4, 'attempt_adapter_notes', '2026-01-01T00:00:00.000Z', 'SEED'), " +
                "       (5, 'maximum_bound_print_plan', '2026-01-01T00:00:00.000Z', 'SEED'), " +
                "       (6, 'flexible_size_and_enlargement_authority', '2026-01-01T00:00:00.000Z', 'SEED');");
            Execute(seeded, "PRAGMA user_version = 6;");

            SeedApprovedOutput(seeded, "legacy-output-session");
        }

        using SqliteConnection upgraded = database.OpenRaw();
        MigrationRunner.Migrate(upgraded).IsSuccess.ShouldBeTrue();
        ReadUserVersion(upgraded).ShouldBe(MigrationRunner.NewestKnownVersion);

        ScalarOf(upgraded, "SELECT COUNT(*) FROM ProcessingSession;").ShouldBe(1L);
        ScalarOf(upgraded, "SELECT COUNT(*) FROM Revision;").ShouldBe(2L);
        ScalarOf(upgraded, "SELECT COUNT(*) FROM ProcessingAttempt;").ShouldBe(1L);
        ScalarOf(upgraded, "SELECT COUNT(*) FROM PrintOutput;").ShouldBe(1L);

        ColumnsOf(upgraded, "PrintOutput").ShouldContain("PromotionReservedPath");

        // The pre-existing output arrives with no promotion in flight, and everything it already
        // recorded is untouched.
        using SqliteCommand read = upgraded.CreateCommand();
        read.CommandText =
            "SELECT RelativePath, Sha256, ByteLength, ReviewState, PromotionReservedPath " +
            "FROM PrintOutput WHERE Id = '33333333-3333-3333-3333-333333333333';";
        using SqliteDataReader reader = read.ExecuteReader();
        reader.Read().ShouldBeTrue();
        reader.GetString(0).ShouldBe("Sessions/legacy-output-session/Approved/legacy.tif");
        reader.GetString(1).ShouldBe(new string('b', 64));
        reader.GetInt64(2).ShouldBe(4096L);
        reader.GetString(3).ShouldBe("APPROVED");
        reader.IsDBNull(4).ShouldBeTrue("a historical output acquires no promotion reservation");
    }

    /// <summary>
    /// 0007's trigger lets an output's location move and refuses every other identity column
    /// (Final Gate §25; Part C2B §4, §8).
    /// </summary>
    /// <remarks>
    /// The file-location model stated as a database rule rather than a convention. A Revision's
    /// path may never change (<c>Revision_Immutable_Update</c>, asserted in <c>DbInvariantTests</c>);
    /// a PrintOutput's may, because approval promotes the deliverable — but its hash, byte length,
    /// source Revision and creation instant may not, because approval copies bytes that were
    /// already validated and never produces different ones.
    /// </remarks>
    [Fact]
    public void A_PrintOutputs_location_may_move_but_its_identity_columns_may_not()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();

        SeedApprovedOutput(connection, "promotion-session");
        const string id = "'33333333-3333-3333-3333-333333333333'";

        // The move approval performs, and the reservation that precedes it, both round-trip.
        Execute(connection,
            $"UPDATE PrintOutput SET PromotionReservedPath = 'Sessions/promotion-session/Approved/moved.tif' WHERE Id = {id};");
        ScalarStringOf(connection, $"SELECT PromotionReservedPath FROM PrintOutput WHERE Id = {id};")
            .ShouldBe("Sessions/promotion-session/Approved/moved.tif");

        Execute(connection,
            $"UPDATE PrintOutput SET RelativePath = 'Sessions/promotion-session/Approved/moved.tif', " +
            $"PromotionReservedPath = NULL WHERE Id = {id};");
        ScalarStringOf(connection, $"SELECT RelativePath FROM PrintOutput WHERE Id = {id};")
            .ShouldBe("Sessions/promotion-session/Approved/moved.tif");
        ScalarOf(connection, $"SELECT COUNT(*) FROM PrintOutput WHERE Id = {id} AND PromotionReservedPath IS NULL;")
            .ShouldBe(1L);

        // And every column that says *which file this is* is refused.
        foreach (string forbidden in new[]
                 {
                     $"UPDATE PrintOutput SET Sha256 = '{new string('c', 64)}' WHERE Id = {id};",
                     $"UPDATE PrintOutput SET ByteLength = 1 WHERE Id = {id};",
                     $"UPDATE PrintOutput SET SourceRevisionId = '55555555-5555-5555-5555-555555555555' WHERE Id = {id};",
                     $"UPDATE PrintOutput SET CreatedAtUtc = '2027-01-01T00:00:00.000Z' WHERE Id = {id};",
                 })
        {
            Should.Throw<SqliteException>(() => Execute(connection, forbidden))
                .Message.ShouldContain("PrintOutput identity columns are immutable");
        }

        // Review state and validity still move, because those are what a review changes.
        Execute(connection, $"UPDATE PrintOutput SET ReviewState = 'REJECTED', IsValid = 0 WHERE Id = {id};");
        ScalarStringOf(connection, $"SELECT ReviewState FROM PrintOutput WHERE Id = {id};").ShouldBe("REJECTED");
    }

    /// <summary>Seeds a session, its root Revision, one attempt and one approved PrintOutput.</summary>
    private static void SeedApprovedOutput(SqliteConnection connection, string sessionId)
    {
        InsertSession(connection, sessionId, []);

        Execute(
            connection,
            $"""
             INSERT INTO Revision
                 (Id, SessionId, SourceRevisionId, Operation, RelativePath, Format, ByteLength,
                  Sha256, ColourMode, CreatedAtUtc)
             VALUES
                 ('22222222-2222-2222-2222-222222222222', '{sessionId}', NULL, 'IMPORT',
                  'Sessions/{sessionId}/Source/design.png', 'PNG', 2048, '{new string('a', 64)}',
                  'RGB', '2026-01-01T00:00:00.000Z'),
                 ('55555555-5555-5555-5555-555555555555', '{sessionId}', NULL, 'IMPORT',
                  'Sessions/{sessionId}/Source/other.png', 'PNG', 1024, '{new string('e', 64)}',
                  'RGB', '2026-01-01T00:00:00.000Z');

             INSERT INTO ProcessingAttempt
                 (Id, SessionId, StepKind, InputRevisionId, OutputRevisionId, Operation, AdapterId,
                  ResultStatus, RetrySequence, StartedAtUtc)
             VALUES
                 ('44444444-4444-4444-4444-444444444444', '{sessionId}', 'PhotoshopOutput',
                  '22222222-2222-2222-2222-222222222222', NULL, 'PHOTOSHOP_OUTPUT',
                  'fake-photoshop-v1', 'FAILED', 0, '2026-01-01T00:00:00.000Z');

             INSERT INTO PrintOutput
                 (Id, SessionId, SourceRevisionId, TargetWidthMm, TargetHeightMm, PixelWidth,
                  PixelHeight, Dpi, SizePresetId, WhiteUnderbaseBranch, ProductionPresetId,
                  ProductionPresetSha256, RelativePath, ByteLength, Sha256, ReviewState, IsValid,
                  CreatedAtUtc)
             VALUES
                 ('33333333-3333-3333-3333-333333333333', '{sessionId}',
                  '22222222-2222-2222-2222-222222222222', 200.0, 150.0, 2362, 1772, 300, 'CUSTOM',
                  'W1_1PX', 'printflow-workstation-v1', '{new string('d', 64)}',
                  'Sessions/{sessionId}/Approved/legacy.tif', 4096, '{new string('b', 64)}',
                  'APPROVED', 1, '2026-01-01T00:00:00.000Z');
             """);
    }

    private static string ScalarStringOf(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()!.ToString()!;
    }

    private static readonly string[] FlexibleColumns =
    [
        "SizingMode", "SizingPreset", "SizingRecommendationKind",
        "SizingRecommendationMaxWidthMm", "SizingRecommendationMaxHeightMm",
        "SizingPresetOverridden", "SizingTargetEdge", "SizingRequestedMm",
        "TargetPlanSourceRevisionId", "TargetPlanSourceSha256",
        "TargetPlanSourcePixelWidth", "TargetPlanSourcePixelHeight",
        "TargetPlanPhotoshopEdge", "TargetPlanProjectedPixelWidth",
        "TargetPlanProjectedPixelHeight", "TargetPlanScaleNumerator",
        "TargetPlanScaleDenominator", "TargetPlanProductionDpi", "TargetPlanDirection",
        "TargetPlanResizePolicy",
        "EnlargementAuthoritySourceRevisionId", "EnlargementAuthoritySourceSha256",
        "EnlargementAuthoritySizingMode", "EnlargementAuthorityTargetEdge",
        "EnlargementAuthorityRequestedMm", "EnlargementAuthorityScaleNumerator",
        "EnlargementAuthorityScaleDenominator", "EnlargementAuthorityProjectedPixelWidth",
        "EnlargementAuthorityProjectedPixelHeight",
    ];

    /// <summary>
    /// One complete, self-consistent TargetEdgeV1 decision: a 2000×1500 px source, A5's configured
    /// 135 mm long edge overridden to a 200.025 mm width.
    /// </summary>
    /// <remarks>
    /// 200.025 mm is an exact midpoint of the accepted conversion — 200.025 × 1500 / 127 is
    /// 2362.5 px precisely — so it rounds away from zero to 2363 and is the value a binary double
    /// would get wrong. It is here on purpose: this row is what the exact-decimal round trip is
    /// asserted against (§7).
    /// </remarks>
    private static readonly (string Column, string Value)[] CompleteTargetEdgeColumns =
    [
        ("DimensionSemantics", "'TARGET_EDGE_V1'"),
        ("SizingMode", "'CUSTOM_TARGET_EDGE'"),
        ("SizingPreset", "'A5'"),
        ("SizingRecommendationKind", "'MAXIMUM_LONG_EDGE'"),
        ("SizingRecommendationMaxWidthMm", "'135'"),
        ("SizingRecommendationMaxHeightMm", "'135'"),
        ("SizingPresetOverridden", "1"),
        ("SizingTargetEdge", "'LONG_EDGE'"),
        ("SizingRequestedMm", "'200.025'"),
        ("TargetPlanSourceRevisionId", "'22222222-2222-2222-2222-222222222222'"),
        ("TargetPlanSourceSha256", $"'{new string('b', 64)}'"),
        ("TargetPlanSourcePixelWidth", "2000"),
        ("TargetPlanSourcePixelHeight", "1500"),
        ("TargetPlanPhotoshopEdge", "'WIDTH'"),
        ("TargetPlanProjectedPixelWidth", "2363"),
        ("TargetPlanProjectedPixelHeight", "1772"),
        ("TargetPlanScaleNumerator", "2363"),
        ("TargetPlanScaleDenominator", "2000"),
        ("TargetPlanProductionDpi", "300"),
        ("TargetPlanDirection", "'ENLARGE'"),
        ("TargetPlanResizePolicy", "'PRESERVE_DETAILS'"),
    ];

    /// <summary>The exact authority that covers <see cref="CompleteTargetEdgeColumns"/>.</summary>
    private static readonly (string Column, string Value)[] CompleteAuthorityColumns =
    [
        ("EnlargementAuthoritySourceRevisionId", "'22222222-2222-2222-2222-222222222222'"),
        ("EnlargementAuthoritySourceSha256", $"'{new string('b', 64)}'"),
        ("EnlargementAuthoritySizingMode", "'CUSTOM_TARGET_EDGE'"),
        ("EnlargementAuthorityTargetEdge", "'LONG_EDGE'"),
        ("EnlargementAuthorityRequestedMm", "'200.025'"),
        ("EnlargementAuthorityScaleNumerator", "2363"),
        ("EnlargementAuthorityScaleDenominator", "2000"),
        ("EnlargementAuthorityProjectedPixelWidth", "2363"),
        ("EnlargementAuthorityProjectedPixelHeight", "1772"),
    ];

    private static long ScalarOf(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    /// <summary>
    /// One complete, self-consistent plan: 2000×1000 px fitted to a 50×50 mm box, so width is the
    /// limiting edge and 50 mm is 591 px at 300 ppi.
    /// </summary>
    private static readonly (string Column, string Value)[] CompletePlanColumns =
    [
        ("DimensionSemantics", "'MAX_BOUNDS_V1'"),
        ("PrintPlanSourceRevisionId", "'11111111-1111-1111-1111-111111111111'"),
        ("PrintPlanSourceSha256", $"'{new string('a', 64)}'"),
        ("PrintPlanSourcePixelWidth", "2000"),
        ("PrintPlanSourcePixelHeight", "1000"),
        ("PrintPlanMaxWidthMm", "50.0"),
        ("PrintPlanMaxHeightMm", "50.0"),
        ("PrintPlanLimitKind", "'CUSTOM'"),
        ("PrintPlanMode", "'PROPORTIONAL_SHRINK'"),
        ("PrintPlanLimitingEdge", "'WIDTH'"),
        ("PrintPlanLimitingValueMm", "50.0"),
        ("PrintPlanProjectedPixelWidth", "591"),
        ("PrintPlanProjectedPixelHeight", "295"),
        ("PrintPlanProductionDpi", "300"),
        ("PrintPlanResizePolicy", "'BICUBIC_SHARPER'"),
    ];

    /// <summary>Inserts a minimal session row plus the supplied extra columns, verbatim.</summary>
    private static void InsertSession(
        SqliteConnection connection, string id, IReadOnlyList<(string Column, string Value)> extra)
    {
        string columns = string.Concat(extra.Select(c => ", " + c.Column));
        string values = string.Concat(extra.Select(c => ", " + c.Value));

        Execute(
            connection,
            $"""
             INSERT INTO ProcessingSession
                 (Id, WorkflowType, OutputName, CurrentStep, State, WorkspacePath,
                  CreatedAtUtc, UpdatedAtUtc{columns})
             VALUES
                 ('{id}', 'GENERATE_PRINT_TIFF', '{id}', 'PrintDimensions', 'ACTIVE',
                  'Sessions/{id}', '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.000Z'{values});
             """);
    }

    /// <summary>
    /// Reads a migration back off the shipped assembly, so a test's "before" state is the script
    /// that actually shipped rather than a copy that can quietly fall behind it.
    /// </summary>
    private static string ReadMigrationScript(string fileName)
    {
        using Stream stream = typeof(MigrationRunner).Assembly.GetManifestResourceStream(
                "PrintFlow.Infrastructure.Sqlite.Migrations." + fileName)
            ?? throw new InvalidOperationException($"Migration resource '{fileName}' is not embedded.");

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long ReadUserVersion(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static IReadOnlyList<string> ColumnsOf(SqliteConnection connection, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";

        List<string> names = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }

    private static void AssertPragma(SqliteConnection connection, string pragma, string expected)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        string actual = command.ExecuteScalar()!.ToString()!;
        actual.ShouldBe(expected, StringCompareShould.IgnoreCase);
    }
}
