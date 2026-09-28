using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Delivery;

namespace PrintFlow.Infrastructure.Sqlite;

/// <summary>Short, transaction-bound journal writes; no transaction crosses file I/O.</summary>
public sealed class SqliteDeliveryRepository(SqliteConnectionFactory factory) : IDeliveryRepository
{
    private const string PreferenceKey = "LAST_SUCCESSFUL_DELIVERY_DESTINATION";

    public Task<OperationResult<IReadOnlyList<DeliveryJournalEntry>>> ListAsync(
        SessionId sessionId, ArtifactKey? artifact, CancellationToken cancellationToken) => Run(() =>
    {
        using SqliteConnection connection = factory.Open();
        using SqliteCommand command = Command(connection, null,
            "SELECT DeliveryId FROM ArtifactDelivery WHERE SessionId=$session " +
            (artifact is null ? "" : "AND Kind=$kind AND " +
                (artifact.Value.Kind == ArtifactKind.ApprovedAssetPng ? "RevisionId" : "PrintOutputId") + "=$artifact ") +
            "ORDER BY IntentOrdinal DESC;");
        Add(command, "$session", sessionId.ToString());
        if (artifact is { } key)
        {
            Add(command, "$kind", key.Kind.ToString());
            Add(command, "$artifact", key.ArtifactId.ToString("D"));
        }
        List<Guid> ids = [];
        using (SqliteDataReader reader = command.ExecuteReader())
            while (reader.Read()) ids.Add(Guid.Parse(reader.GetString(0)));
        List<DeliveryJournalEntry> entries = [];
        foreach (Guid id in ids)
            if (ReadEntry(connection, null, id) is { } entry) entries.Add(entry);
        return OperationResult.Ok<IReadOnlyList<DeliveryJournalEntry>>(entries);
    });

    public Task<OperationResult<DeliveryJournalEntry?>> FindByIdAsync(Guid deliveryId, CancellationToken cancellationToken) =>
        Run(() => { using SqliteConnection c = factory.Open(); return OperationResult.Ok(ReadEntry(c, null, deliveryId)); });

    public Task<OperationResult<DeliveryJournalEntry?>> FindByRequestAsync(Guid requestId, CancellationToken cancellationToken) => Run(() =>
    {
        using SqliteConnection c = factory.Open();
        DeliveryJournalEntry? direct = Find(c, null, "RequestId", requestId.ToString("D"));
        if (direct is not null) return OperationResult.Ok<DeliveryJournalEntry?>(direct);
        using SqliteCommand alias = Command(c, null,
            "SELECT DeliveryId,RequestedFolder,RequestedFileName FROM DeliveryRequestAlias WHERE RequestId=$request;");
        Add(alias, "$request", requestId);
        using SqliteDataReader reader = alias.ExecuteReader();
        if (!reader.Read()) return OperationResult.Ok<DeliveryJournalEntry?>(null);
        Guid id = Guid.Parse(reader.GetString(0));
        DeliveryRequestBinding binding = new(reader.GetString(1), reader.GetString(2));
        reader.Close();
        return OperationResult.Ok<DeliveryJournalEntry?>(ReadEntry(c, null, id)! with { RequestBinding = binding });
    });

    public Task<OperationResult<DeliveryJournalEntry?>> FindSuccessorAsync(Guid priorDeliveryId, CancellationToken cancellationToken) =>
        FindAsync("ReplacementOfDeliveryId", priorDeliveryId.ToString("D"));

    public Task<OperationResult<DeliveryJournalEntry?>> FindByDestinationAsync(string destinationKey, CancellationToken cancellationToken) =>
        FindAsync("DestinationKey", destinationKey);

    private Task<OperationResult<DeliveryJournalEntry?>> FindAsync(string column, string value) => Run(() =>
    {
        using SqliteConnection c = factory.Open();
        return OperationResult.Ok(Find(c, null, column, value));
    });

