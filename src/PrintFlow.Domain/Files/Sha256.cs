using System.Diagnostics.CodeAnalysis;

namespace PrintFlow.Domain.Files;

/// <summary>
/// A SHA-256 digest in canonical uppercase hexadecimal.
/// </summary>
/// <remarks>
/// Every approval in PrintFlow is bound to one of these (MVP design invariants 2 and 3).
/// The type exists so a hash can never be confused with a path, a name, or another hash
/// algorithm's output, and so comparison is always case-correct.
/// </remarks>
public readonly record struct Sha256
{
    public const int HexLength = 64;

    private Sha256(string value) => Value = value;

    /// <summary>64 uppercase hexadecimal characters.</summary>
    public string Value { get; }

    /// <summary>First 12 characters, for logs and compact UI. Never used for comparison.</summary>
    public string ShortForm => Value.Length >= 12 ? Value[..12] : Value;

    public static Sha256 Parse(string value) =>
        TryParse(value, out Sha256 hash)
            ? hash
            : throw new ArgumentException(
                $"'{value}' is not a 64-character hexadecimal SHA-256 digest.", nameof(value));

    public static bool TryParse(string? value, out Sha256 hash)
    {
        hash = default;
        if (value is null || value.Length != HexLength)
        {
            return false;
        }

        foreach (char c in value)
        {
            bool isHex = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!isHex)
            {
                return false;
            }
        }

        hash = new Sha256(value.ToUpperInvariant());
        return true;
    }

    public static Sha256 FromBytes(ReadOnlySpan<byte> digest)
    {
        if (digest.Length != 32)
        {
            throw new ArgumentException(
                $"A SHA-256 digest is 32 bytes; got {digest.Length}.", nameof(digest));
        }

        return new Sha256(Convert.ToHexString(digest));
    }

    [SuppressMessage("Design", "CA1062:Validate arguments of public methods",
        Justification = "Struct value; Value is never null once constructed through the factories.")]
    public override string ToString() => Value ?? string.Empty;
}
