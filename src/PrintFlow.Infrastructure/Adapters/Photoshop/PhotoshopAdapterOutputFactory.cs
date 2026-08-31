using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>
/// The one place a C1 <see cref="PhotoshopValidatedTiffCandidate"/> becomes an
/// <see cref="AdapterOutput"/> (Epic 11400 Part C2A §8, §9).
/// </summary>
/// <remarks>
/// It exists as its own type rather than as a private helper on the processor so the C2A
/// boundary is a place rather than a promise: an architecture test asserts that
/// <c>new AdapterOutput</c> appears exactly once in the Photoshop adapter, here, and that the
/// only path to it takes a validated candidate. There is deliberately no overload accepting a
/// path, a save return, a <see cref="PhotoshopW1PreparedDocument"/>, or a candidate that failed
/// validation — those are the fabrications §8 rules out, and none of them is expressible.
/// <para>
/// The candidate is necessary but not sufficient. §9 requires the candidate, the request's
/// reserved destination and the bytes actually on disk to be the same file by every measure
/// PrintFlow has, so all of them are compared here before anything is constructed. A mismatch
/// fails; it never resolves in favour of whichever file Photoshop happened to create.
/// </para>
/// </remarks>
internal static class PhotoshopAdapterOutputFactory
{
    /// <summary>The validation identity recorded in the note, so a stored note stays readable.</summary>
    internal const string ValidationVersion = "photoshop-tiff-validation-c1-v1";

