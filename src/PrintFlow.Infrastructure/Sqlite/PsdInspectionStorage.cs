using Dapper;
using Microsoft.Data.Sqlite;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;

namespace PrintFlow.Infrastructure.Sqlite;

internal static class PsdInspectionStorage
{
    internal static async Task<IReadOnlyList<ProcessingAttempt>> LoadAsync(
        SqliteConnection connection, IReadOnlyList<ProcessingAttempt> attempts)
    {
        List<ProcessingAttempt> result = [];
        foreach (ProcessingAttempt attempt in attempts)
        {
            if (attempt.Operation != PrintFlow.Domain.Revisions.OperationKind.PreparePsd)
            {
                result.Add(attempt);
                continue;
            }
            var row = await connection.QuerySingleOrDefaultAsync<InspectionRow>(
                "SELECT * FROM PsdInspection WHERE AttemptId = @Id;", new { Id = attempt.Id.ToString() });
            if (row is null) { result.Add(attempt); continue; }
            var channels = await connection.QueryAsync<ChannelRow>(
                "SELECT Name, Kind FROM PsdChannel WHERE AttemptId = @Id ORDER BY Ordinal;",
                new { Id = attempt.Id.ToString() });
            result.Add(attempt with { PsdInspection = new PsdInspection(row.PixelWidth, row.PixelHeight,
                row.OriginalMode, row.BitDepth, row.HasRealMergedData, row.HasTransparency,
                [.. channels.Select(c => new PsdChannel(c.Name, c.Kind))], row.PhotoshopVersion) });
        }
        return result;
    }

    internal static async Task WriteAsync(SqliteConnection connection, SqliteTransaction transaction,
        ProcessingAttempt attempt)
    {
        if (attempt.PsdInspection is not { } facts) { return; }
        string id = attempt.Id.ToString();
        int inserted = await connection.ExecuteAsync("""
            INSERT INTO PsdInspection (AttemptId, PixelWidth, PixelHeight, OriginalMode, BitDepth,
                HasRealMergedData, HasTransparency, PhotoshopVersion)
            VALUES (@Id, @PixelWidth, @PixelHeight, @OriginalMode, @BitDepth,
                @HasRealMergedData, @HasTransparency, @PhotoshopVersion)
            ON CONFLICT(AttemptId) DO NOTHING;
            """, new { Id = id, facts.PixelWidth, facts.PixelHeight, facts.OriginalMode, facts.BitDepth,
                facts.HasRealMergedData, facts.HasTransparency, facts.PhotoshopVersion }, transaction);
        if (inserted == 0) { return; }
        for (int i = 0; i < facts.Channels.Length; i++)
        {
            PsdChannel channel = facts.Channels[i];
            await connection.ExecuteAsync("""
                INSERT INTO PsdChannel (AttemptId, Ordinal, Name, Kind) VALUES (@Id, @Ordinal, @Name, @Kind);
                """, new { Id = id, Ordinal = i, channel.Name, channel.Kind }, transaction);
        }
    }

    private sealed class InspectionRow
    {
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
        public string OriginalMode { get; set; } = "";
        public int BitDepth { get; set; }
        public bool HasRealMergedData { get; set; }
        public bool? HasTransparency { get; set; }
        public string PhotoshopVersion { get; set; } = "";
    }
    private sealed class ChannelRow
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
    }
}
