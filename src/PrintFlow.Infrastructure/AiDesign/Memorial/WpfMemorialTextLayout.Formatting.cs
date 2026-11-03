using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Infrastructure.AiDesign.Memorial;

public sealed partial class WpfMemorialTextLayout
{
    private sealed class LiteralSource(string text, TextRunProperties properties) : TextSource
    {
        public override TextRun GetTextRun(int index)
        {
            if (index >= text.Length) return new TextEndOfParagraph(1);
            if (IsLineBreak(text[index])) return new TextEndOfLine(text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1);
            int end = index;
            while (end < text.Length && !IsLineBreak(text[end])) end++;
            return new TextCharacters(text, index, end - index, properties);
        }
        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit) =>
            new(limit, new(CultureInfo.InvariantCulture, new CharacterBufferRange(text, 0, limit)));
        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => index;
    }

    private static bool IsLineBreak(char value) => value is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029';

    private sealed class RunProperties(Typeface typeface, double size) : TextRunProperties
    {
        public override Typeface Typeface => typeface;
        public override double FontRenderingEmSize => size;
        public override double FontHintingEmSize => size;
        public override TextDecorationCollection TextDecorations => [];
        public override Brush ForegroundBrush => Brushes.Black;
        public override Brush? BackgroundBrush => null;
        public override CultureInfo CultureInfo => CultureInfo.InvariantCulture;
        public override TextEffectCollection TextEffects => [];
    }

    private sealed class ParagraphProperties(TextRunProperties properties, FlowDirection direction) : TextParagraphProperties
    {
        public override FlowDirection FlowDirection => direction;
        public override TextAlignment TextAlignment => TextAlignment.Left;
        public override double LineHeight => 0;
        public override bool FirstLineInParagraph => true;
        public override TextRunProperties DefaultTextRunProperties => properties;
        public override TextWrapping TextWrapping => TextWrapping.NoWrap;
        public override TextMarkerProperties? TextMarkerProperties => null;
        public override double Indent => 0;
    }

    private static FlowDirection ParagraphDirection(string text, int start, int end)
    {
        if (end == start) return FlowDirection.LeftToRight;
        string paragraph = text[start..end]; var types = new ushort[paragraph.Length];
        if (!NativeMethods.GetStringTypeW(2, paragraph, paragraph.Length, types)) throw new InvalidOperationException("Paragraph bidi classification is unavailable.");
        int isolates = 0;
        for (int i = 0; i < paragraph.Length; i++)
        {
            char value = paragraph[i];
            if (value is '\u2066' or '\u2067' or '\u2068') { isolates++; continue; }
            if (value == '\u2069') { isolates = Math.Max(0, isolates - 1); continue; }
            if (isolates > 0) continue;
            if (value is '\u202B' or '\u202E' or '\u200F' or '\u061C') return FlowDirection.RightToLeft;
            if (value is '\u202A' or '\u202D' or '\u200E') return FlowDirection.LeftToRight;
            if (types[i] == 1) return FlowDirection.LeftToRight;
            if (types[i] == 2) return FlowDirection.RightToLeft;
        }
        return FlowDirection.LeftToRight;
    }
}
