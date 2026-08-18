using System.IO;
using Microsoft.Data.Sqlite;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Sqlite;

namespace PrintFlow.Tests.Fixtures;

/// <summary>A throwaway, fully migrated SQLite database under the OS temp directory.</summary>
internal sealed class TempDatabase : IDisposable
{
    public string Path { get; }

    public SqliteConnectionFactory Factory { get; }

    public TempDatabase(bool migrate = true)
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PrintFlowTests");
        Directory.CreateDirectory(directory);
        Path = System.IO.Path.Combine(directory, Guid.NewGuid().ToString("N") + ".db");
        Factory = new SqliteConnectionFactory(Path);

        if (migrate)
        {
            using SqliteConnection connection = Factory.Open();
            OperationResult<PrintFlow.Domain.Results.Unit> result = MigrationRunner.Migrate(connection);
            if (result.IsFailure)
            {
                throw new InvalidOperationException($"Fixture migration failed: {result.Failure}");
            }
        }
    }

    /// <summary>Opens a connection without applying migrations, for migration-behaviour tests.</summary>
    public SqliteConnection OpenRaw() => Factory.Open();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        TryDelete(Path);
        TryDelete(Path + "-wal");
        TryDelete(Path + "-shm");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