    public Task<OperationResult<DeliveryJournalEntry>> CreateOrCoalesceAsync(
        DeliveryRecord proposed, DeliveryAttemptRecord attempt, CancellationToken cancellationToken) => Run(() =>
    {
        using SqliteConnection c = factory.Open();
        using SqliteTransaction tx = c.BeginTransaction();
        DeliveryJournalEntry? sameRequest = Find(c, tx, "RequestId", proposed.RequestId.ToString("D"));
        if (sameRequest is not null)
        {
            if (!SameRequest(sameRequest.Delivery, proposed))
                return OperationResult.Fail<DeliveryJournalEntry>(FailureCode.PreconditionNotMet,
                    "RequestConflict: this RequestId is bound to different artifact or destination evidence.");
            tx.Commit();
            return OperationResult.Ok(sameRequest);
        }
        using (SqliteCommand alias = Command(c, tx,
            "SELECT DeliveryId, RequestFingerprint FROM DeliveryRequestAlias WHERE RequestId=$request;"))
        {
            Add(alias, "$request", proposed.RequestId);
            using SqliteDataReader reader = alias.ExecuteReader();
            if (reader.Read())
            {
                Guid boundId = Guid.Parse(reader.GetString(0));
                string fingerprint = reader.GetString(1);
                reader.Close();
                if (fingerprint != Fingerprint(proposed))
                    return OperationResult.Fail<DeliveryJournalEntry>(FailureCode.PreconditionNotMet,
                        "RequestConflict: coalesced RequestId was previously bound to different intent.");
                DeliveryJournalEntry bound = ReadEntry(c, tx, boundId)!;
                tx.Commit();
                return OperationResult.Ok(bound);
            }
        }
        if (proposed.ReplacementOfDeliveryId is { } prior &&
            Find(c, tx, "ReplacementOfDeliveryId", prior.ToString("D")) is { } successor)
        {
            if (!SameCoalescedIntent(successor.Delivery, proposed))
                return OperationResult.Fail<DeliveryJournalEntry>(FailureCode.PreconditionNotMet,
                    "RequestConflict: successor is bound to different authority or destination.");
            BindAlias(c, tx, proposed, successor.Delivery.DeliveryId);
            tx.Commit();
            return OperationResult.Ok(successor);
        }
        if (!AuthorityRecorded(c, tx, proposed))
            return OperationResult.Fail<DeliveryJournalEntry>(FailureCode.PreconditionNotMet,
                "The exact approved review and artifact are no longer current in the delivery transaction.");
        DeliveryJournalEntry? sameDestination = Find(c, tx, "DestinationKey", proposed.DestinationKey);
        if (proposed.ReplacementOfDeliveryId is null && sameDestination is not null)
        {
            // Ordinary requests follow the newest explicit generation at this physical
            // destination. Its predecessor link is journal lineage, not a different copy intent.
            if (SameOrdinaryDestinationIntent(sameDestination.Delivery, proposed))
            {
                BindAlias(c, tx, proposed, sameDestination.Delivery.DeliveryId);
                tx.Commit();
                return OperationResult.Ok(sameDestination);
            }
            if (sameDestination.Attempt?.State is DeliveryAttemptState.Intent or DeliveryAttemptState.Staging
                or DeliveryAttemptState.ReadyToPublish or DeliveryAttemptState.NeedsReconciliation)
                return OperationResult.Fail<DeliveryJournalEntry>(FailureCode.PreconditionNotMet,
                    "Destination is already owned by another active delivery request.");
        }
        long ordinal = ScalarLong(c, tx, "SELECT COALESCE(MAX(IntentOrdinal),0)+1 FROM ArtifactDelivery;");
        proposed = proposed with { IntentOrdinal = ordinal };
        using (SqliteCommand command = Command(c, tx, """
            INSERT INTO ArtifactDelivery
              (DeliveryId,RequestId,IntentOrdinal,SessionId,Kind,RevisionId,PrintOutputId,ReviewId,
               ApprovalSubjectKind,ApprovalSubjectId,PromotionSourceRevisionId,ApprovedSha256,ApprovedLength,
               RequestedFolder,RequestedFileName,ResolvedFolder,FinalPath,VolumeId,DirectoryId,DestinationKey,
               ReplacementOfDeliveryId,Status,CreatedAtUtc)
            VALUES ($id,$request,$ordinal,$session,$kind,$revision,$output,$review,$subjectKind,$subjectId,
                    $promotionSource,$sha,$length,$requestedFolder,$leaf,$folder,$final,$volume,$directory,$key,
                    $prior,'Pending',$created);
            """))
        {
            Add(command, "$id", proposed.DeliveryId);
            Add(command, "$request", proposed.RequestId);
            Add(command, "$ordinal", ordinal);
            Add(command, "$session", proposed.Artifact.SessionId);
            Add(command, "$kind", proposed.Artifact.Kind.ToString());
            Add(command, "$revision", proposed.Artifact.Kind == ArtifactKind.ApprovedAssetPng ? proposed.Artifact.ArtifactId : null);
            Add(command, "$output", proposed.Artifact.Kind == ArtifactKind.ApprovedPrintTiff ? proposed.Artifact.ArtifactId : null);
            Add(command, "$review", proposed.ReviewId);
            Add(command, "$subjectKind", proposed.ApprovalSubjectKind);
            Add(command, "$subjectId", proposed.ApprovalSubjectId);
            Add(command, "$promotionSource", proposed.PromotionSourceRevisionId);
            Add(command, "$sha", proposed.ApprovedSha256.Value);
            Add(command, "$length", proposed.ApprovedLength);
            Add(command, "$requestedFolder", proposed.RequestedFolder);
            Add(command, "$leaf", proposed.RequestedFileName);
            Add(command, "$folder", proposed.ResolvedFolder);
            Add(command, "$final", proposed.FinalPath);
            Add(command, "$volume", proposed.VolumeId);
            Add(command, "$directory", proposed.DirectoryId);
            Add(command, "$key", proposed.DestinationKey);
            Add(command, "$prior", proposed.ReplacementOfDeliveryId);
            Add(command, "$created", Iso(proposed.CreatedAtUtc));
            command.ExecuteNonQuery();
        }
        InsertAttempt(c, tx, attempt);
        tx.Commit();
        return OperationResult.Ok(new DeliveryJournalEntry(proposed, attempt));
    });

