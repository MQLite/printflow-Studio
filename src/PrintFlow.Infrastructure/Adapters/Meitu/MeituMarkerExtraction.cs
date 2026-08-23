using System.Collections.Immutable;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// Turns Epic 11000's human-written marker descriptions into the exact labels a UI text
/// snapshot can be compared against (Epic 11300 Part A §2).
/// </summary>
/// <remarks>
/// The signed clean-start evidence records markers as prose — "图片编辑 / PhotoEditor entry",
/// "bottom category navigation and 更多工具 area" — because it was written for a person to read.
/// PrintFlow needs the label, not the sentence.
///
/// Transcribing the labels into a constant in the adapter would have been simpler and worse: the
/// list would then be a second copy that no hash protects and that silently stops matching the
/// signed evidence the day either is edited. A deterministic extraction keeps the signed file as
/// the single source, at the cost of one rule that needs its own tests — which is what this
/// class is.
/// </remarks>
public static class MeituMarkerExtraction
{
    private static readonly char[] Separators =
        [' ', '\t', '/', ',', ';', '(', ')', '（', '）', '、'];

    /// <summary>
    /// Extracts the distinct CJK-bearing labels from <paramref name="descriptions"/>, ordered
    /// so the result is stable.
    /// </summary>
    /// <remarks>
    /// The rule: split on whitespace and the separators Epic 11000 used, keep the tokens
    /// containing at least one CJK character. English scaffolding ("entry", "area", "logo")
    /// carries none and is dropped, so it can never be matched against a Meitu screen. A token
    /// like "AI变清晰" survives whole, because the Latin prefix is part of the label rather than
    /// a separate word.
    /// </remarks>
    public static ImmutableArray<string> Extract(IEnumerable<string> descriptions)
    {
        ArgumentNullException.ThrowIfNull(descriptions);

        HashSet<string> labels = new(StringComparer.Ordinal);

        foreach (string description in descriptions)
        {
            foreach (string token in description.Split(
                         Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (ContainsCjk(token))
                {
                    labels.Add(token);
                }
            }
        }

        return [.. labels.OrderBy(l => l, StringComparer.Ordinal)];
    }

    private static bool ContainsCjk(string token)
    {
        foreach (char c in token)
        {
            // CJK Unified Ideographs plus extension A: enough for the Simplified Chinese labels
            // Epic 11000 captured, and narrow enough that Latin scaffolding never qualifies.
            if (c is >= '㐀' and <= '鿿')
            {
                return true;
            }
        }

        return false;
    }
}
