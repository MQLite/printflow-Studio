using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.AiDesign.Memorial;

public sealed partial class WpfMemorialTextLayout
{
    private const double UnitsPerMm = 96 / 25.4;
    private const double FormattingWidth = 1_000_000;
    private sealed record Fragment(int Start, int Length, int GlyphCount, Geometry Ink, double Left, double Width, double Baseline, double Ascent, double Height);

    private OperationResult<WpfMemorialTextDrawing> Shape(MemorialTextLine line)
    {
        if (line is null || line.Text is null || line.Bounds is null || !Positive(line.FontSizeMm) || !Positive(line.Bounds.Width) || !Positive(line.Bounds.Height) ||
            !double.IsFinite(line.Bounds.X) || !double.IsFinite(line.Bounds.Y) || !Enum.IsDefined(line.Placement) ||
            line.FontSizeMm > 1000 || line.Text.Length > _options.MaximumTextLength) return Failed<WpfMemorialTextDrawing>("InvalidTextStyle", line?.Key ?? "");
        if (line.FontSizeMm < _options.MinimumFontSizeMm) return Failed<WpfMemorialTextDrawing>("FontTooSmall", line.Key);
        if (line.Placement == MemorialTextPlacement.Arc && (line.ArcRadiusMm is not { } radius || !Positive(radius)) ||
            line.Placement == MemorialTextPlacement.Flat && line.ArcRadiusMm is not null) return Failed<WpfMemorialTextDrawing>("InvalidTextStyle", line.Key);
        if (line.ArcRadiusMm is { } arcRadius && !Positive(arcRadius * UnitsPerMm)) return Failed<WpfMemorialTextDrawing>("InvalidTextStyle", line.Key);
        if (line.Placement == MemorialTextPlacement.Arc && (line.Text.Any(IsLineBreak) || line.Text.Contains('\t'))) return Failed<WpfMemorialTextDrawing>("ArcRequiresSingleLine", line.Key);
        foreach (var rune in line.Text.EnumerateRunes())
            if (rune.Value == 0xFFFD || Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control && rune.Value is not ('\r' or '\n' or '\t' or 0x85))
                return Failed<WpfMemorialTextDrawing>("InvalidText", line.Key);
        // Select only an installed family object; an arbitrary path/URI/fallback string never reaches the font resolver.
        var family = Fonts.SystemFontFamilies.FirstOrDefault(x => string.Equals(x.Source, line.FontFamily, StringComparison.OrdinalIgnoreCase) ||
            x.FamilyNames.Values.Any(name => string.Equals(name, line.FontFamily, StringComparison.OrdinalIgnoreCase)));
        if (family is null) return Failed<WpfMemorialTextDrawing>("FontUnavailable", line.Key);
        var properties = new RunProperties(new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), line.FontSizeMm * UnitsPerMm);
        var source = new LiteralSource(line.Text, properties);
        using var formatter = TextFormatter.Create(TextFormattingMode.Ideal);
        List<Fragment> fragments = []; double width = 0, height = 0;
        int index = 0;
        do
        {
            int lineEnd = index; while (lineEnd < line.Text.Length && !IsLineBreak(line.Text[lineEnd])) lineEnd++;
            int expectedLength = lineEnd - index;
            if (lineEnd < line.Text.Length) expectedLength += line.Text[lineEnd] == '\r' && lineEnd + 1 < line.Text.Length && line.Text[lineEnd + 1] == '\n' ? 2 : 1;
            var paragraph = new ParagraphProperties(properties, ParagraphDirection(line.Text, index, lineEnd));
            using var row = formatter.FormatLine(source, index, FormattingWidth, paragraph, null);
            if (row.Length <= 0 || row.Length < expectedLength || row.IsTruncated || row.HasCollapsed) return Failed<WpfMemorialTextDrawing>("TextDoesNotFit", line.Key);
            width = Math.Max(width, row.WidthIncludingTrailingWhitespace);
            // IndexedGlyphRun carries source spans, but SimpleTextLine returns every run
            // at (0,0). Draw supplies the actual baseline and fallback/bidi placement.
            var drawing = new DrawingGroup();
            // RTL Draw coordinates start at the paragraph's right edge. Remove the
            // formatting-width margin while retaining trailing-space advances.
            double rowOffset = paragraph.FlowDirection == FlowDirection.RightToLeft
                ? row.WidthIncludingTrailingWhitespace + row.Start - FormattingWidth : -row.Start;
            using (var context = drawing.Open()) row.Draw(context, new Point(rowOffset, height), InvertAxes.None);
            var drawnRuns = DrawnRuns(drawing, Matrix.Identity).ToList();
            foreach (var indexed in row.GetIndexedGlyphRuns())
            {
                var indexedRun = indexed.GlyphRun;
                int drawnIndex = drawnRuns.FindIndex(x => x.Run.GlyphTypeface.FontUri == indexedRun.GlyphTypeface.FontUri &&
                    x.Run.BidiLevel == indexedRun.BidiLevel && x.Run.GlyphIndices.SequenceEqual(indexedRun.GlyphIndices));
                if (drawnIndex < 0) return Failed<WpfMemorialTextDrawing>("GlyphRunMappingUnavailable", line.Key);
                var (run, drawingTransform) = drawnRuns[drawnIndex]; drawnRuns.RemoveAt(drawnIndex);
                int chars = indexedRun.Characters?.Count ?? indexed.TextSourceLength;
                var maps = indexedRun.ClusterMap is { Count: > 0 } ? indexedRun.ClusterMap.ToArray() : Enumerable.Range(0, chars).Select(x => checked((ushort)x)).ToArray();
                var boundaries = maps.Select(x => (int)x).Append(run.GlyphIndices.Count).Distinct().Order().ToArray();
                double[] advances = new double[run.GlyphIndices.Count + 1];
                for (int g = 0; g < run.GlyphIndices.Count; g++) advances[g + 1] = advances[g] + run.AdvanceWidths[g];
                for (int c = 0; c < chars;)
                {
                    int end = c + 1; while (end < chars && maps[end] == maps[c]) end++;
                    int startGlyph = maps[c], endGlyph = boundaries.First(x => x > startGlyph);
                    int start = indexed.TextSourceCharacterIndex + c, length = end - c;
                    if (start < 0 || start + length > line.Text.Length) return Failed<WpfMemorialTextDrawing>("TextShapingUnavailable", line.Key);
                    bool visible = line.Text.Substring(start, length).EnumerateRunes().Any(r => Rune.GetUnicodeCategory(r) != UnicodeCategory.Format &&
                        r.Value is not (>= 0xFE00 and <= 0xFE0F) and not (>= 0xE0100 and <= 0xE01EF));
                    if (visible && run.GlyphIndices.Skip(startGlyph).Take(endGlyph - startGlyph).Any(g => g == 0)) return Failed<WpfMemorialTextDrawing>("MissingGlyph", line.Key);
                    double direction = (run.BidiLevel & 1) == 0 ? 1 : -1;
                    var origin = new Point(run.BaselineOrigin.X + direction * advances[startGlyph], run.BaselineOrigin.Y);
                    var slice = new GlyphRun(run.GlyphTypeface, run.BidiLevel, run.IsSideways, run.FontRenderingEmSize, 1f,
                        run.GlyphIndices.Skip(startGlyph).Take(endGlyph - startGlyph).ToArray(), origin,
                        run.AdvanceWidths.Skip(startGlyph).Take(endGlyph - startGlyph).ToArray(),
                        run.GlyphOffsets is { Count: > 0 } ? run.GlyphOffsets.Skip(startGlyph).Take(endGlyph - startGlyph).ToArray() : null,
                        null, null, null, null, null);
                    double advance = advances[endGlyph] - advances[startGlyph];
                    // Space glyphs can return the shared frozen Geometry.Empty. Apply
                    // transforms to our own container, never mutate WPF's cached outline.
                    var outline = new GeometryGroup { FillRule = FillRule.Nonzero, Transform = new MatrixTransform(drawingTransform) };
                    outline.Children.Add(slice.BuildGeometry());
                    var actualOrigin = drawingTransform.Transform(origin);
                    fragments.Add(new(start, length, endGlyph - startGlyph, outline, actualOrigin.X + (direction < 0 ? -advance : 0), advance,
                        actualOrigin.Y, row.Baseline, row.Height));
                    c = end;
                }
            }
            height += row.Height; index += row.Length;
        } while (index < line.Text.Length || index == line.Text.Length && line.Text.Length > 0 && IsLineBreak(line.Text[^1]));