    public Task<OperationResult<DeliveryAttemptRecord>> BeginNextAttemptAsync(
        Guid deliveryId, string directoryId, string destinationKey, CancellationToken cancellationToken) => Run(() =>
    {
        using SqliteConnection c = factory.Open();
        using SqliteTransaction tx = c.BeginTransaction();
        DeliveryJournalEntry? entry = ReadEntry(c, tx, deliveryId);
        if (entry is null || entry.Delivery.Status == "Delivered")
            return OperationResult.Fail<DeliveryAttemptRecord>(FailureCode.PreconditionNotMet, "Delivery cannot start another attempt.");
        if (entry.Attempt is { State: DeliveryAttemptState.Intent or DeliveryAttemptState.Staging
            or DeliveryAttemptState.ReadyToPublish or DeliveryAttemptState.NeedsReconciliation } active)
        {
            tx.Commit();
            return OperationResult.Ok(active);
        }
        Guid attemptId = Guid.NewGuid();
        DeliveryAttemptRecord next = new(attemptId, deliveryId, (entry.Attempt?.AttemptNumber ?? 0) + 1,
            destinationKey, DeliveryAttemptState.Intent, ".printflow-" + attemptId.ToString("N") + ".partial",
            directoryId, null, null, entry.Delivery.ApprovedSha256, entry.Delivery.ApprovedLength,
            null, null, DateTimeOffset.UtcNow, null);
        InsertAttempt(c, tx, next);
        tx.Commit();
        return OperationResult.Ok(next);
    });

    public Task<OperationResult<DeliveryAttemptRecord>> MarkStagingAsync(
        Guid attemptId, string stagedFileId, DateTimeOffset stagedCreationUtc, CancellationToken cancellationToken) =>
        Transition(attemptId, "UPDATE DeliveryAttempt SET State='Staging', StagingFileId=$file, StagingCreationUtc=$at " +
            "WHERE AttemptId=$id AND State='Intent';", stagedFileId, stagedCreationUtc);

    public Task<OperationResult<DeliveryAttemptRecord>> MarkReadyAsync(
        Guid attemptId, DateTimeOffset verifiedAtUtc, CancellationToken cancellationToken) =>
        Transition(attemptId, "UPDATE DeliveryAttempt SET State='ReadyToPublish', StageVerifiedAtUtc=$at " +
            "WHERE AttemptId=$id AND State='Staging';", null, verifiedAtUtc);

