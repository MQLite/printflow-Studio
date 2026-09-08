using PrintFlow.Domain.Results;
using PrintFlow.Domain.Settings;

namespace PrintFlow.Workflow.Ports;

/// <summary>
/// Persisted operator settings (Jira 11108; MVP design §17.6).
/// </summary>
/// <remarks>
/// A sibling of <see cref="ISessionRepository"/> rather than a method on it, because a setting
/// belongs to no session: folding it into the session aggregate's mutation would make "change a
/// setting" require a session, and would put a value with no workflow meaning inside a workflow
/// transaction.
/// <para>
/// The same architectural rule holds here as for sessions: nothing above this seam sees
/// <c>SqliteConnection</c>, Dapper or SQL, and the key space is the closed
/// <see cref="SettingKey"/> vocabulary rather than free text.
/// </para>
/// <para>
/// <see cref="UpsertAsync"/> takes a batch and writes it in <b>one</b> transaction. Settings are
/// changed together — a Settings screen commits what the operator edited, not one field at a
/// time — and a half-applied batch is exactly the inconsistency Jira 11108 requires not to
/// survive a failure.
/// </para>
/// </remarks>
public interface ISettingsRepository
{
    /// <summary>Reads one setting, or null when no row exists for it.</summary>
    /// <remarks>
    /// Null means "the operator has never set this", and every caller must answer it with the
    /// behaviour it already has — its configured or preset default. An installation upgraded to
    /// this schema has no rows at all, so reading must leave current behaviour unchanged.
    /// </remarks>
    Task<OperationResult<SettingEntry?>> ReadAsync(SettingKey key, CancellationToken cancellationToken);

    /// <summary>Reads every persisted setting.</summary>
    Task<OperationResult<IReadOnlyList<SettingEntry>>> ReadAllAsync(CancellationToken cancellationToken);

    /// <summary>Inserts or replaces <paramref name="entries"/> as one transaction.</summary>
    Task<OperationResult<Unit>> UpsertAsync(
        IReadOnlyList<SettingEntry> entries, CancellationToken cancellationToken);
}
