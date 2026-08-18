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

    public override string ToString() => Value ?? string.Empty;

    /// <summary>Validation outcome for <see cref="OutputName"/>.</summary>
    public readonly record struct NameValidation(bool IsValid, OutputName Name, string Error)
    {
        public static NameValidation Valid(OutputName name) => new(true, name, string.Empty);

        public static NameValidation Invalid(string error) => new(false, default, error);
    }
}