    public Task<OperationResult<DeliveryAttemptRecord>> EndAttemptAsync(
        Guid attemptId, DeliveryAttemptState state, string? failureCode, CancellationToken cancellationToken) => Run(() =>
    {
        if (state is not (DeliveryAttemptState.Failed or DeliveryAttemptState.Cancelled or DeliveryAttemptState.NeedsReconciliation))
            return OperationResult.Fail<DeliveryAttemptRecord>(FailureCode.PreconditionNotMet, "Invalid terminal attempt state.");
        using SqliteConnection c = factory.Open();
        using SqliteTransaction tx = c.BeginTransaction();
        using SqliteCommand command = Command(c, tx,
            "UPDATE DeliveryAttempt SET State=$state, FailureCode=$failure, FinishedAtUtc=$at " +
            "WHERE AttemptId=$id AND State IN ('Intent','Staging','ReadyToPublish','NeedsReconciliation');");
        Add(command, "$id", attemptId); Add(command, "$state", state.ToString());
        Add(command, "$failure", failureCode); Add(command, "$at", Iso(DateTimeOffset.UtcNow));
        if (command.ExecuteNonQuery() != 1)
            return OperationResult.Fail<DeliveryAttemptRecord>(FailureCode.PreconditionNotMet, "Attempt state changed.");
        DeliveryAttemptRecord result = ReadAttempt(c, tx, attemptId)!;
        tx.Commit();
        return OperationResult.Ok(result);
    });

    public Task<OperationResult<DeliveryJournalEntry>> MarkDeliveredAsync(
        Guid deliveryId, Guid attemptId, string finalFileId, DateTimeOffset finalCreationUtc,
        DateTimeOffset verifiedAtUtc, CancellationToken cancellationToken) => Run(() =>
    {
        using SqliteConnection c = factory.Open();
        using SqliteTransaction tx = c.BeginTransaction();
        DeliveryJournalEntry? current = ReadEntry(c, tx, deliveryId);
        if (current is null || current.Attempt?.AttemptId != attemptId ||
            current.Attempt.State is not (DeliveryAttemptState.ReadyToPublish or DeliveryAttemptState.NeedsReconciliation) ||
            current.Attempt.StagingFileId != finalFileId ||
            current.Attempt.StagingCreationUtc is null || current.Attempt.StageVerifiedAtUtc is null)
            return OperationResult.Fail<DeliveryJournalEntry>(FailureCode.PreconditionNotMet,
                "ReadyToPublish ownership checkpoint is absent or changed.");
        if (!AuthorityRecorded(c, tx, current.Delivery))
            return OperationResult.Fail<DeliveryJournalEntry>(FailureCode.PreconditionNotMet,
                "The exact approved review and artifact changed before the Delivered commit.");
        using (SqliteCommand attempt = Command(c, tx,
            "UPDATE DeliveryAttempt SET State='Delivered', FinishedAtUtc=$at " +
            "WHERE AttemptId=$id AND State IN ('ReadyToPublish','NeedsReconciliation');"))
        {
            Add(attempt, "$id", attemptId); Add(attempt, "$at", Iso(verifiedAtUtc));
            if (attempt.ExecuteNonQuery() != 1)
                return OperationResult.Fail<DeliveryJournalEntry>(FailureCode.PreconditionNotMet, "Attempt changed before delivery commit.");
        }
        using (SqliteCommand delivery = Command(c, tx, """
            UPDATE ArtifactDelivery SET Status='Delivered', VerifiedAtUtc=$at,
              VerifiedSha256=ApprovedSha256, VerifiedLength=ApprovedLength,
              FinalFileId=$file, FinalCreationUtc=$created, WinningAttemptId=$attempt
            WHERE DeliveryId=$id AND Status='Pending';
            """))
        {
            Add(delivery, "$id", deliveryId); Add(delivery, "$at", Iso(verifiedAtUtc));
            Add(delivery, "$file", finalFileId); Add(delivery, "$created", Iso(finalCreationUtc));
            Add(delivery, "$attempt", attemptId);
            if (delivery.ExecuteNonQuery() != 1)
                return OperationResult.Fail<DeliveryJournalEntry>(FailureCode.PreconditionNotMet, "Delivery changed before commit.");
        }
        // Preference and Delivered are one transaction. A late reconciliation of an older
        // intent cannot displace a newer successful destination.
        long rememberedOrdinal = 0;
        using (SqliteCommand read = Command(c, tx, "SELECT Value FROM Setting WHERE Key=$key;"))
        {
            Add(read, "$key", PreferenceKey);
            if (read.ExecuteScalar() is string json)
            {
                try { rememberedOrdinal = JsonSerializer.Deserialize<DeliveryDestinationPreference>(json)?.IntentOrdinal ?? 0; }
                catch (JsonException) { return OperationResult.Fail<DeliveryJournalEntry>(FailureCode.PersistenceError,
                    "Existing destination preference is unreadable; delivery transaction rolled back."); }
            }
        }
        if (current.Delivery.IntentOrdinal > rememberedOrdinal)
        {
            string json = JsonSerializer.Serialize(new DeliveryDestinationPreference(1,
                current.Delivery.RequestedFolder, current.Delivery.ResolvedFolder,
                current.Delivery.VolumeId, current.Delivery.DirectoryId, current.Delivery.IntentOrdinal));
            using SqliteCommand setting = Command(c, tx,
                "INSERT INTO Setting(Key,Value) VALUES($key,$value) " +
                "ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;");
            Add(setting, "$key", PreferenceKey); Add(setting, "$value", json);
            setting.ExecuteNonQuery();
        }
        tx.Commit();
        return OperationResult.Ok(ReadEntry(c, null, deliveryId)!);
    });

