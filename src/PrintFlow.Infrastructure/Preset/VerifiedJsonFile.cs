using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Preset;

/// <summary>
/// Reads a signed baseline document read-only and refuses it unless its bytes hash to the
/// digest the caller expected.
/// </summary>
/// <remarks>
/// Extracted so that the workstation preset and the per-application evidence files it vouches
/// for are loaded on identical terms: opened read-only with no write share, never rewritten,
/// hashed in full, and failed closed on any mismatch. Epic 11000 marked these files read-only
/// on disk, but a file attribute is a convenience — the SHA-256 is the authority.
/// </remarks>
internal static class VerifiedJsonFile
{
    /// <summary>Reads and hash-verifies <paramref name="absolutePath"/>.</summary>
    /// <param name="absolutePath">The signed document.</param>
    /// <param name="expected">The digest recorded for it by an already-trusted authority.</param>
    /// <param name="description">How the document is named in a failure message.</param>
    internal static OperationResult<JsonDocument> Read(
        string absolutePath, Sha256 expected, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (!File.Exists(absolutePath))
        {
            return OperationResult.Fail<JsonDocument>(
                FailureCode.EnvironmentNotVerified, $"{description} not found at '{absolutePath}'.");
        }

        byte[] bytes;
        try
        {
            using FileStream stream = new(
                absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1 << 16, useAsync: false);
            using MemoryStream buffer = new(checked((int)Math.Min(stream.Length, int.MaxValue)));
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail<JsonDocument>(
                FailureCode.EnvironmentNotVerified, $"{description} could not be read: {ex.Message}");
        }

        Sha256 actual = Sha256.FromBytes(SHA256.HashData(bytes));
        if (!actual.Equals(expected))
        {
            return OperationResult.Fail<JsonDocument>(
                FailureCode.PresetHashMismatch,
                $"{description} hash mismatch: expected {expected}, computed {actual}.");
        }

        try
        {
            return OperationResult.Ok(JsonDocument.Parse(bytes));
        }
        catch (JsonException ex)
        {
            return OperationResult.Fail<JsonDocument>(
                FailureCode.EnvironmentNotVerified, $"{description} is not valid JSON: {ex.Message}");
        }
    }
}
