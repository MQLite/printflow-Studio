using System.Text.RegularExpressions;
using PrintFlow.Domain.AiDesign.Authoring;

namespace PrintFlow.Workflow.AiDesign.Authoring;

/// <summary>Applies changes in generated text to the editable document, keeping the operator's prose and image nodes.</summary>
public static partial class PromptFieldSynchronization
{
    [GeneratedRegex(@"[^\r\n]*(?:\r\n|\r|\n)|[^\r\n]+$")]
    private static partial Regex Lines();

    public static bool ContentEquals(AiPromptDocument left, AiPromptDocument right) =>
        TextAndImages(left).SequenceEqual(TextAndImages(right));

    private static IEnumerable<(string Text, string? Slot)> TextAndImages(AiPromptDocument document)
    {
        var text = new System.Text.StringBuilder();
        foreach (var part in document.Parts)
        {
            if (part is AiTextPart literal) text.Append(literal.Text);
            else if (part is AiImagePart image)
            {
                yield return (text.ToString(), image.SlotId);
                text.Clear();
            }
        }
        yield return (text.ToString(), null);
    }

    public static AiPromptDocument Update(AiPromptDocument edited, AiPromptDocument before, AiPromptDocument after)
    {
        // Project image nodes as atomic tokens so whole-line additions/removals can cross
        // references. The input text never contains the private token prefix.
        var literals = new[] { edited, before, after }.SelectMany(x => x.Parts).OfType<AiTextPart>().ToArray();
        string prefix;
        do { prefix = "\uE000" + Guid.NewGuid().ToString("N") + "-"; }
        while (literals.Any(x => x.Text.Contains(prefix, StringComparison.Ordinal)));
        var images = new List<AiImagePart>();
        var tokens = new Dictionary<ImageOccurrence, string>();
        var currentImages = ImageOccurrences(edited).ToHashSet();
        var previousImages = ImageOccurrences(before).ToHashSet();
        var revisedImages = ImageOccurrences(after).ToHashSet();
        var removedManually = previousImages.Intersect(revisedImages).Except(currentImages).ToHashSet();

        string text = Encode(edited);
        string oldText = Encode(before);
        string newText = Encode(after);
        if (oldText == newText) return edited;
        var retainedTokens = currentImages.Select(key => tokens[key]).ToArray();
        string merged = MergeText(text, oldText, newText, retainedTokens);
        if (merged == text) return edited;

        var parts = new List<AiPromptPart>();
        int cursor = 0;
        foreach (Match match in Regex.Matches(merged, Regex.Escape(prefix) + @"([0-9]+)\uE001", RegexOptions.CultureInvariant))
        {
            if (cursor < match.Index) parts.Add(new AiTextPart(Guid.NewGuid(), merged[cursor..match.Index]));
            parts.Add(images[int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)]);
            cursor = match.Index + match.Length;
        }
        if (cursor < merged.Length) parts.Add(new AiTextPart(Guid.NewGuid(), merged[cursor..]));
        return new AiPromptDocument(parts);