    private Task<OperationResult<DeliveryAttemptRecord>> Transition(
        Guid attemptId, string sql, string? fileId, DateTimeOffset atUtc) => Run(() =>
    {
        using SqliteConnection c = factory.Open();
        using SqliteTransaction tx = c.BeginTransaction();
        using SqliteCommand command = Command(c, tx, sql);
        Add(command, "$id", attemptId); Add(command, "$at", Iso(atUtc));
        if (fileId is not null) Add(command, "$file", fileId);
        if (command.ExecuteNonQuery() != 1)
            return OperationResult.Fail<DeliveryAttemptRecord>(FailureCode.PreconditionNotMet, "Attempt checkpoint changed.");
        DeliveryAttemptRecord result = ReadAttempt(c, tx, attemptId)!;
        tx.Commit();
        return OperationResult.Ok(result);
    });

    private static void InsertAttempt(SqliteConnection c, SqliteTransaction tx, DeliveryAttemptRecord attempt)
    {
        using SqliteCommand command = Command(c, tx, """
            INSERT INTO DeliveryAttempt
              (AttemptId,DeliveryId,AttemptNumber,DestinationKey,State,StagingLeaf,DirectoryId,
               StagingFileId,StagingCreationUtc,ExpectedSha256,ExpectedLength,StageVerifiedAtUtc,
               FailureCode,StartedAtUtc,FinishedAtUtc)
            VALUES ($id,$delivery,$number,$key,$state,$leaf,$directory,$file,$creation,$sha,$length,$verified,
                    $failure,$started,$finished);
            """);
        Add(command, "$id", attempt.AttemptId); Add(command, "$delivery", attempt.DeliveryId);
        Add(command, "$number", attempt.AttemptNumber); Add(command, "$key", attempt.DestinationKey);
        Add(command, "$state", attempt.State.ToString()); Add(command, "$leaf", attempt.StagingLeaf);
        Add(command, "$directory", attempt.DirectoryId); Add(command, "$file", attempt.StagingFileId);
        Add(command, "$creation", attempt.StagingCreationUtc is { } creation ? Iso(creation) : null);
        Add(command, "$sha", attempt.ExpectedSha256.Value); Add(command, "$length", attempt.ExpectedLength);
        Add(command, "$verified", attempt.StageVerifiedAtUtc is { } verified ? Iso(verified) : null);
        Add(command, "$failure", attempt.FailureCode); Add(command, "$started", Iso(attempt.StartedAtUtc));
        Add(command, "$finished", attempt.FinishedAtUtc is { } finished ? Iso(finished) : null);
        command.ExecuteNonQuery();
    }