    /// <summary>Turns one validated candidate into workflow output, or refuses.</summary>
    /// <param name="request">
    /// The immutable request built from the producing Attempt. It is the authority for the
    /// reserved destination and the white-underbase branch, and nothing here re-decides either.
    /// </param>
    /// <param name="candidate">The settled, independently validated TIFF.</param>
    /// <param name="workspace">Resolves both references so the paths they name can be compared.</param>
    /// <param name="elapsed">Wall-clock time for the whole composed operation.</param>
    internal static OperationResult<AdapterOutput> Create(
        PhotoshopRequest request,
        PhotoshopValidatedTiffCandidate candidate,
        IWorkspace workspace,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(workspace);

        // The reference the workflow reserved and the reference the candidate validated must be
        // the same value, not merely two paths that resolve alike: area included, because an
        // Approved reference and a Working one naming the same bytes are different promises.
        if (candidate.Tiff != request.ExpectedOutput)
        {
            return Refuse(
                "The validated TIFF candidate does not name the exact reserved workflow output.",
                candidate,
                ("expectedOutput", request.ExpectedOutput.RelativePath),
                ("expectedArea", request.ExpectedOutput.Area.ToString()),
                ("candidateOutput", candidate.Tiff.RelativePath),
                ("candidateArea", candidate.Tiff.Area.ToString()));
        }

        string expectedPath;
        string candidatePath;
        try
        {
            expectedPath = Path.GetFullPath(workspace.ResolveAbsolute(request.ExpectedOutput));
            candidatePath = Path.GetFullPath(workspace.ResolveAbsolute(candidate.Tiff));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Refuse($"The validated TIFF destination could not be resolved: {ex.Message}", candidate);
        }

        if (!string.Equals(expectedPath, candidatePath, StringComparison.OrdinalIgnoreCase))
        {
            return Refuse(
                "The reserved workflow output and the validated candidate resolve to different files.",
                candidate,
                ("expectedPath", expectedPath),
                ("candidatePath", candidatePath));
        }

        // Re-read the final bytes rather than trusting the candidate's own record of them. The
        // candidate was validated some milliseconds ago; this is the last moment before a
        // Revision can be created from it, and so the only moment at which "the file the
        // workflow is about to record" and "the file that was validated" can still be compared.
        FileInfo finalFile = new(expectedPath);
        if (!finalFile.Exists)
        {
            return Refuse(
                "The validated TIFF is no longer present at the reserved workflow destination.",
                candidate, ("expectedPath", expectedPath));
        }

        if (finalFile.Length != candidate.ByteLength)
        {
            return Refuse(
                "The final TIFF byte length does not match the independently validated candidate.",
                candidate,
                ("validatedByteLength", candidate.ByteLength.ToString(CultureInfo.InvariantCulture)),
                ("actualByteLength", finalFile.Length.ToString(CultureInfo.InvariantCulture)));
        }

        Sha256 finalSha;
        try
        {
            using FileStream stream = new(
                expectedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: false);
            finalSha = Sha256.FromBytes(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Refuse($"The final TIFF could not be re-read completely: {ex.Message}", candidate);
        }

        if (!finalSha.Equals(candidate.Sha256))
        {
            return Refuse(
                "The final TIFF hash does not match the independently validated candidate.",
                candidate,
                ("validatedSha256", candidate.Sha256.ToString()),
                ("actualSha256", finalSha.ToString()));
        }

        return OperationResult.Ok(new AdapterOutput(candidate.Tiff, elapsed, Notes(request, candidate)));
    }

    /// <summary>The bounded factual summary the successful Attempt keeps (§16).</summary>
    /// <remarks>
    /// A fixed-shape sentence built from the request's authority and the candidate's validated
    /// facts, and from nothing else. It is deliberately a summary rather than the parser output:
    /// the inspector reads layer records, spot-colour resources and per-sample data, and storing
    /// that would put megabytes of diagnostics in a column a support engineer has to read. What
    /// is kept is the set of facts a later review or support question actually turns on.
    /// <para>
    /// TIFF-specific facts that <c>FileFacts</c> cannot express — the W1 spot channel, the
    /// compression contract, the sample layout — live here rather than being forced into the
    /// Revision's shape (§13).
    /// </para>
    /// </remarks>
    private static string Notes(PhotoshopRequest request, PhotoshopValidatedTiffCandidate candidate)
    {
        ProductionTiffFacts facts = candidate.Facts;
        StringBuilder note = new();

        note.Append(CultureInfo.InvariantCulture,
            $"production Photoshop TIFF; {request.Branch} branch; ");
        note.Append(CultureInfo.InvariantCulture,
            $"{facts.PixelWidth}x{facts.PixelHeight} px @ ");
        note.Append(CultureInfo.InvariantCulture,
            $"{facts.XResolutionDpi.ToString("0.##", CultureInfo.InvariantCulture)}x{facts.YResolutionDpi.ToString("0.##", CultureInfo.InvariantCulture)} dpi; ");
        note.Append(CultureInfo.InvariantCulture,
            $"{facts.ByteOrder}, compression {Compression(facts.Compression)}, ");
        note.Append(CultureInfo.InvariantCulture,
            $"{facts.SamplesPerPixel}x{Depth(facts)}-bit {(facts.PlanarConfiguration == 1 ? "interleaved" : "planar")} separated; ");
        note.Append(CultureInfo.InvariantCulture,
            $"spot {Channels(facts)} (photoshop spot {facts.W1IsPhotoshopSpotChannel}, non-white {facts.W1NonWhiteSampleCount} px); ");
        note.Append(CultureInfo.InvariantCulture,
            $"alpha {facts.HasAlphaOrTransparencySample}, pyramid {facts.HasImagePyramid}, ");
        note.Append(CultureInfo.InvariantCulture,
            $"layers {facts.PhotoshopLayerCount} all-RLE {facts.AllPhotoshopLayerChannelsUseRle}; ");
        note.Append(CultureInfo.InvariantCulture,
            $"{candidate.ByteLength} bytes; sha256 {candidate.Sha256.ShortForm}; ");
        note.Append(CultureInfo.InvariantCulture,
            $"backing unchanged {candidate.BackingWorkingSha256.ShortForm}; ");
        note.Append(CultureInfo.InvariantCulture,
            $"save {candidate.SaveSettings.ImageCompression}/{candidate.SaveSettings.LayerCompression}/{candidate.SaveSettings.ByteOrder}, as copy {candidate.SaveSettings.AsCopy}; ");
        note.Append(CultureInfo.InvariantCulture,
            $"settled over {candidate.SettlingObservations.Length} observations in {candidate.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s; ");
        note.Append(ValidationVersion);

        if (!candidate.ValidationLimitations.IsDefaultOrEmpty)
        {
            note.Append(CultureInfo.InvariantCulture,
                $"; limitations: {string.Join(", ", candidate.ValidationLimitations)}");
        }

        return note.Append('.').ToString();
    }

    private static string Channels(ProductionTiffFacts facts) =>
        facts.ExtraChannelNames.IsDefaultOrEmpty ? "(none)" : string.Join("+", facts.ExtraChannelNames);

    private static string Compression(ushort value) => value switch
    {
        1 => "none",
        5 => "lzw",
        7 => "jpeg",
        8 => "deflate",
        32773 => "packbits",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    private static string Depth(ProductionTiffFacts facts) =>
        facts.BitsPerSample.IsDefaultOrEmpty
            ? "?"
            : facts.BitsPerSample[0].ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Every refusal here states that no workflow output was constructed, because the reader's
    /// next question is always whether a Revision could have been created from this run.
    /// </summary>
    private static OperationResult<AdapterOutput> Refuse(
        string detail, PhotoshopValidatedTiffCandidate candidate, params (string Key, string Value)[] context)
    {
        Dictionary<string, string> entries = new()
        {
            ["adapterOutputConstructed"] = "false",
            ["revisionCreated"] = "false",
            ["retainedWorkingTiff"] = candidate.Tiff.RelativePath,
        };

        foreach ((string key, string value) in context)
        {
            entries[key] = value;
        }

        return OperationResult.Fail<AdapterOutput>(OperationFailure.Create(
            FailureCode.OutputValidationFailed, detail, isRetryable: false, context: entries));
    }
}
