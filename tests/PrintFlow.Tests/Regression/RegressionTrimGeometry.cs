using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Trimming;

namespace PrintFlow.Tests.Regression;

/// <summary>
/// The <c>printflow-regression-v3</c> fine-hair trim contract: the Trim output is exactly the
/// crop that the actual pre-Trim cutout and the attempt's recorded margin require.
/// </summary>
/// <remarks>
/// <b>Why v3 exists.</b> v2 required the trim to shrink the canvas unconditionally. The actual Meitu
/// cutouts of FIX-FINE-HAIR-001 keep alpha &gt; 0 on every edge, so the correct crop is the whole
/// canvas and that requirement could not be met without altering pixels. v3 is a deliberately
/// versioned expectation change, not a reinterpretation of the v2 property, which v1/v2 runs keep.
/// <para>
/// <b>Independent of the Product's crop calculation.</b> The expected rectangle is recomputed here
/// from the decoded input with a test-side scan and margin/clamp arithmetic; the Product's
/// <see cref="TrimGeometry"/> is only ever the thing compared, never an operand of the expectation.
/// A full-canvas output therefore passes only when the scan says the full canvas is required, a
/// no-op copy fails whenever a removable border exists, and matching dimensions alone never pass.
/// </para>
/// <para>
/// <b>Sample representation.</b> Both files are decoded by WIC with the stored pixel format
/// preserved and colour management ignored, and compared as straight (non-premultiplied) 8-bit
/// BGRA samples, which is what an 8-bit RGBA PNG decodes to. Any other stored format is refused
/// rather than converted: a conversion could hide an alpha or colour difference. No tolerance,
/// premultiplication or resampling is applied, and compressed-file hashes are used for identity
/// only, never for pixel equality.
/// </para>
/// <para>
/// It does not decide whether alpha-bearing edge pixels are desirable foreground. Hair retention and
/// background removal remain the Operator's FINE-HAIR-VISUAL-001 decision.
/// </para>
/// </remarks>
internal static class RegressionTrimGeometry
{
    /// <summary>The set whose fine-hair manifest carries this contract.</summary>
    public const string SetId = "printflow-regression-v3";

    /// <summary>The manifest property and the assertion name; one closed name for one contract.</summary>
    public const string AssertionName = "trimMatchesAlphaBoundsAndMargins";

    /// <summary>Verifies the latest successful Trim attempt of a session against its own files.</summary>
    /// <param name="asset">The loaded manifest; it must unambiguously declare the v3 contract.</param>
    /// <param name="approvedCutout">The cutout digest the caller approved before Trim.</param>
    /// <param name="trimmedArtefact">The Trim artefact digest the caller goes on to approve and export.</param>
    /// <param name="attempts">The session's persisted attempts.</param>
    /// <param name="revisions">The session's persisted Revisions.</param>
    /// <param name="resolve">Resolves a Revision's workspace file to an absolute path.</param>
    public static RegressionAssertion Verify(
        RegressionAssetManifest asset,
        Sha256 approvedCutout,
        Sha256 trimmedArtefact,
        IReadOnlyList<ProcessingAttempt> attempts,
        IReadOnlyList<Revision> revisions,
        Func<WorkspaceFileRef, string> resolve)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(resolve);

        if (!string.Equals(asset.SetId, SetId, StringComparison.Ordinal) ||
            asset.TrimMatchesAlphaBoundsAndMargins.Value is not true ||
            asset.TrimBoundsStrictlyInsideCanvas.Present)
        {
            return Fail("The loaded fine-hair manifest does not carry the unambiguous v3 " +
                        "trimMatchesAlphaBoundsAndMargins=true expectation.");
        }

        ProcessingAttempt? attempt = attempts.LastOrDefault(
            a => a.Operation == OperationKind.Trim && a.Status == AttemptStatus.Succeeded);
        if (attempt is null)
        {
            return Fail("No successful Trim attempt was recorded, so there is no crop to verify.");
        }

        if (attempt.TrimParameters is not { } margin)
        {
            return Fail($"Trim attempt {attempt.Id} recorded no margin; a margin is never assumed.");
        }

        if (attempt.TrimGeometry is not { } persisted)
        {
            return Fail($"Trim attempt {attempt.Id} persisted no ContentBounds/AppliedBounds.");
        }

        Revision? input = attempt.InputRevisionId is { } inputId
            ? revisions.SingleOrDefault(r => r.Id == inputId)
            : null;
        Revision? output = attempt.OutputRevisionId is { } outputId
            ? revisions.SingleOrDefault(r => r.Id == outputId)
            : null;
        if (input is null || output is null)
        {
            return Fail($"Trim attempt {attempt.Id} does not name both a recorded input Revision " +
                        $"({attempt.InputRevisionId?.ToString() ?? "(none)"}) and a recorded output " +
                        $"Revision ({attempt.OutputRevisionId?.ToString() ?? "(none)"}).");
        }

