namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// The collection every SQLite-touching test belongs to, so that no two of them run at once.
/// </summary>
/// <remarks>
/// <c>TempDatabase</c> and <c>TempApplication</c> call
/// <c>SqliteConnection.ClearAllPools()</c> when they are disposed, so that the temp database
/// file is unlocked and can be deleted. That call is <b>process-global</b>: it disposes the
/// pooled connections of every other test running at that moment, and the victim fails with an
/// <c>ObjectDisposedException</c> from deep inside SQLitePCL — a flake that looks like a
/// persistence bug and is not one.
/// <para>
/// Putting these classes in one xUnit collection is the smallest fix that removes the race:
/// tests in the same collection never run concurrently, so a disposal can no longer land on a
/// connection another test is using. Everything else in the suite — the unit, workflow and
/// architecture tests, which are the overwhelming majority — keeps running in parallel,
/// because none of them opens a connection.
/// </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class SqliteCollection
{
    public const string Name = "SQLite";
}
