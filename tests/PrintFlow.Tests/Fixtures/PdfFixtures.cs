using System.IO;

namespace PrintFlow.Tests.Fixtures;

internal static class PdfFixtures
{
    internal static byte[] Read(string name)
    {
        using var stream = typeof(PdfFixtures).Assembly.GetManifestResourceStream($"PrintFlow.Tests.Fixtures.Pdf.{name}.pdf")!;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    }
}
