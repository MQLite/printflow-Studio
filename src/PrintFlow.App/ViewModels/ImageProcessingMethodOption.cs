using PrintFlow.App.Resources;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

public sealed record ImageProcessingMethodOption(ImageProcessingMethod Method, string Label, string Description, bool IsAvailable)
{
    internal static IReadOnlyList<ImageProcessingMethodOption> ForStep(
        StepKind step, ISessionService sessions, bool includeCombined = false)
    {
        ImageProcessingMethod[] methods = step switch
        {
            StepKind.Enhancement => [ImageProcessingMethod.MeituEnhancement, ImageProcessingMethod.LightSharpen,
                ImageProcessingMethod.AiUpscale, ImageProcessingMethod.OpenAiRestore, ImageProcessingMethod.SkipEnhancement],
            StepKind.BackgroundRemoval => [ImageProcessingMethod.MeituBackgroundRemoval, ImageProcessingMethod.BiRefNet,
                ImageProcessingMethod.OpenAiCutout, ImageProcessingMethod.KeepBackground],
            StepKind.Trim => [ImageProcessingMethod.AlphaTrim, ImageProcessingMethod.ManualCrop, ImageProcessingMethod.KeepCanvas],
            _ => [],
        };
        if (includeCombined && step == StepKind.Enhancement)
            methods = [.. methods, ImageProcessingMethod.OpenAiCombined];

        return methods.Select(method =>
        {
            OperationFailure? unavailable = sessions.GetImageProcessingUnavailable(method);
            string description = AvailabilityHint(method);
            return new ImageProcessingMethodOption(method, Name(method),
                unavailable is null ? description : $"{description} {UnavailableHint(unavailable)}",
                unavailable is null);
        }).ToArray();
    }

    internal static string Name(ImageProcessingMethod method) => Strings.Resolve("ImageProcessing_Method_" + (method switch
    {
        ImageProcessingMethod.MeituEnhancement or ImageProcessingMethod.MeituBackgroundRemoval => "Meitu",
        _ => method.ToString(),
    }));

    internal static string UnavailableHint(OperationFailure failure)
    {
        if (failure.MessageKey is { } key && Strings.Resolve(key) is { } translated && translated != key)
            return translated;
        return Strings.Resolve("ImageProcessing_DependencyUnavailable");
    }

    private static string AvailabilityHint(ImageProcessingMethod method) => Strings.Resolve(method switch
    {
        ImageProcessingMethod.MeituEnhancement or ImageProcessingMethod.MeituBackgroundRemoval => "ImageProcessing_MeituHint",
        ImageProcessingMethod.AlphaTrim => "ImageProcessing_AlphaHint",
        ImageProcessingMethod.SkipEnhancement or ImageProcessingMethod.KeepBackground => "ImageProcessing_SkipHint",
        ImageProcessingMethod.ManualCrop => "ImageProcessing_ManualCropHint",
        ImageProcessingMethod.KeepCanvas => "ImageProcessing_KeepCanvasHint",
        ImageProcessingMethod.LightSharpen => "ImageProcessing_SharpenHint",
        ImageProcessingMethod.AiUpscale => "ImageProcessing_UpscaleHint",
        ImageProcessingMethod.BiRefNet => "ImageProcessing_BiRefNetHint",
        ImageProcessingMethod.OpenAiCutout => "ImageProcessing_OpenAiCutoutHint",
        ImageProcessingMethod.OpenAiCombined => "ImageProcessing_CombinedHint",
        ImageProcessingMethod.OpenAiRestore => "ImageProcessing_CloudHint",
        _ => "ImageProcessing_Ready",
    });
}