        string Encode(AiPromptDocument document)
        {
            var output = new System.Text.StringBuilder();
            using var occurrences = ImageOccurrences(document).GetEnumerator();
            foreach (var part in document.Parts)
            {
                if (part is AiTextPart literal) output.Append(literal.Text);
                else if (part is AiImagePart image)
                {
                    occurrences.MoveNext();
                    var key = occurrences.Current;
                    if (removedManually.Contains(key)) continue;
                    if (!tokens.TryGetValue(key, out string? token))
                    {
                        token = prefix + images.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\uE001";
                        tokens.Add(key, token);
                        images.Add(image);
                    }
                    output.Append(token);
                }
            }
            return output.ToString();
        }
    }

    private static IEnumerable<ImageOccurrence> ImageOccurrences(AiPromptDocument document)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var image in document.Parts.OfType<AiImagePart>())
        {
            int occurrence = counts.GetValueOrDefault(image.SlotId);
            counts[image.SlotId] = occurrence + 1;
            yield return new ImageOccurrence(image.SlotId, occurrence);
        }
    }

    private readonly record struct ImageOccurrence(string SlotId, int Occurrence);

    private static string MergeText(string edited, string before, string after, IReadOnlyList<string> retainedTokens)
    {
        if (before == after) return edited;
        if (edited == before) return after;

        var oldLines = Lines().Matches(before).Select(x => x.Value).ToArray();
        var newLines = Lines().Matches(after).Select(x => x.Value).ToArray();
        var newPositions = newLines.Select((line, index) => (line, index))
            .GroupBy(x => x.line, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Select(y => y.index).ToArray(), StringComparer.Ordinal);
        var changes = new List<(int OldStart, int OldEnd, int NewStart, int NewEnd)>();
        int oldStart = 0, newStart = 0;
        for (int i = 0; i < oldLines.Length; i++)
        {
            if (!newPositions.TryGetValue(oldLines[i], out var indices)) continue;
            int position = Array.BinarySearch(indices, newStart);
            if (position < 0) position = ~position;
            if (position == indices.Length) continue;
            int j = indices[position];
            if (i != oldStart || j != newStart) changes.Add((oldStart, i, newStart, j));
            oldStart = i + 1;
            newStart = j + 1;
        }
        if (oldStart != oldLines.Length || newStart != newLines.Length)
            changes.Add((oldStart, oldLines.Length, newStart, newLines.Length));

        // Work backwards so subsequent edits cannot become anchors for earlier edits.
        foreach (var (oldFrom, oldTo, newFrom, newTo) in changes.AsEnumerable().Reverse())
        {
            string oldText = string.Concat(oldLines[oldFrom..oldTo]);
            string newText = string.Concat(newLines[newFrom..newTo]);
            int preceding = oldFrom > 0 ? FindLine(edited, oldLines[oldFrom - 1], 0) : -1;
            int start = preceding < 0 ? 0 : preceding + oldLines[oldFrom - 1].Length;
            int next = oldTo < oldLines.Length ? FindLine(edited, oldLines[oldTo], start) : -1;
            int limit = next < 0 ? edited.Length : next;
            int match = oldText.Length == 0 ? -1 : FindLine(edited, oldText, start);
            if (match >= 0 && match + oldText.Length <= limit && EndsAtLineBoundary(edited, oldText, match))
            {
                edited = edited.Remove(match, oldText.Length).Insert(match, newText);
                continue;
            }

            // Retain hand-written text before or after an identifiable generated line.
            string oldBody = oldText.TrimEnd('\r', '\n');
            if (oldBody.Length != 0 && oldBody.IndexOfAny(['\r', '\n']) < 0 &&
                (match = FindUniqueInline(edited, oldBody, start, limit)) >= 0)
            {
                edited = edited.Remove(match, oldBody.Length).Insert(match, newText.TrimEnd('\r', '\n'));
                continue;
            }

            if (newText.Length == 0) continue;
            // If the reference was moved by hand, keep it at its current position.
            foreach (string token in retainedTokens)
                if (newText.Contains(token, StringComparison.Ordinal) && edited.Contains(token, StringComparison.Ordinal))
                    newText = newText.Replace(token, "", StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(newText)) continue;
            int existing = FindLine(edited, newText, start);
            if (existing >= 0 && existing + newText.Length <= limit && EndsAtLineBoundary(edited, newText, existing)) continue;
            // The generated line was replaced or there was no previous value. Keep existing
            // manual text and insert the new generated line next to the nearest surviving anchor.
            int insertAt = next >= start ? next : preceding >= 0 ? start : edited.Length;
            string separator = newText.Contains("\r\n", StringComparison.Ordinal) || before.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            if (insertAt > 0 && edited[insertAt - 1] is not ('\r' or '\n')) newText = separator + newText;
            if (insertAt < edited.Length && newText[^1] is not ('\r' or '\n') && edited[insertAt] is not ('\r' or '\n')) newText += separator;
            edited = edited.Insert(insertAt, newText);
        }
        return edited;
    }

    private static int FindLine(string text, string value, int start)
    {
        int index = text.IndexOf(value, start, StringComparison.Ordinal);
        while (index > 0 && text[index - 1] is not ('\r' or '\n'))
            index = text.IndexOf(value, index + 1, StringComparison.Ordinal);
        return index;
    }

    private static bool EndsAtLineBoundary(string text, string matched, int start) =>
        matched[^1] is '\r' or '\n' || start + matched.Length == text.Length || text[start + matched.Length] is '\r' or '\n';

    private static int FindUniqueInline(string text, string value, int start, int limit)
    {
        int match = -1;
        for (int index = text.IndexOf(value, start, StringComparison.Ordinal);
             index >= 0 && index + value.Length <= limit;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            int end = index + value.Length;
            if (end < text.Length && char.IsLetterOrDigit(text[end])) continue;
            if (match >= 0) return -1;
            match = index;
        }
        return match;
    }
}
