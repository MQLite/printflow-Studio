using System.Globalization;
using System.Text;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// Synthetic single-page PDFs whose visible artwork is painted through a named spot colourant.
/// </summary>
/// <remarks>
/// Written byte-by-byte rather than produced by <c>Fixtures/Pdf/generate.py</c> because these
/// exist to be read as evidence: the colourant name, the colour-space family and the tint
/// transform are all legible in the source of this file, so a reader can confirm that the spot
/// really is present in the PDF structure before drawing any conclusion about what PrintFlow can
/// or cannot observe. No customer artwork and no third-party PDF writer is involved.
/// <para>
/// Both files are deliberately <i>renderable</i>. The tint transform maps full tint onto a
/// strongly visible alternate colour, so the accepted <c>Windows.Data.Pdf</c> path succeeds and
/// produces non-blank pixels. That is the whole point of the fixture: a successful render is not
/// evidence that the source carried no spot channel.
/// </para>
/// </remarks>
internal static class SpotColourantPdfFixtures
{
    /// <summary>The production white-ink channel name, used verbatim as the colourant name.</summary>
    internal const string ColourantName = "W1";

    /// <summary>Page width in points; 2 inches, matching the other single-page fixtures.</summary>
    private const int PageWidthPoints = 144;

    /// <summary>Page height in points; 1 inch.</summary>
    private const int PageHeightPoints = 72;

    /// <summary>
    /// One page painting a filled rectangle through <c>[/Separation /W1 /DeviceCMYK]</c>.
    /// </summary>
    internal static byte[] SeparationW1() => Build(
        colourSpace: $"[/Separation /{ColourantName} /DeviceCMYK 5 0 R]",
        // Full tint of the spot resolves to solid magenta+yellow, i.e. visibly red.
        tintTransform: "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 1 1 0] /N 1 >>");

    /// <summary>
    /// One page painting a filled rectangle through <c>[/DeviceN [/W1] /DeviceCMYK]</c>.
    /// </summary>
    internal static byte[] DeviceNW1() => Build(
        colourSpace: $"[/DeviceN [/{ColourantName}] /DeviceCMYK 5 0 R]",
        // Full tint of the spot resolves to solid cyan.
        tintTransform: "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [1 0 0 0] /N 1 >>");

    /// <summary>
    /// The same page geometry with no spot colourant anywhere, painted in <c>/DeviceRGB</c>.
    /// </summary>
    /// <remarks>
    /// The control. Every fact the accepted PDF inspection can report is identical between this
    /// file and the two above, which is what makes the comparison in the gate test meaningful.
    /// </remarks>
    internal static byte[] DeviceRgbControl() => Build(
        colourSpace: null,
        tintTransform: null);

    private static byte[] Build(string? colourSpace, string? tintTransform)
    {
        bool spot = colourSpace is not null;
        string resources = spot
            ? "<< /ColorSpace << /CS0 4 0 R >> >>"
            : "<< >>";
        // Identical geometry in both branches so the rendered rectangle is the same rectangle.
        string content = spot
            ? "/CS0 cs 1 scn 10 10 40 30 re f\n"
            : "1 0 0 rg 10 10 40 30 re f\n";

        List<string> objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R " +
                $"/MediaBox [0 0 {PageWidthPoints} {PageHeightPoints}] " +
                $"/Resources {resources} /Contents 6 0 R >>",
            colourSpace ?? "null",
            tintTransform ?? "null",
            $"<< /Length {content.Length} >>\nstream\n{content}endstream",
        ];

        StringBuilder document = new();
        document.Append("%PDF-1.7\n");
        List<int> offsets = [];
        for (int number = 1; number <= objects.Count; number++)
        {
            offsets.Add(document.Length);
            document.Append(number.ToString(CultureInfo.InvariantCulture))
                .Append(" 0 obj\n").Append(objects[number - 1]).Append("\nendobj\n");
        }

        int startXref = document.Length;
        document.Append("xref\n0 ").Append((objects.Count + 1).ToString(CultureInfo.InvariantCulture))
            .Append("\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            document.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        document.Append("trailer\n<< /Size ")
            .Append((objects.Count + 1).ToString(CultureInfo.InvariantCulture))
            .Append(" /Root 1 0 R >>\nstartxref\n")
            .Append(startXref.ToString(CultureInfo.InvariantCulture))
            .Append("\n%%EOF\n");

        // Every byte written above is ASCII, so character offsets are byte offsets and the xref
        // table is correct as written.
        return Encoding.ASCII.GetBytes(document.ToString());
    }
}
