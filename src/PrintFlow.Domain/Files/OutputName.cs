namespace PrintFlow.Domain.Files;

/// <summary>
/// The operator-editable name used for a session's produced files.
/// </summary>
/// <remarks>
/// Defaults to the imported file's stem and may be edited to replace a corrupted or
/// meaningless name. Editing it never renames the operator's source file
/// (MVP design §9.4 and invariant 1).
///
/// This type guarantees only that the stem is non-empty, free of characters Windows
/// forbids, and within the length budget. Collision handling (<c>_02</c>, <c>_03</c>) and
/// reserved device names belong to the workspace naming service in Epic 11100 Task 11107,
/// because only that module can see what already exists on disk.
/// </remarks>
public readonly record struct OutputName
{
    /// <summary>Length cap on the stem; guards Windows path limits with Chinese names (plan risk R9).</summary>
    public const int MaxLength = 80;

    /// <summary>Characters Windows forbids in a file name.</summary>
    private static readonly char[] ForbiddenCharacters =
        ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private OutputName(string value) => Value = value;

    public string Value { get; }

    public static NameValidation Create(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return NameValidation.Invalid("An output name cannot be empty.");
        }

        string trimmed = candidate.Trim().TrimEnd('.', ' ');
        if (trimmed.Length == 0)
        {
            return NameValidation.Invalid("An output name cannot consist only of dots or spaces.");
        }

        if (trimmed.Length > MaxLength)
        {
            return NameValidation.Invalid(
                $"An output name may be at most {MaxLength} characters; got {trimmed.Length}.");
        }

        if (trimmed.IndexOfAny(ForbiddenCharacters) >= 0)
        {
            return NameValidation.Invalid(
                "An output name must not contain any of these characters: " +
                string.Join(' ', ForbiddenCharacters));
        }

        foreach (char c in trimmed)
        {
            if (char.IsControl(c))
            {
                return NameValidation.Invalid("An output name must not contain control characters.");
            }
        }

        return NameValidation.Valid(new OutputName(trimmed));
    }

    /// <summary>Creates a name that is already known to be valid; throws otherwise.</summary>
    public static OutputName Parse(string candidate)
    {
        NameValidation result = Create(candidate);
        return result.IsValid
            ? result.Name
            : throw new ArgumentException(result.Error, nameof(candidate));
    }

    /// <summary>Reserved Windows device names; matched case-insensitively against the whole stem.</summary>
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Sanitises an arbitrary candidate (typically the imported file's stem) into a valid
    /// <see cref="OutputName"/>, never failing (Epic 11100 plan §13.1; task §19).
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="Create"/>, which rejects invalid operator input outright, this always
    /// produces a usable name: it strips what Windows forbids, collapses whitespace, protects
    /// reserved device names, and falls back to <c>Untitled</c> when nothing is left. Non-ASCII
    /// characters, including Chinese, are preserved — sanitisation removes what NTFS forbids,
    /// not what looks unfamiliar.
    /// </remarks>
    public static OutputName Sanitise(string? candidate)
    {
        string working = candidate ?? string.Empty;

        // Strip control characters and characters Windows forbids.
        System.Text.StringBuilder builder = new(working.Length);
        foreach (char c in working)
        {
            if (char.IsControl(c) || Array.IndexOf(ForbiddenCharacters, c) >= 0)
            {
                continue;
            }

            builder.Append(c);
        }

        working = builder.ToString();

        // Collapse runs of whitespace to a single space.
        working = CollapseWhitespace(working);

        working = working.Trim().TrimEnd('.', ' ');

        if (working.Length > MaxLength)
        {
            working = working[..MaxLength].TrimEnd('.', ' ');
        }

        if (working.Length == 0)
        {
            working = "Untitled";
        }

        if (ReservedDeviceNames.Contains(working))
        {
            working = "_" + working;
        }

        // The prefix or truncation above cannot reintroduce a forbidden character or exceed
        // the length budget, so this always succeeds.
        return Parse(working);
    }

    private static string CollapseWhitespace(string value)
    {
        System.Text.StringBuilder builder = new(value.Length);
        bool previousWasSpace = false;
        foreach (char c in value)
        {
            bool isSpace = char.IsWhiteSpace(c);
            if (isSpace)
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                }

                previousWasSpace = true;
                continue;
            }

            builder.Append(c);
            previousWasSpace = false;
        }

        return builder.ToString();
    }

    public override string ToString() => Value ?? string.Empty;

    /// <summary>Validation outcome for <see cref="OutputName"/>.</summary>
    public readonly record struct NameValidation(bool IsValid, OutputName Name, string Error)
    {
        public static NameValidation Valid(OutputName name) => new(true, name, string.Empty);

        public static NameValidation Invalid(string error) => new(false, default, error);
    }
}
