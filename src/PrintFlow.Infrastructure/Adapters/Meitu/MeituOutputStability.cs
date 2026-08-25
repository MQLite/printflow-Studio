using System.IO;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// One read-only look at a controlled output path (Epic 11300 Part B2B §14, §15).
/// </summary>
/// <param name="Exists">Whether anything is there yet.</param>
/// <param name="ByteLength">Its length, or zero when it does not exist.</param>
/// <param name="CanOpenForRead">
/// Whether it could be opened for reading with sharing that a still-writing producer would
/// refuse.
/// </param>
/// <remarks>
/// Deliberately three separate facts rather than one "ready" flag. A zero-byte file exists; a
/// file of the right length can still be held open by the writer; and a file that can be opened
/// can still be truncated a moment later. Keeping them apart is what lets the rule below state
/// which of them was missing when a wait times out.
/// </remarks>
public readonly record struct MeituOutputObservation(bool Exists, long ByteLength, bool CanOpenForRead);

/// <summary>Takes one read-only look at a path. Never writes, never creates, never deletes.</summary>
public interface IMeituOutputProbe
{
    /// <summary>Observes <paramref name="absolutePath"/> without changing it.</summary>
    MeituOutputObservation Probe(string absolutePath);
}

/// <summary>
/// The real probe, over the file system.
/// </summary>
/// <remarks>
/// <see cref="FileShare.Read"/> is the point of the open attempt: a producer still writing the
/// file holds it in a way that makes this fail, so the attempt distinguishes "the bytes have
/// stopped changing" from "the writer has let go". Neither alone is enough — a slow writer can
/// pause at a stable length — and §15 asks for both.
/// </remarks>
public sealed class FileSystemMeituOutputProbe : IMeituOutputProbe
{
    /// <inheritdoc />
    public MeituOutputObservation Probe(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        FileInfo info = new(absolutePath);
        if (!info.Exists)
        {
            return new MeituOutputObservation(false, 0, false);
        }

        long length;
        try
        {
            length = info.Length;
        }
        catch (IOException)
        {
            return new MeituOutputObservation(true, 0, false);
        }

        bool readable;
        try
        {
            using FileStream stream = new(
                absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, useAsync: false);
            readable = true;
        }
        catch (IOException)
        {
            readable = false;
        }
        catch (UnauthorizedAccessException)
        {
            readable = false;
        }

        return new MeituOutputObservation(true, length, readable);
    }
}

/// <summary>
/// Decides, from a sequence of observations, whether a controlled output has settled
/// (Epic 11300 Part B2B §14, §15).
/// </summary>
/// <remarks>
/// A pure fold over what was observed, rather than a sleep. §15 rules out "wait N seconds and
/// assume" in the plainest terms, and the difference matters in both directions: a fixed sleep
/// that is too short claims a half-written file, and one that is too long makes every attempt
/// slower than it needs to be for no gain in certainty.
///
/// The rule is deliberately small enough to state in a sentence: the same non-zero length,
/// observed <see cref="RequiredConsecutiveObservations"/> times in a row, with the file readable
/// on the last of them. Nothing about elapsed time appears in it — the timing is the caller's
/// polling budget, and a slower machine takes more polls rather than a different answer.
/// </remarks>
public static class MeituOutputStabilityRule
{
    /// <summary>
    /// How many consecutive equal, non-zero observations count as settled.
    /// </summary>
    /// <remarks>
    /// Three rather than two. Two consecutive equal lengths is one interval of quiet, which a
    /// writer flushing in chunks produces routinely; three is two intervals, and at the adapter's
    /// 250 ms poll that is half a second of a file not changing while also being readable.
    /// The live 1.1 MB export was observed complete at the first poll after the confirm, so this
    /// costs nothing in practice and is the margin that makes the claim honest rather than lucky.
    /// </remarks>
    public const int RequiredConsecutiveObservations = 3;

    /// <summary>
    /// Whether <paramref name="recent"/> — most recent last — shows a settled file.
    /// </summary>
    public static bool IsSettled(IReadOnlyList<MeituOutputObservation> recent) =>
        IsSettled(recent, RequiredConsecutiveObservations);

    /// <summary>Whether the last <paramref name="required"/> observations show a settled file.</summary>
    public static bool IsSettled(IReadOnlyList<MeituOutputObservation> recent, int required)
    {
        ArgumentNullException.ThrowIfNull(recent);

        if (required <= 0 || recent.Count < required)
        {
            // Fewer observations than the rule asks for is "not yet", never "good enough".
            return false;
        }

        MeituOutputObservation last = recent[^1];
        if (!last.Exists || last.ByteLength <= 0 || !last.CanOpenForRead)
        {
            return false;
        }

        for (int i = recent.Count - required; i < recent.Count; i++)
        {
            MeituOutputObservation observation = recent[i];
            if (!observation.Exists || observation.ByteLength != last.ByteLength)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Says which part of the rule the last observation failed, for the timeout message.
    /// </summary>
    public static string DescribeUnsettled(IReadOnlyList<MeituOutputObservation> recent)
    {
        ArgumentNullException.ThrowIfNull(recent);

        if (recent.Count == 0)
        {
            return "the path was never observed";
        }

        MeituOutputObservation last = recent[^1];
        if (!last.Exists)
        {
            return "no file ever appeared at the controlled path";
        }

        if (last.ByteLength <= 0)
        {
            return "the file appeared but stayed empty";
        }

        return !last.CanOpenForRead
            ? "the file could not be opened for reading, so something still holds it"
            : "the file's length was still changing";
    }
}
