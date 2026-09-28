using Dapper;
using Microsoft.Data.Sqlite;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;
using static PrintFlow.Tests.Fixtures.CorrectionFixtures;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// SCRUM-11148 migration 0019 against fresh, test-owned temporary databases: the additive table,
/// its constraints and triggers, the aggregate round trip and every conditional change, including
/// the whole-transaction rollback of a failed <see cref="CorrectionRequestChange.AssertBound"/>.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class CorrectionRequestPersistenceTests
{
    [Fact]
    public void The_migration_adds_only_the_correction_request_table_and_its_own_objects()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.OpenRaw();

        var objects = connection.Query<(string Type, string Name, string Table)>(
            "SELECT type, name, tbl_name FROM sqlite_master WHERE tbl_name = 'CorrectionRequest' ORDER BY type, name;").ToList();

        objects.ShouldContain(("table", "CorrectionRequest", "CorrectionRequest"));
        objects.ShouldContain(("index", "UX_CorrectionRequest_OneOpenPerSession", "CorrectionRequest"));
        objects.ShouldContain(("trigger", "CorrectionRequest_Identity_Immutable", "CorrectionRequest"));
        objects.ShouldContain(("trigger", "CorrectionRequest_ForwardOnly", "CorrectionRequest"));
        connection.ExecuteScalar<long>("SELECT COUNT(*) FROM CorrectionRequest;").ShouldBe(0, "no legacy handoff is backfilled");
        connection.Query<string>("PRAGMA foreign_key_list('CorrectionRequest');").ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_request_round_trips_and_moves_forward_through_conditional_changes_only()
    {
        using SessionServiceHarness h = new();
        (SessionAggregate aggregate, CorrectionRequest row, ProcessingAttempt attempt) = await SeedAsync(h);

        (await CommitAsync(h, aggregate, new CorrectionRequestChange.Insert(row))).IsSuccess.ShouldBeTrue();
        (await LoadAsync(h, aggregate.Session.Id)).CorrectionRequests.Single().ShouldBe(row with
        {
            CreatedAtUtc = Truncate(row.CreatedAtUtc),
        });

        // Out of order is refused: an attempt cannot be set before READY.
        (await CommitAsync(h, aggregate, new CorrectionRequestChange.SetLastImportAttempt(row.Id, attempt.Id))).IsFailure.ShouldBeTrue();
        (await CommitAsync(h, aggregate, new CorrectionRequestChange.MarkReady(row.Id, h.Clock.GetUtcNow()))).IsSuccess.ShouldBeTrue();
        (await CommitAsync(h, aggregate, new CorrectionRequestChange.MarkReady(row.Id, h.Clock.GetUtcNow()))).IsFailure.ShouldBeTrue("only once");
        (await CommitAsync(h, aggregate, new CorrectionRequestChange.AssertBound(row.Id, attempt.Id))).IsFailure.ShouldBeTrue("not bound yet");
        (await CommitAsync(h, aggregate, new CorrectionRequestChange.SetLastImportAttempt(row.Id, attempt.Id))).IsSuccess.ShouldBeTrue();
        (await CommitAsync(h, aggregate, new CorrectionRequestChange.AssertBound(row.Id, attempt.Id))).IsSuccess.ShouldBeTrue();

        CorrectionRequest bound = (await LoadAsync(h, aggregate.Session.Id)).CorrectionRequests.Single();
        (bound.Status, bound.LastImportAttemptId).ShouldBe((CorrectionRequestStatus.Ready, attempt.Id));
        (await CommitAsync(h, aggregate, new CorrectionRequestChange.MarkReturned(
            row.Id, AttemptId.From(Guid.NewGuid()), row.HandedOutRevisionId, h.Clock.GetUtcNow()))).IsFailure.ShouldBeTrue("another attempt");
        (await CommitAsync(h, aggregate, new CorrectionRequestChange.MarkReturned(
            row.Id, attempt.Id, row.HandedOutRevisionId, h.Clock.GetUtcNow()))).IsSuccess.ShouldBeTrue();
        (await CommitAsync(h, aggregate, new CorrectionRequestChange.Supersede(row.Id, h.Clock.GetUtcNow()))).IsFailure.ShouldBeTrue("a returned request is closed");
        (await LoadAsync(h, aggregate.Session.Id)).CorrectionRequests.Single().Status.ShouldBe(CorrectionRequestStatus.Returned);
    }

    [Fact]
    public async Task A_failed_assertion_rolls_back_the_whole_closing_transaction()
    {
        using SessionServiceHarness h = new();
        (SessionAggregate aggregate, CorrectionRequest row, ProcessingAttempt attempt) = await SeedAsync(h);
        await CommitAsync(h, aggregate, new CorrectionRequestChange.Insert(row));
        await CommitAsync(h, aggregate, new CorrectionRequestChange.MarkReady(row.Id, h.Clock.GetUtcNow()));
        await CommitAsync(h, aggregate, new CorrectionRequestChange.SetLastImportAttempt(row.Id, attempt.Id));
        SessionAggregate before = await LoadAsync(h, aggregate.Session.Id);

        // A close of a different attempt, handing the session off, asserting the wrong binding.
        ProcessingAttempt other = attempt with { Id = AttemptId.From(Guid.NewGuid()) };
        SessionMutation closing = SessionMutation.Empty(before.Session with
        {
            State = SessionState.HandedOff, HandOffReason = "must not land", HandedOffAtUtc = h.Clock.GetUtcNow(),
        }) with
        {
            UpsertAttempts = [attempt.Interrupt(h.Clock.GetUtcNow())],
            CorrectionRequestChanges = [new CorrectionRequestChange.AssertBound(row.Id, other.Id)],
        };

        (await h.Repository.CommitAsync(closing, default)).IsFailure.ShouldBeTrue();

        SessionAggregate after = await LoadAsync(h, aggregate.Session.Id);
        after.Session.ShouldBe(before.Session);
        after.Attempts.Single(a => a.Id == attempt.Id).Status.ShouldBe(AttemptStatus.Running);
        after.CorrectionRequests.ShouldBe(before.CorrectionRequests);
    }

    [Fact]
    public async Task Identity_is_immutable_one_open_request_per_session_and_closed_rows_do_not_reopen()
    {
        using SessionServiceHarness h = new();
        (SessionAggregate aggregate, CorrectionRequest row, _) = await SeedAsync(h);
        await CommitAsync(h, aggregate, new CorrectionRequestChange.Insert(row));
        using SqliteConnection connection = h.Database.OpenRaw();
        string id = row.Id.ToString("D");

        foreach (string update in new[]
        {
            "UPDATE CorrectionRequest SET HandedOutSha256 = @other WHERE Id = @id;",
            "UPDATE CorrectionRequest SET ReferenceRevisionId = HandedOutRevisionId WHERE Id = @id;",
            "UPDATE CorrectionRequest SET FolderRelativePath = 'elsewhere' WHERE Id = @id;",
            "UPDATE CorrectionRequest SET EffectiveReason = 'rewritten' WHERE Id = @id;",
            "UPDATE CorrectionRequest SET Note = 'rewritten' WHERE Id = @id;",
        })
        {
            Should.Throw<SqliteException>(() => connection.Execute(update, new { id, other = new string('a', 64) }));
        }

        CorrectionRequest second = row with { Id = Guid.NewGuid(), Folder = WorkspaceDirRef.Create(row.Folder.RelativePath + "-2") };
        (await CommitAsync(h, aggregate, new CorrectionRequestChange.Insert(second))).IsFailure.ShouldBeTrue("one open request per session");
        (await CommitAsync(h, aggregate,
            new CorrectionRequestChange.Supersede(row.Id, h.Clock.GetUtcNow()),
            new CorrectionRequestChange.Insert(second))).IsSuccess.ShouldBeTrue("a supersede and its replacement land together");

        Should.Throw<SqliteException>(() => connection.Execute(
            "UPDATE CorrectionRequest SET Status = 'READY', ReadyAtUtc = '2026-01-01T00:00:00.000Z', ClosedAtUtc = NULL WHERE Id = @id;", new { id }));
        (await LoadAsync(h, aggregate.Session.Id)).CorrectionRequests.Select(r => r.Status)
            .ShouldBe([CorrectionRequestStatus.Superseded, CorrectionRequestStatus.Preparing], ignoreOrder: true);
    }

    [Fact]
    public async Task Retention_maintenance_cannot_carry_request_changes()
    {
        using SessionServiceHarness h = new();
        (SessionAggregate aggregate, CorrectionRequest row, _) = await SeedAsync(h);
        (await h.Repository.CommitAsync(SessionMutation.Empty(aggregate.Session) with
        {
            IsRetentionMaintenance = true,
            CorrectionRequestChanges = [new CorrectionRequestChange.Insert(row)],
        }, default)).IsFailure.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------------------

    private static async Task<(SessionAggregate Aggregate, CorrectionRequest Row, ProcessingAttempt Attempt)> SeedAsync(SessionServiceHarness h)
    {
        SessionView review = await AtBackgroundRemovalReviewAsync(h, Service(h));
        SessionAggregate aggregate = await LoadAsync(h, review.Id);
        Revision r = aggregate.Revisions.Single(x => x.Id == review.CurrentArtefact!.RevisionId);
        Revision u = aggregate.Revisions.Single(x => x.Id == r.SourceRevisionId);
        CorrectionRequest row = new(
            Guid.NewGuid(), aggregate.Session.Id, StepKind.BackgroundRemoval, r.Id, r.Sha256, u.Id, u.Sha256,
            WorkspaceDirRef.Create(aggregate.Session.Workspace.RelativePath + "/Correction/logo-seeded"),
            "logo - REFERENCE (do not edit).png", "logo - CORRECT THIS.png", "logo - CORRECTED.png",
            null, "Operator asked a colleague to correct the background-removal result.",
            CorrectionRequestStatus.Preparing, h.Clock.GetUtcNow(), null, null, null, null);

        // A real Running import attempt row, so the binding columns reference an existing attempt.
        ProcessingAttempt attempt = ProcessingAttempt.Start(AttemptId.From(Guid.NewGuid()), aggregate.Session.Id,
            StepKind.BackgroundRemoval, u.Id, OperationKind.ManualResultImport, "manual-result-import-v1", h.Clock.GetUtcNow());
        (await h.Repository.CommitAsync(SessionMutation.Empty(aggregate.Session) with { UpsertAttempts = [attempt] }, default))
            .IsSuccess.ShouldBeTrue();
        return (await LoadAsync(h, review.Id), row, attempt);
    }

    private static Task<PrintFlow.Domain.Results.OperationResult<PrintFlow.Domain.Results.Unit>> CommitAsync(
        SessionServiceHarness h, SessionAggregate aggregate, params CorrectionRequestChange[] changes) =>
        h.Repository.CommitAsync(SessionMutation.Empty(aggregate.Session) with { CorrectionRequestChanges = changes }, default);

    private static DateTimeOffset Truncate(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
}
