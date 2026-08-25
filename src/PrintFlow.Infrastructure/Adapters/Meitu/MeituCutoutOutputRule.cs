using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>The path and file contract for a production Background Removal cutout.</summary>
public static class MeituCutoutOutputRule
{
    private const string RequiredSuffix = "_CUTOUT.png";

    /// <summary>Requires a new controlled CUTOUT sibling, before Meitu is touched.</summary>
    public static OperationResult<Unit> ValidateDestination(
        WorkspaceFileRef input, WorkspaceFileRef output)
    {
        if (!output.FileName.EndsWith(RequiredSuffix, StringComparison.Ordinal) ||
            output.FileName.Length <= RequiredSuffix.Length)
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.PreconditionNotMet,
                $"Background Removal output must use the controlled '{{Name}}{RequiredSuffix}' name; " +
                $"'{output.FileName}' does not. Nothing was invoked.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["expectedPattern"] = $"{{Name}}{RequiredSuffix}",
                    ["actualFileName"] = output.FileName,
                    ["exportInvoked"] = "false",
                }));
        }

        if (!string.Equals(ParentOf(input), ParentOf(output), StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.PreconditionNotMet,
                "Background Removal input and output must be different managed Working references " +
                "in the same producing Attempt directory. Nothing was invoked.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["input"] = input.RelativePath,
                    ["expectedOutput"] = output.RelativePath,
                    ["exportInvoked"] = "false",
                }));
        }

        return OperationResult.Ok();
    }

    /// <summary>Requires a real PNG, unchanged canvas dimensions and meaningful alpha.</summary>
    public static OperationResult<Unit> Validate(
        FileFacts source,
        FileFacts output,
        MeituTransparencyFacts transparency)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(transparency);

        if (output.ByteLength <= 0)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.OutputUnreadable,
                "The exported cutout is empty. No AdapterOutput may be produced from it.");
        }

        if (output.Format != ImageFormat.Png)
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.OutputUnreadable,
                $"The exported cutout's content is {output.Format}, not Png. The extension alone " +
                "does not establish the format.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["expectedFormat"] = ImageFormat.Png.ToString(),
                    ["actualFormat"] = output.Format.ToString(),
                }));
        }

        if (!source.HasPixelDimensions || !output.HasPixelDimensions)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.OutputUnreadable,
                "Source and cutout pixel dimensions must both be readable before the canvas can be compared.");
        }

        if (output.PixelWidth != source.PixelWidth || output.PixelHeight != source.PixelHeight)
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.OutputValidationFailed,
                $"The cutout canvas is {output.PixelWidth}x{output.PixelHeight}, but the source is " +
                $"{source.PixelWidth}x{source.PixelHeight}. Background Removal must preserve the " +
                "observed canvas dimensions.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["sourcePixels"] = $"{source.PixelWidth}x{source.PixelHeight}",
                    ["outputPixels"] = $"{output.PixelWidth}x{output.PixelHeight}",
                }));
        }

        return MeituTransparencyRule.Validate(transparency);
    }

    private static string ParentOf(WorkspaceFileRef file)
    {
        int slash = file.RelativePath.LastIndexOf('/');
        return slash < 0 ? string.Empty : file.RelativePath[..slash];
    }
}