    private static bool SameRequest(DeliveryRecord a, DeliveryRecord b) =>
        a.Artifact == b.Artifact && a.ReviewId == b.ReviewId &&
        a.ApprovedSha256 == b.ApprovedSha256 && a.ApprovedLength == b.ApprovedLength &&
        a.DestinationKey == b.DestinationKey && a.ReplacementOfDeliveryId == b.ReplacementOfDeliveryId &&
        a.RequestedFolder == b.RequestedFolder && a.RequestedFileName == b.RequestedFileName;

    private static bool SameCoalescedIntent(DeliveryRecord a, DeliveryRecord b) =>
        a.Artifact == b.Artifact && a.ReviewId == b.ReviewId &&
        a.ApprovedSha256 == b.ApprovedSha256 && a.ApprovedLength == b.ApprovedLength &&
        a.DestinationKey == b.DestinationKey && a.ReplacementOfDeliveryId == b.ReplacementOfDeliveryId;

    private static bool SameOrdinaryDestinationIntent(DeliveryRecord a, DeliveryRecord b) =>
        b.ReplacementOfDeliveryId is null && a.Artifact == b.Artifact && a.ReviewId == b.ReviewId &&
        a.ApprovedSha256 == b.ApprovedSha256 && a.ApprovedLength == b.ApprovedLength &&
        a.DestinationKey == b.DestinationKey;

    private static void BindAlias(SqliteConnection c, SqliteTransaction tx, DeliveryRecord request, Guid deliveryId)
    {
        using SqliteCommand command = Command(c, tx,
            "INSERT INTO DeliveryRequestAlias(RequestId,DeliveryId,RequestedFolder,RequestedFileName,RequestFingerprint) " +
            "VALUES($request,$delivery,$folder,$leaf,$fingerprint);");
        Add(command, "$request", request.RequestId);
        Add(command, "$delivery", deliveryId);
        Add(command, "$folder", request.RequestedFolder);
        Add(command, "$leaf", request.RequestedFileName);
        Add(command, "$fingerprint", Fingerprint(request));
        command.ExecuteNonQuery();
    }