        if (output.Operation != OperationKind.Trim || output.SourceRevisionId != input.Id)
        {
            return Fail($"Output Revision {output.Id} is {output.Operation} from " +
                        $"{output.SourceRevisionId?.ToString() ?? "(no source)"}, not a Trim of input {input.Id}.");
        }

        // The verified pair must be the pair the caller approves and exports, not merely the
        // latest attempt's: otherwise the checked file and the promoted file could differ.
        if (!input.Sha256.Equals(approvedCutout) || !output.Sha256.Equals(trimmedArtefact))
        {
            return Fail($"Trim attempt {attempt.Id} used input {input.Sha256} and produced {output.Sha256}; " +
                        $"the caller approved cutout {approvedCutout} and holds Trim artefact {trimmedArtefact}.");
        }

        if (!TryRead(resolve(input.File), input, "input", out byte[]? inputBytes, out string? inputSha, out string? problem) ||
            !TryRead(resolve(output.File), output, "output", out byte[]? outputBytes, out string? outputSha, out problem))
        {
            return Fail(problem!);
        }

        if (Decode(inputBytes!, "input", out problem) is not { } source ||
            Decode(outputBytes!, "output", out problem) is not { } produced)
        {
            return Fail(problem!);
        }

        StringBuilder detail = new();
        detail.Append(
            $"Attempt {attempt.Id} ({attempt.AdapterId}). Input Revision {input.Id} sha256 {inputSha} " +
            $"decoded {source.Width}x{source.Height} Bgra32; output Revision {output.Id} sha256 {outputSha} " +
            $"decoded {produced.Width}x{produced.Height} Bgra32. Recorded margin {margin}. ");

        // The independent expectation: every pixel with alpha > 0, in input coordinates.
        Edges? scanned = AlphaExtent(source);
        if (scanned is not { } content)
        {
            detail.Append("The decoded input has no pixel with alpha > 0, so no crop can be verified.");
            return Fail(detail.ToString());
        }

        Edges applied = new(
            (int)Math.Max(0L, (long)content.Left - margin.Left),
            (int)Math.Max(0L, (long)content.Top - margin.Top),
            (int)Math.Min(source.Width, (long)content.Right + margin.Right),
            (int)Math.Min(source.Height, (long)content.Bottom + margin.Bottom));

        Edges persistedContent = Edges.Of(persisted.ContentBounds);
        Edges persistedApplied = Edges.Of(persisted.AppliedBounds);
        bool contentMatches = persistedContent == content;
        bool appliedMatches = persistedApplied == applied;
        bool sizeMatches = produced.Width == applied.Width && produced.Height == applied.Height;
        bool factsMatch = output.Facts.PixelWidth == produced.Width && output.Facts.PixelHeight == produced.Height;
        string? pixelDifference = sizeMatches ? FirstDifference(source, produced, applied) : null;
        bool pixelsMatch = sizeMatches && pixelDifference is null;

        detail.Append(
            $"Independent alpha>0 content {content}; persisted ContentBounds {persistedContent} " +
            $"{(contentMatches ? "match" : "MISMATCH")}. Expected applied (content + margin, clamped) {applied}; " +
            $"persisted AppliedBounds {persistedApplied} {(appliedMatches ? "match" : "MISMATCH")}. " +
            $"Output size {produced.Width}x{produced.Height} vs expected {applied.Width}x{applied.Height} " +
            $"{(sizeMatches ? "match" : "MISMATCH")}; Revision facts " +
            $"{output.Facts.PixelWidth?.ToString(CultureInfo.InvariantCulture) ?? "(missing)"}x" +
            $"{output.Facts.PixelHeight?.ToString(CultureInfo.InvariantCulture) ?? "(missing)"} " +
            $"{(factsMatch ? "match" : "MISMATCH")}. Output pixels equal input region {applied}: " +
            $"{(pixelsMatch ? "yes" : sizeMatches ? "NO, " + pixelDifference : "not compared (size differs)")}. " +
            Extent(source, content, applied, margin));

