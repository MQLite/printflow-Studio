using System.IO;
using Microsoft.Data.Sqlite;

namespace PrintFlow.Infrastructure.Sqlite;

/// <summary>
/// Opens SQLite connections with the required pragmas applied consistently
/// (Epic 11100 Task 11108; plan §4.4).
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string databaseAbsolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseAbsolutePath);

        string? directory = System.IO.Path.GetDirectoryName(databaseAbsolutePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        SqliteConnectionStringBuilder builder = new() { DataSource = databaseAbsolutePath };
        _connectionString = builder.ToString();
    }

    /// <summary>Opens a new connection with WAL, full durability, foreign keys and a busy timeout applied.</summary>
    public SqliteConnection Open()
    {
        SqliteConnection connection = new(_connectionString);
        connection.Open();

        using SqliteCommand pragmas = connection.CreateCommand();
        pragmas.CommandText =
            """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = FULL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            """;
        pragmas.ExecuteNonQuery();

        return connection;
    }
}
