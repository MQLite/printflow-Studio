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
