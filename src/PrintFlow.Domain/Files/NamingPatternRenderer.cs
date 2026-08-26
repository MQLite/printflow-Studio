using System.Globalization;
using System.Text;
using PrintFlow.Domain.Results;

namespace PrintFlow.Domain.Files;

/// <summary>
/// One substitution the renderer will honour: the exact text between the braces, and the
/// value that text stands for.
/// </summary>
/// <remarks>
/// <c>Spelling</c> is matched ordinally and in full. It is not a name plus a format specifier
/// the renderer interprets — <c>Sequence</c> and <c>Sequence:00</c> are two separate,
/// separately supplied spellings, because a renderer that parsed format specifiers would be
/// the general template facility this type exists to avoid.
/// </remarks>
public readonly record struct NamingToken(string Spelling, string Value);

/// <summary>
/// The single authority that turns a preset naming pattern into text, honouring exactly the
/// named tokens its caller supplies and failing closed on everything else
/// (Epic 11300 Final Gate naming-contract fix §4, §6).
/// </summary>
/// <remarks>
/// The accepted workstation manifest has always written its naming patterns with named tokens
/// — <c>{Name}_HD.png</c>, <c>{Name}_CUTOUT.png</c>, <c>{Name}_{SizeMm}mm_CMYK_W.tif</c>,
/// <c>_{Sequence:00}</c>. Those patterns were previously handed straight to
/// <c>string.Format</c>, whose composite-format language reads <c>{Name}</c> as a malformed
/// index and throws <see cref="FormatException"/>. In Fake-mode Background Removal that
/// exception escaped through the view model and terminated the WPF process before CUTOUT
/// review could be reached (R2 Final Gate).
///
/// Two rules keep that from recurring, and keep this from growing into something larger:
/// <list type="bullet">
///   <item>the token vocabulary is closed and supplied per call, so a pattern the caller has
///         no value for is a structured failure rather than a guess or a crash;</item>
///   <item>the rendered text is checked against what a Windows file name may contain, so a
///         pattern can never introduce a separator, a drive qualifier, or a traversal —
///         this renders names, never paths (§7).</item>
/// </list>
/// Failures are <see cref="FailureCode.PreconditionNotMet"/>: the pattern reached the renderer
/// from a hash-verified preset, so an unrenderable one is a configuration defect the operator
/// must be told about through the ordinary failure surface, not an exception to be caught.
/// </remarks>
public static class NamingPatternRenderer
{
    /// <summary>The established output name supplied by PrintFlow — <c>{Name}</c>.</summary>
    public const string NameSpelling = "Name";

    /// <summary>The target width in whole millimetres — <c>{SizeMm}</c>.</summary>
    public const string SizeMmSpelling = "SizeMm";

    /// <summary>The collision sequence, unpadded — <c>{Sequence}</c>.</summary>
    public const string SequenceSpelling = "Sequence";

    /// <summary>The collision sequence, zero-padded to two digits — <c>{Sequence:00}</c>.</summary>
    public const string PaddedSequenceSpelling = "Sequence:00";

    /// <summary>Characters Windows forbids in a file name (the set <c>OutputName</c> enforces).</summary>
    private static readonly char[] ForbiddenCharacters =
        ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary><c>{Name}</c> bound to the session's established output name.</summary>
    public static NamingToken ForName(OutputName name) => new(NameSpelling, name.Value);

    /// <summary><c>{SizeMm}</c> bound to the target width, rounded to whole millimetres.</summary>
    public static NamingToken ForSizeMm(double widthMm) => new(
        SizeMmSpelling,
        ((int)Math.Round(widthMm, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture));

    /// <summary>Both accepted spellings of the collision sequence.</summary>
    public static NamingToken[] ForSequence(int sequence) =>
    [
        new(SequenceSpelling, sequence.ToString(CultureInfo.InvariantCulture)),
        new(PaddedSequenceSpelling, sequence.ToString("00", CultureInfo.InvariantCulture)),
    ];

    /// <summary>
    /// Renders <paramref name="pattern"/>, substituting only the supplied
    /// <paramref name="tokens"/>.
    /// </summary>
    /// <returns>
    /// The rendered text, or a structured failure naming what about the pattern could not be
    /// honoured. Never throws for a bad pattern — a bad pattern is data, not a programmer error.
    /// </returns>
    public static OperationResult<string> Render(string? pattern, params NamingToken[] tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Unrenderable(pattern, "it is empty", tokens);
        }

        StringBuilder rendered = new(pattern.Length + 16);
        for (int i = 0; i < pattern.Length; i++)
        {
            char current = pattern[i];

            if (current == '}')
            {
                return Unrenderable(pattern, $"it has an unmatched '}}' at position {i}", tokens);
            }

            if (current != '{')
            {
                rendered.Append(current);
                continue;
            }

            int close = pattern.IndexOf('}', i + 1);
            if (close < 0)
            {
                return Unrenderable(pattern, $"it has an unmatched '{{' at position {i}", tokens);
            }

            string spelling = pattern[(i + 1)..close];
            if (spelling.Length == 0 || spelling.Contains('{', StringComparison.Ordinal))
            {
                return Unrenderable(
                    pattern, $"it has a malformed token '{{{spelling}}}' at position {i}", tokens);
            }

            if (!TryResolve(tokens, spelling, out string value))
            {
                return Unrenderable(pattern, $"it uses the unsupported token '{{{spelling}}}'", tokens);
            }

            rendered.Append(value);
            i = close;
        }

        string result = rendered.ToString();

        if (string.IsNullOrWhiteSpace(result))
        {
            return Unrenderable(pattern, "it renders to an empty name", tokens);
        }

        if (result.IndexOfAny(ForbiddenCharacters) >= 0)
        {
            return Unrenderable(
                pattern,
                $"it renders '{result}', which holds a character a Windows file name may not " +
                $"({string.Join(' ', ForbiddenCharacters)}) — naming patterns name files, never paths",
                tokens);
        }

        foreach (char c in result)
        {
            if (char.IsControl(c))
            {
                return Unrenderable(pattern, "it renders a name holding control characters", tokens);
            }
        }

        return OperationResult.Ok(result);
    }

    private static bool TryResolve(NamingToken[] tokens, string spelling, out string value)
    {
        foreach (NamingToken token in tokens)
        {
            if (string.Equals(token.Spelling, spelling, StringComparison.Ordinal))
            {
                value = token.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static OperationResult<string> Unrenderable(string? pattern, string reason, NamingToken[] tokens) =>
        OperationResult.Fail<string>(
            FailureCode.PreconditionNotMet,
            $"The naming pattern '{pattern}' cannot be rendered because {reason}. " +
            $"Supported here: {Supported(tokens)}.");

    private static string Supported(NamingToken[] tokens) =>
        tokens.Length == 0
            ? "no tokens at all"
            : string.Join(", ", tokens.Select(t => $"{{{t.Spelling}}}"));
}