        return new RegressionAssertion(AssertionName,
            contentMatches && appliedMatches && sizeMatches && factsMatch && pixelsMatch,
            detail.ToString());
    }

    private static RegressionAssertion Fail(string detail) => new(AssertionName, false, detail);

    private static bool TryRead(
        string path, Revision revision, string role,
        out byte[]? bytes, out string? sha, out string? problem)
    {
        bytes = null;
        sha = null;
        problem = null;

        if (!File.Exists(path))
        {
            problem = $"The {role} Revision {revision.Id} file '{path}' does not exist.";
            return false;
        }

        bytes = File.ReadAllBytes(path);
        sha = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(sha, revision.Sha256.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            problem = $"The {role} file '{path}' hashes to {sha}; Revision {revision.Id} records " +
                      $"{revision.Sha256}. These are not the Revision's bytes.";
            return false;
        }

        return true;
    }

    private static Surface? Decode(byte[] bytes, string role, out string? problem)
    {
        problem = null;
        try
        {
            using MemoryStream stream = new(bytes, writable: false);
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count != 1)
            {
                problem = $"The {role} decodes to {decoder.Frames.Count} frames; exactly one is required.";
                return null;
            }

            BitmapFrame frame = decoder.Frames[0];
            if (frame.Format != PixelFormats.Bgra32)
            {
                problem = $"The {role} decodes as {frame.Format}. This check compares straight 8-bit " +
                          "BGRA samples only and does not convert another format.";
                return null;
            }

            byte[] pixels = new byte[checked(frame.PixelWidth * frame.PixelHeight * 4)];
            frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
            return new Surface(frame.PixelWidth, frame.PixelHeight, pixels);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or
                                       ArgumentException or OverflowException or
                                       InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            problem = $"The {role} could not be decoded: {ex.Message}";
            return null;
        }
    }

    /// <summary>The smallest rectangle holding every pixel whose alpha byte is not zero.</summary>
    private static Edges? AlphaExtent(Surface surface)
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (int y = 0; y < surface.Height; y++)
        {
            int row = y * surface.Width;
            for (int x = 0; x < surface.Width; x++)
            {
                if (surface.Bgra[((row + x) * 4) + 3] != 0)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = y;
                }
            }
        }

        return right < 0 ? null : new Edges(left, top, right + 1, bottom + 1);
    }

    /// <summary>The first sample of the output that is not the input's sample at the same crop position.</summary>
    private static string? FirstDifference(Surface source, Surface produced, Edges region)
    {
        const string channels = "BGRA";
        for (int y = 0; y < produced.Height; y++)
        {
            int from = (((region.Top + y) * source.Width) + region.Left) * 4;
            int to = y * produced.Width * 4;
            for (int i = 0; i < produced.Width * 4; i++)
            {
                byte expected = source.Bgra[from + i];
                byte actual = produced.Bgra[to + i];
                if (expected != actual)
                {
                    return string.Create(CultureInfo.InvariantCulture,
                        $"first difference at output ({i / 4},{y}) channel {channels[i % 4]}: " +
                        $"expected {expected}, found {actual}");
                }
            }
        }

        return null;
    }

    private static string Extent(Surface source, Edges content, Edges applied, TrimMargin margin)
    {
        if (applied.Left == 0 && applied.Top == 0 && applied.Right == source.Width && applied.Bottom == source.Height)
        {
            List<string> reached = [];
            List<string> clamped = [];
            Classify("top", content.Top == 0, margin.Top > 0);
            Classify("right", content.Right == source.Width, margin.Right > 0);
            Classify("bottom", content.Bottom == source.Height, margin.Bottom > 0);
            Classify("left", content.Left == 0, margin.Left > 0);

            return $"Full {source.Width}x{source.Height} extent retained because alpha>0 content reaches " +
                   $"{(reached.Count == 0 ? "no edge" : string.Join("/", reached))}" +
                   (clamped.Count == 0 ? "." : $" and the recorded margin reaches the canvas on {string.Join("/", clamped)}.");

            void Classify(string edge, bool touches, bool hasMargin)
            {
                if (touches)
                {
                    reached.Add(edge);
                }
                else if (hasMargin)
                {
                    clamped.Add(edge);
                }
            }
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"Cropped inside the {source.Width}x{source.Height} canvas; removed border top {applied.Top}, " +
            $"right {source.Width - applied.Right}, bottom {source.Height - applied.Bottom}, left {applied.Left} px.");
    }

    private sealed record Surface(int Width, int Height, byte[] Bgra);

    /// <summary>Half-open <c>[left, top -> right, bottom)</c> edges, the Product's own convention.</summary>
    private readonly record struct Edges(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left;

        public int Height => Bottom - Top;

        public static Edges Of(TrimBounds bounds) =>
            new(bounds.Left, bounds.Top, bounds.RightExclusive, bounds.BottomExclusive);

        public override string ToString() => string.Create(CultureInfo.InvariantCulture,
            $"[{Left},{Top} -> {Right},{Bottom}) {Width}x{Height}");
    }
}
