using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// What a file has to be before PrintFlow will call it an Enhancement result
/// (Epic 11300 Part B2B §16, §17, §18, §19).
/// </summary>
/// <remarks>
/// A rule over <see cref="FileFacts"/> rather than over a file, because the reading is
/// <c>IFileInspector</c>'s job and it already does it properly — one pass, hash and metadata
/// from the same bytes, so the two can never describe different content. §16 is explicit that a
/// second image-inspection implementation must not appear here, and none does: this class
/// decides what the existing inspector's answer has to look like.
///
/// What it deliberately does not do is judge the picture. Dimensions and a readable PNG header
/// say the export produced a real image of at least the right size; they say nothing about
/// whether the enhancement improved anything, and §18 is clear that inferring quality from them
/// would be a claim this slice has no evidence for.
/// </remarks>
public static class MeituEnhancementOutputRule
{
    /// <summary>
    /// Decides whether <paramref name="output"/> is an acceptable Enhancement result for
    /// <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The working copy's facts, read before Meitu was given it.</param>
    /// <param name="output">The exported file's facts.</param>
    /// <param name="expectedFormat">The format the signed export evidence required.</param>
    /// <remarks>
    /// The dimension rule is "not smaller in either direction", and the looseness is the point.
    /// Meitu was observed upscaling 320x240 to 1280x960, but that factor belongs to one image
    /// and one set of module settings — the same panel offers 高清 and 保持原尺寸, and
    /// 保持原尺寸 means exactly what it says. Encoding 4x here would reject a legitimate result
    /// the moment an operator or a future Meitu build chose differently, while catching nothing
    /// that "not smaller" does not already catch: a zero, a shrunk output and a thumbnail all
    /// fail both.
    /// </remarks>
    public static OperationResult<Unit> Validate(
        FileFacts source, FileFacts output, ImageFormat expectedFormat)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        if (output.ByteLength <= 0)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.OutputUnreadable,
                "The exported file is empty. No Revision may be created from it.");
        }

        if (output.Format != expectedFormat)
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.OutputUnreadable,
                $"The exported file's content is {output.Format}, not the {expectedFormat} the signed " +
                "export evidence required. The format was read from the file's own bytes, not from its " +
                "name, so a correct extension over the wrong content is caught here.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["expectedFormat"] = expectedFormat.ToString(),
                    ["actualFormat"] = output.Format.ToString(),
                }));
        }

        if (!output.HasPixelDimensions)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.OutputUnreadable,
                "The exported file's pixel dimensions could not be read, so PrintFlow cannot establish " +
                "that it is a usable image. No Revision may be created from it.");
        }

        if (!source.HasPixelDimensions)
        {
            // Refused rather than skipped. "The source could not be measured, so anything the
            // output says is acceptable" is how a shrunk or empty result gets through.
            return OperationResult.Fail<Unit>(
                FailureCode.OutputUnreadable,
                "The working copy's pixel dimensions could not be read, so the exported result cannot be " +
                "compared against the image it was produced from and is not accepted.");
        }

        if (output.PixelWidth < source.PixelWidth || output.PixelHeight < source.PixelHeight)
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.OutputUnreadable,
                $"The exported result is {output.PixelWidth}x{output.PixelHeight}, smaller than the " +
                $"{source.PixelWidth}x{source.PixelHeight} working copy it was produced from. An " +
                "Enhancement that shrinks the image is not a result PrintFlow will hand to review.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["sourcePixels"] = $"{source.PixelWidth}x{source.PixelHeight}",
                    ["outputPixels"] = $"{output.PixelWidth}x{output.PixelHeight}",
                }));
        }

        return OperationResult.Ok();
    }

    /// <summary>
    /// Confirms the working copy Meitu was given is byte-for-byte what it was
    /// (Epic 11300 Part B2B §19).
    /// </summary>
    /// <remarks>
    /// The Save surface carries a 覆盖原图 control that overwrites the input, and no PrintFlow
    /// code path can reach it — but "no code path reaches it" is a claim about the code, and this
    /// is a check on the world. Meitu is an application PrintFlow drives rather than controls,
    /// and the customer's original is protected by the working-copy boundary further up; what is
    /// left to establish here is that the attempt's own input survived its attempt.
    /// </remarks>
    public static OperationResult<Unit> ConfirmSourceUnchanged(
        FileFacts before, FileFacts after, string workingCopyName)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        return before.Sha256.Equals(after.Sha256) && before.ByteLength == after.ByteLength
            ? OperationResult.Ok()
            : OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.OutputUnreadable,
                $"The working copy '{workingCopyName}' PrintFlow handed to Meitu is not the file it was " +
                "before the run. The export is not accepted, because an attempt that modified its own " +
                "input has not produced a result derived from the input it recorded.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["expectedSha256"] = before.Sha256.ToString(),
                    ["actualSha256"] = after.Sha256.ToString(),
                    ["expectedBytes"] = before.ByteLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["actualBytes"] = after.ByteLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
                }));
    }
}