        var group = new GeometryGroup { FillRule = FillRule.Nonzero }; var envelope = new Rect(0, 0, width, height);
        List<WpfMemorialTextCluster> clusters = [];
        var starts = StringInfo.ParseCombiningCharacters(line.Text).ToList(); starts.Add(line.Text.Length);
        starts.RemoveAll(boundary => fragments.Any(x => boundary > x.Start && boundary < x.Start + x.Length));
        if (line.Placement == MemorialTextPlacement.Arc)
        {
            if (width / UnitsPerMm / line.ArcRadiusMm!.Value > Math.PI) return Failed<WpfMemorialTextDrawing>("TextDoesNotFit", line.Key);
            envelope = Rect.Empty;
        }
        for (int i = 0; i + 1 < starts.Count; i++)
        {
            int begin = starts[i], end = starts[i + 1];
            var pieces = fragments.Where(x => x.Start >= begin && x.Start < end).ToArray();
            if (pieces.Length == 0) continue;
            var ink = new GeometryGroup { FillRule = FillRule.Nonzero }; foreach (var part in pieces) ink.Children.Add(part.Ink);
            double left = pieces.Min(x => x.Left), right = pieces.Max(x => x.Left + x.Width);
            double angle = 0;
            var clusterOrigin = new Point((left + right) / 2, pieces[0].Baseline);
            if (line.Placement == MemorialTextPlacement.Arc)
            {
                double center = (left + right) / 2, radiusUnits = line.ArcRadiusMm!.Value * UnitsPerMm;
                double theta = (center - width / 2) / radiusUnits; angle = theta * 180 / Math.PI;
                var matrix = Matrix.Identity; matrix.Translate(-center, -pieces[0].Baseline); matrix.Rotate(angle);
                matrix.Translate(radiusUnits * Math.Sin(theta), radiusUnits * (1 - Math.Cos(theta)));
                clusterOrigin = matrix.Transform(clusterOrigin);
                ink.Transform = new MatrixTransform(matrix);
                var box = new Rect(left, pieces[0].Baseline - pieces[0].Ascent, right - left, pieces.Max(x => x.Height));
                envelope.Union(new MatrixTransform(matrix).TransformBounds(box));
            }
            group.Children.Add(ink); clusters.Add(new(begin, end - begin, pieces.Sum(x => x.GlyphCount), angle) { OriginMm = clusterOrigin });
        }
        envelope.Union(group.Bounds);
        if (envelope.IsEmpty) envelope = new(0, 0, width, height);
        if (envelope.Width / UnitsPerMm > line.Bounds.Width + 1e-7 || envelope.Height / UnitsPerMm > line.Bounds.Height + 1e-7)
            return Failed<WpfMemorialTextDrawing>("TextDoesNotFit", line.Key, $"Required envelope {envelope}; logical width {width}, height {height}.");
        var transform = Matrix.Identity; transform.Scale(1 / UnitsPerMm, 1 / UnitsPerMm);
        transform.Translate(line.Bounds.X + (line.Bounds.Width - envelope.Width / UnitsPerMm) / 2 - envelope.X / UnitsPerMm,
            line.Bounds.Y + (line.Bounds.Height - envelope.Height / UnitsPerMm) / 2 - envelope.Y / UnitsPerMm);
        if (!Finite(transform)) return Failed<WpfMemorialTextDrawing>("InvalidTextStyle", line.Key);
        group.Transform = new MatrixTransform(transform);
        var bounds = group.Bounds;
        if (!string.IsNullOrWhiteSpace(line.Text) && (bounds.IsEmpty || !Positive(bounds.Width) || !Positive(bounds.Height)))
            return Failed<WpfMemorialTextDrawing>("NoVisibleText", line.Key);
        if (!bounds.IsEmpty && (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height)))
            return Failed<WpfMemorialTextDrawing>("InvalidTextStyle", line.Key);
        group.Freeze();
        return OperationResult.Ok(new WpfMemorialTextDrawing(group, Array.AsReadOnly(clusters.Select(x => x with { OriginMm = transform.Transform(x.OriginMm) }).ToArray())));
    }

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
    private static bool Finite(Matrix value) => double.IsFinite(value.M11) && double.IsFinite(value.M12) && double.IsFinite(value.M21) &&
        double.IsFinite(value.M22) && double.IsFinite(value.OffsetX) && double.IsFinite(value.OffsetY);

    private static IEnumerable<(GlyphRun Run, Matrix Transform)> DrawnRuns(Drawing drawing, Matrix parent)
    {
        if (drawing is GlyphRunDrawing glyph && glyph.GlyphRun is { } run) yield return (run, parent);
        if (drawing is not DrawingGroup group) yield break;
        var matrix = group.Transform?.Value ?? Matrix.Identity; matrix.Append(parent);
        foreach (var child in group.Children)
            foreach (var item in DrawnRuns(child, matrix)) yield return item;
    }
}