    private static string Fingerprint(DeliveryRecord d)
    {
        string canonical = JsonSerializer.Serialize(new
        {
            SessionId = d.Artifact.SessionId.ToString(),
            Kind = d.Artifact.Kind.ToString(),
            d.Artifact.ArtifactId,
            ReviewId = d.ReviewId.ToString(),
            ApprovedSha256 = d.ApprovedSha256.Value,
            d.ApprovedLength,
            d.RequestedFolder,
            d.RequestedFileName,
            d.DestinationKey,
            d.ReplacementOfDeliveryId,
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool AuthorityRecorded(SqliteConnection c, SqliteTransaction tx, DeliveryRecord d)
    {
        // The service checks full aggregate lineage under the session gate; this transactional
        // predicate anchors the final commit to the current exact review and stored artifact.
        // A different process that directly changes review/output rows cannot slip between
        // this read and the delivery update in the same SQLite write transaction.
        string artifactTable = d.Artifact.Kind == ArtifactKind.ApprovedAssetPng ? "Revision" : "PrintOutput";
        string artifactExtra = d.Artifact.Kind == ArtifactKind.ApprovedAssetPng
            ? "AND a.RetentionReleasedAtUtc IS NULL"
            : "AND a.RecycledAtUtc IS NULL AND a.ReviewState='APPROVED'";
        using SqliteCommand command = Command(c, tx, $"""
            SELECT COUNT(*) FROM ReviewDecision r JOIN {artifactTable} a ON a.Id=$artifact
            WHERE r.Id=$review AND r.SessionId=$session AND r.SubjectId=$subject
              AND r.SubjectKind=$subjectKind AND r.Decision='APPROVED'
              AND r.ReviewedSha256=$sha AND a.SessionId=$session
              AND a.Sha256=$sha AND a.ByteLength=$length AND a.IsValid=1
              {artifactExtra}
              AND NOT EXISTS (
                SELECT 1 FROM ReviewDecision later
                WHERE later.SessionId=r.SessionId AND later.SubjectKind=r.SubjectKind
                  AND later.SubjectId=r.SubjectId
                  AND (later.DecidedAtUtc>r.DecidedAtUtc OR
                       (later.DecidedAtUtc=r.DecidedAtUtc AND later.Id>r.Id)));
            """);
        Add(command, "$artifact", d.Artifact.ArtifactId);
        Add(command, "$review", d.ReviewId);
        Add(command, "$session", d.Artifact.SessionId);
        Add(command, "$subject", d.ApprovalSubjectId);
        Add(command, "$subjectKind", d.Artifact.Kind == ArtifactKind.ApprovedAssetPng ? "REVISION" : "PRINT_OUTPUT");
        Add(command, "$sha", d.ApprovedSha256.Value);
        Add(command, "$length", d.ApprovedLength);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    private static DeliveryJournalEntry? Find(SqliteConnection c, SqliteTransaction? tx, string column, string value)
    {
        using SqliteCommand command = Command(c, tx,
            $"SELECT DeliveryId FROM ArtifactDelivery WHERE {column}=$value ORDER BY IntentOrdinal DESC LIMIT 1;");
        Add(command, "$value", value);
        return command.ExecuteScalar() is string id ? ReadEntry(c, tx, Guid.Parse(id)) : null;
    }

    private static DeliveryJournalEntry? ReadEntry(SqliteConnection c, SqliteTransaction? tx, Guid id)
    {
        using SqliteCommand command = Command(c, tx, "SELECT * FROM ArtifactDelivery WHERE DeliveryId=$id;");
        Add(command, "$id", id);
        DeliveryRecord? delivery;
        using (SqliteDataReader r = command.ExecuteReader())
        {
            if (!r.Read()) return null;
            ArtifactKind kind = Enum.Parse<ArtifactKind>(S(r, "Kind"));
            Guid artifact = Guid.Parse(S(r, kind == ArtifactKind.ApprovedAssetPng ? "RevisionId" : "PrintOutputId"));
            delivery = new DeliveryRecord(
                id, Guid.Parse(S(r, "RequestId")), L(r, "IntentOrdinal"),
                new ArtifactKey(SessionId.From(Guid.Parse(S(r, "SessionId"))), kind, artifact),
                ReviewId.From(Guid.Parse(S(r, "ReviewId"))), S(r, "ApprovalSubjectKind"),
                Guid.Parse(S(r, "ApprovalSubjectId")),
                NS(r, "PromotionSourceRevisionId") is { } source ? RevisionId.From(Guid.Parse(source)) : null,
                Sha256.Parse(S(r, "ApprovedSha256")), L(r, "ApprovedLength"),
                S(r, "RequestedFolder"), S(r, "RequestedFileName"), S(r, "ResolvedFolder"),
                S(r, "FinalPath"), S(r, "VolumeId"), S(r, "DirectoryId"), S(r, "DestinationKey"),
                NS(r, "ReplacementOfDeliveryId") is { } prior ? Guid.Parse(prior) : null,
                S(r, "Status"), DateTimeOffset.Parse(S(r, "CreatedAtUtc"), CultureInfo.InvariantCulture),
                Date(r, "VerifiedAtUtc"), NS(r, "VerifiedSha256") is { } hash ? Sha256.Parse(hash) : null,
                NL(r, "VerifiedLength"), NS(r, "FinalFileId"), Date(r, "FinalCreationUtc"),
                NS(r, "WinningAttemptId") is { } winner ? Guid.Parse(winner) : null);
        }
        using SqliteCommand latest = Command(c, tx,
            "SELECT AttemptId FROM DeliveryAttempt WHERE DeliveryId=$id ORDER BY AttemptNumber DESC LIMIT 1;");
        Add(latest, "$id", id);
        DeliveryAttemptRecord? attempt = latest.ExecuteScalar() is string aid ? ReadAttempt(c, tx, Guid.Parse(aid)) : null;
        return new DeliveryJournalEntry(delivery, attempt);
    }

    private static DeliveryAttemptRecord? ReadAttempt(SqliteConnection c, SqliteTransaction? tx, Guid id)
    {
        using SqliteCommand command = Command(c, tx, "SELECT * FROM DeliveryAttempt WHERE AttemptId=$id;");
        Add(command, "$id", id);
        using SqliteDataReader r = command.ExecuteReader();
        if (!r.Read()) return null;
        return new DeliveryAttemptRecord(id, Guid.Parse(S(r, "DeliveryId")), (int)L(r, "AttemptNumber"),
            S(r, "DestinationKey"), Enum.Parse<DeliveryAttemptState>(S(r, "State")),
            S(r, "StagingLeaf"), S(r, "DirectoryId"), NS(r, "StagingFileId"),
            Date(r, "StagingCreationUtc"), Sha256.Parse(S(r, "ExpectedSha256")), L(r, "ExpectedLength"),
            Date(r, "StageVerifiedAtUtc"), NS(r, "FailureCode"),
            DateTimeOffset.Parse(S(r, "StartedAtUtc"), CultureInfo.InvariantCulture), Date(r, "FinishedAtUtc"));
    }

    private static SqliteCommand Command(SqliteConnection c, SqliteTransaction? tx, string sql) =>
        new() { Connection = c, Transaction = tx, CommandText = sql };
    private static void Add(SqliteCommand c, string name, object? value) =>
        c.Parameters.AddWithValue(name, value switch
        {
            null => DBNull.Value,
            Guid id => id.ToString("D"),
            SessionId id => id.ToString(),
            ReviewId id => id.ToString(),
            RevisionId id => id.ToString(),
            _ => value,
        });
    private static string S(SqliteDataReader r, string column) => r.GetString(r.GetOrdinal(column));
    private static string? NS(SqliteDataReader r, string column) => r.IsDBNull(r.GetOrdinal(column)) ? null : S(r, column);
    private static long L(SqliteDataReader r, string column) => r.GetInt64(r.GetOrdinal(column));
    private static long? NL(SqliteDataReader r, string column) => r.IsDBNull(r.GetOrdinal(column)) ? null : L(r, column);
    private static DateTimeOffset? Date(SqliteDataReader r, string column) =>
        NS(r, column) is { } value ? DateTimeOffset.Parse(value, CultureInfo.InvariantCulture) : null;
    private static string Iso(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static long ScalarLong(SqliteConnection c, SqliteTransaction tx, string sql)
    { using SqliteCommand command = Command(c, tx, sql); return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture); }

    public Task<OperationResult<DeliveryDestinationPreferenceView?>> ReadDestinationPreferenceAsync(
        CancellationToken cancellationToken) => Run(() =>
    {
        using SqliteConnection c = factory.Open();
        using SqliteCommand read = Command(c, null, "SELECT Value FROM Setting WHERE Key=$key;");
        Add(read, "$key", PreferenceKey);
        if (read.ExecuteScalar() is not string json) return OperationResult.Ok<DeliveryDestinationPreferenceView?>(null);
        try
        {
            // The operator's own spelling is displayed; delivery resolves it again from scratch.
            DeliveryDestinationPreference? stored = JsonSerializer.Deserialize<DeliveryDestinationPreference>(json);
            return stored is { DisplayFolder.Length: > 0 }
                ? OperationResult.Ok<DeliveryDestinationPreferenceView?>(new(stored.DisplayFolder, stored.IntentOrdinal))
                : OperationResult.Fail<DeliveryDestinationPreferenceView?>(FailureCode.PersistenceError,
                    "Destination preference is incomplete.");
        }
        catch (JsonException)
        {
            return OperationResult.Fail<DeliveryDestinationPreferenceView?>(FailureCode.PersistenceError,
                "Destination preference is unreadable.");
        }
    });

    private static Task<OperationResult<T>> Run<T>(Func<OperationResult<T>> body)
    {
        try { return Task.FromResult(body()); }
        catch (SqliteException ex) { return Task.FromResult(OperationResult.Fail<T>(FailureCode.PersistenceError, ex.Message)); }
    }

    private sealed record DeliveryDestinationPreference(
        int Version, string DisplayFolder, string ResolvedFolder,
        string VolumeId, string DirectoryId, long IntentOrdinal);
}
