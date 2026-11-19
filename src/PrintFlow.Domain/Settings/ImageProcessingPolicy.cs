using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrintFlow.Domain.Results;

namespace PrintFlow.Domain.Settings;

/// <summary>Stable persisted method names for the settings, session and attempt records.</summary>
public enum ImageProcessingMethod
{
    MeituEnhancement,
    LightSharpen,
    AiUpscale,
    OpenAiRestore,
    SkipEnhancement,
    MeituBackgroundRemoval,
    BiRefNet,
    OpenAiCutout,
    KeepBackground,
    AlphaTrim,
    ManualCrop,
    KeepCanvas,
    OpenAiCombined,
    ManualResult,
}

public sealed record SessionImageProcessingSelection(
    [property: JsonRequired] ImageProcessingMethod Enhancement,
    [property: JsonRequired] ImageProcessingMethod BackgroundRemoval,
    [property: JsonRequired] ImageProcessingMethod Crop,
    [property: JsonRequired] bool UseOpenAiCombined = false)
{
    public static SessionImageProcessingSelection Compatible { get; } = new(
        ImageProcessingMethod.MeituEnhancement, ImageProcessingMethod.MeituBackgroundRemoval,
        ImageProcessingMethod.AlphaTrim);

    [JsonIgnore]
    public bool HasValidMethods =>
        Enhancement is ImageProcessingMethod.MeituEnhancement or ImageProcessingMethod.LightSharpen
            or ImageProcessingMethod.AiUpscale or ImageProcessingMethod.OpenAiRestore
            or ImageProcessingMethod.SkipEnhancement &&
        BackgroundRemoval is ImageProcessingMethod.MeituBackgroundRemoval or ImageProcessingMethod.BiRefNet
            or ImageProcessingMethod.OpenAiCutout or ImageProcessingMethod.KeepBackground &&
        Crop is ImageProcessingMethod.AlphaTrim or ImageProcessingMethod.ManualCrop or ImageProcessingMethod.KeepCanvas;

    public string Serialize() => JsonSerializer.Serialize(this, ImageProcessingPolicy.JsonOptions);

    public static bool TryParse(string value, [NotNullWhen(true)] out SessionImageProcessingSelection? selection) =>
        ImageProcessingPolicy.TryReadJson(value, out selection) && selection.HasValidMethods;
}

public sealed record ImageProcessingPolicy(
    [property: JsonRequired] SessionImageProcessingSelection Selection,
    [property: JsonRequired] bool IgnoreSmallTrimNoise)
{
    private const int CurrentVersion = 1;
    public const string InvalidDefaultsMessage = "ImageProcessing_InvalidDefaults";
    public const string UnavailableDefaultsMessage = "ImageProcessing_UnavailableDefaults";

    public static bool IsDefaultsFailure(OperationFailure failure) =>
        failure.MessageKey == InvalidDefaultsMessage;

    // Names are persisted rather than enum ordinals. Required fields distinguish a damaged
    // setting from a deliberate selection whose value happens to be the enum's first member.
    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    [JsonRequired]
    public int Version { get; init; } = CurrentVersion;

    public static ImageProcessingPolicy CompatibleDefault { get; } =
        new(SessionImageProcessingSelection.Compatible, IgnoreSmallTrimNoise: true);

    public SettingEntry ToSetting() => SettingEntry.Text(
        SettingKey.ImageProcessingDefaults, JsonSerializer.Serialize(this, JsonOptions));

    public static OperationResult<ImageProcessingPolicy> FromSettings(IReadOnlyList<SettingEntry> entries)
    {
        SettingEntry? stored = entries.FirstOrDefault(entry => entry.Key == SettingKey.ImageProcessingDefaults);
        if (stored is null)
            return OperationResult.Ok(CompatibleDefault);

        if (!TryReadJson(stored.Value, out ImageProcessingPolicy? policy) ||
            policy.Version != CurrentVersion || policy.Selection is not { HasValidMethods: true })
        {
            return OperationResult.Fail<ImageProcessingPolicy>(OperationFailure.Create(
                FailureCode.PersistenceError, "Image processing defaults are malformed or use an unknown version.",
                messageKey: InvalidDefaultsMessage));
        }

        return OperationResult.Ok(policy);
    }

    public static int ReadTrimMarginPixels(IReadOnlyList<SettingEntry> entries) =>
        Math.Max(0, entries.FirstOrDefault(entry => entry.Key == SettingKey.TrimSafetyMarginPixels)?.AsInteger() ?? 0);

    internal static bool TryReadJson<T>(string value, [NotNullWhen(true)] out T? decoded) where T : class
    {
        try
        {
            decoded = JsonSerializer.Deserialize<T>(value, JsonOptions);
            return decoded is not null;
        }
        catch (JsonException)
        {
            decoded = null;
            return false;
        }
    }
}
