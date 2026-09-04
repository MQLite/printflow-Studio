using Dapper;
using Microsoft.Data.Sqlite;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;

namespace PrintFlow.Infrastructure.Sqlite;

internal static class PdfInspectionStorage
{
    internal static async Task<IReadOnlyList<ProcessingAttempt>> LoadAsync(SqliteConnection connection, IReadOnlyList<ProcessingAttempt> attempts)
    {
        List<ProcessingAttempt> result = [];
        foreach (var attempt in attempts)
        {
            if (attempt.Operation != PrintFlow.Domain.Revisions.OperationKind.PreparePdf)
            {
                result.Add(attempt);
                continue;
            }
            var row = await connection.QuerySingleOrDefaultAsync<InspectionRow>(
                "SELECT * FROM PdfInspection WHERE AttemptId = @Id;", new { Id = attempt.Id.ToString() });
            result.Add(row is null ? attempt : attempt with { PdfInspection = new PdfInspection(
                row.IsReadable, row.IsEncrypted, row.PageCount, row.PreparedPageNumber,
                row.MediaWidth is null ? null : new(row.MediaX!.Value, row.MediaY!.Value, row.MediaWidth.Value, row.MediaHeight!.Value),
                row.CropWidth is null ? null : new(row.CropX!.Value, row.CropY!.Value, row.CropWidth.Value, row.CropHeight!.Value),
                row.RotationDegrees, row.PageWidth, row.PageHeight, row.RequestedRasterDpi,
                row.PixelWidth, row.PixelHeight, row.HasTransparency, row.Provider) });
        }
        return result;
    }

    internal static async Task WriteAsync(SqliteConnection connection, SqliteTransaction transaction, ProcessingAttempt attempt)
    {
        if (attempt.PdfInspection is not { } facts) return;
        await connection.ExecuteAsync("""
            INSERT INTO PdfInspection (AttemptId, IsReadable, IsEncrypted, PageCount, PreparedPageNumber, MediaX, MediaY, MediaWidth, MediaHeight, CropX, CropY, CropWidth, CropHeight, RotationDegrees, PageWidth, PageHeight, RequestedRasterDpi, PixelWidth, PixelHeight, HasTransparency, Provider)
            VALUES (@Id, @IsReadable, @IsEncrypted, @PageCount, @PreparedPageNumber, @MediaX, @MediaY, @MediaWidth, @MediaHeight, @CropX, @CropY, @CropWidth, @CropHeight, @RotationDegrees, @PageWidth, @PageHeight, @RequestedRasterDpi, @PixelWidth, @PixelHeight, @HasTransparency, @Provider)
            ON CONFLICT(AttemptId) DO NOTHING;
            """, new { Id = attempt.Id.ToString(), facts.IsReadable, facts.IsEncrypted, facts.PageCount, facts.PreparedPageNumber, MediaX = facts.MediaBox?.X, MediaY = facts.MediaBox?.Y, MediaWidth = facts.MediaBox?.Width, MediaHeight = facts.MediaBox?.Height, CropX = facts.CropBox?.X, CropY = facts.CropBox?.Y, CropWidth = facts.CropBox?.Width, CropHeight = facts.CropBox?.Height, facts.RotationDegrees, facts.PageWidth, facts.PageHeight, facts.RequestedRasterDpi, facts.PixelWidth, facts.PixelHeight, facts.HasTransparency, facts.Provider }, transaction);
    }

    private sealed class InspectionRow
    {
        public bool IsReadable { get; set; }
        public bool? IsEncrypted { get; set; }
        public int? PageCount { get; set; }
        public int? PreparedPageNumber { get; set; }
        public double? MediaX { get; set; }
        public double? MediaY { get; set; }
        public double? MediaWidth { get; set; }
        public double? MediaHeight { get; set; }
        public double? CropX { get; set; }
        public double? CropY { get; set; }
        public double? CropWidth { get; set; }
        public double? CropHeight { get; set; }
        public int? RotationDegrees { get; set; }
        public double? PageWidth { get; set; }
        public double? PageHeight { get; set; }
        public int RequestedRasterDpi { get; set; }
        public int? PixelWidth { get; set; }
        public int? PixelHeight { get; set; }
        public bool? HasTransparency { get; set; }
        public string Provider { get; set; } = "";
    }
}
