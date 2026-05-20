using System.Text.Json;
using System.Text.Json.Serialization;

namespace IoTCoWork.Workbench.Models;

public sealed class StudioSettings
{
    public const string DefaultAiGatewayBaseUrl = "https://iotsharp.online/v1/";

    private string? _legacyBaseUrl;
    private string? _legacyAccessToken;

    public string AiGatewayBaseUrl { get; set; } = DefaultAiGatewayBaseUrl;
    public string NetworkProxyUrl { get; set; } = string.Empty;
    public string AiGatewayAccessToken { get; set; } = string.Empty;
    public string DeviceLocalId { get; set; } = $"iotcowork-{Guid.NewGuid():N}";

    [JsonPropertyName("apiKey")]
    public string? LegacyImageApiKey
    {
        get => null;
        set => _legacyAccessToken = value;
    }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ObsoleteSettings { get; set; }
    [JsonPropertyName("sonnetBaseUrl")]
    public string? LegacyBaseUrl
    {
        get => null;
        set => _legacyBaseUrl = value;
    }

    [JsonPropertyName("baseUrl")]
    public string? LegacyOpenAiBaseUrl
    {
        get => null;
        set => _legacyBaseUrl = value;
    }

    [JsonPropertyName("sonnetProxyUrl")]
    public string? LegacyProxyUrl
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(NetworkProxyUrl))
            {
                NetworkProxyUrl = value.Trim();
            }
        }
    }

    public string ThemeMode { get; set; } = "system";
    public string ChatModel { get; set; } = "gpt-5.5";
    public string PromptPolishMode { get; set; } = "direct";
    public string Model { get; set; } = "gpt-image-2";
    public string Size { get; set; } = "auto";
    public string AspectRatio { get; set; } = "auto";
    public string ResolutionTier { get; set; } = "2k";
    public string Quality { get; set; } = "auto";
    public string Style { get; set; } = "默认";
    public string Background { get; set; } = "auto";
    public string Format { get; set; } = "png";
    public int Compression { get; set; } = 100;
    public string Moderation { get; set; } = "auto";
    public string InputFidelity { get; set; } = "默认";
    public string ResponseFormat { get; set; } = "b64_json";
    public string RequestMode { get; set; } = "stream";
    public int PartialImages { get; set; } = 2;
    public string User { get; set; } = string.Empty;
    public string AdvancedJson { get; set; } = string.Empty;

    public void Normalize()
    {
        if (ShouldUseLegacyBaseUrl(_legacyBaseUrl))
        {
            AiGatewayBaseUrl = _legacyBaseUrl!.Trim();
        }

        if (string.IsNullOrWhiteSpace(AiGatewayAccessToken) &&
            !string.IsNullOrWhiteSpace(_legacyAccessToken))
        {
            AiGatewayAccessToken = _legacyAccessToken.Trim();
        }

        if (string.IsNullOrWhiteSpace(AiGatewayBaseUrl))
        {
            AiGatewayBaseUrl = DefaultAiGatewayBaseUrl;
        }

        AiGatewayBaseUrl = NormalizeAbsoluteUrl(AiGatewayBaseUrl, DefaultAiGatewayBaseUrl);
        NetworkProxyUrl = NetworkProxyUrl.Trim();
        AiGatewayAccessToken = AiGatewayAccessToken.Trim();
        if (string.IsNullOrWhiteSpace(DeviceLocalId))
        {
            DeviceLocalId = $"iotcowork-{Guid.NewGuid():N}";
        }

        ThemeMode = NormalizeThemeMode(ThemeMode);
        PromptPolishMode = NormalizePromptPolishMode(PromptPolishMode);
        AspectRatio = NormalizeAspectRatio(AspectRatio);
        ResolutionTier = NormalizeResolutionTier(ResolutionTier);

        _legacyBaseUrl = null;
        _legacyAccessToken = null;
        ObsoleteSettings = null;
    }

    public static string NormalizeThemeMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "light" => "light",
            "dark" => "dark",
            _ => "system",
        };
    }

    public static string NormalizePromptPolishMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "ask" => "ask",
            "auto" => "auto",
            _ => "direct",
        };
    }

    public static string NormalizeAspectRatio(string? value)
    {
        return value?.Trim() switch
        {
            "1:1" => "1:1",
            "3:4" => "3:4",
            "2:3" => "2:3",
            "9:16" => "9:16",
            "3:2" => "3:2",
            "4:3" => "4:3",
            "16:9" => "16:9",
            "21:9" => "21:9",
            _ => "auto",
        };
    }

    public static string NormalizeResolutionTier(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "1k" => "1k",
            "2k" => "2k",
            "4k" => "4k",
            "8mp" or "8k" => "8mp",
            _ => "2k",
        };
    }

    private bool ShouldUseLegacyBaseUrl(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            (string.IsNullOrWhiteSpace(AiGatewayBaseUrl) ||
            string.Equals(AiGatewayBaseUrl.Trim(), DefaultAiGatewayBaseUrl, StringComparison.OrdinalIgnoreCase)) &&
            Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" &&
            uri.Host.EndsWith("iotsharp.online", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeAbsoluteUrl(string value, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            candidate = fallback;
        }

        return candidate.EndsWith('/') ? candidate : candidate + "/";
    }
}

public sealed class StudioSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "新建作图";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public string Mode { get; set; } = "generate";
    public string Prompt { get; set; } = string.Empty;
    public string ImageReferences { get; set; } = string.Empty;
    public string MaskReference { get; set; } = string.Empty;
    public List<StudioMessage> Messages { get; set; } = [];
}

public sealed class StudioMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public List<GeneratedImage> Images { get; set; } = [];
}

public sealed class GeneratedImage
{
    public string Url { get; set; } = string.Empty;
    public string? RevisedPrompt { get; set; }
    public string? MimeType { get; set; }
}

public sealed class StudioSnapshot
{
    public StudioSettings Settings { get; set; } = new();
    public List<StudioSession> Sessions { get; set; } = [];
    public string? ActiveSessionId { get; set; }
}

public sealed record StudioImageRequest(
    StudioSettings Settings,
    string Prompt,
    IReadOnlyList<string> ImageReferences,
    string? MaskReference,
    IReadOnlyList<ImageReferenceFile> ReferenceFiles,
    int Count,
    string Mode);

public sealed record ImageReferenceFile(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record StudioImageResult(
    IReadOnlyList<GeneratedImage> Images,
    string RawJson);

internal sealed class ImageGenerationRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = "gpt-image-2";
    [JsonPropertyName("prompt")] public string Prompt { get; set; } = string.Empty;
    [JsonPropertyName("n")] public int Count { get; set; } = 1;
    [JsonPropertyName("size")] public string? Size { get; set; }
    [JsonPropertyName("quality")] public string? Quality { get; set; }
    [JsonPropertyName("style")] public string? Style { get; set; }
    [JsonPropertyName("background")] public string? Background { get; set; }
    [JsonPropertyName("output_format")] public string? OutputFormat { get; set; }
    [JsonPropertyName("output_compression")] public int? OutputCompression { get; set; }
    [JsonPropertyName("moderation")] public string? Moderation { get; set; }
    [JsonPropertyName("input_fidelity")] public string? InputFidelity { get; set; }
    [JsonPropertyName("response_format")] public string? ResponseFormat { get; set; }
    [JsonPropertyName("stream")] public bool? Stream { get; set; }
    [JsonPropertyName("partial_images")] public int? PartialImages { get; set; }
    [JsonPropertyName("user")] public string? User { get; set; }
    [JsonPropertyName("images")] public List<ImageReferencePayload>? Images { get; set; }
    [JsonPropertyName("image")] public ImageReferencePayload? Image { get; set; }
    [JsonPropertyName("mask")] public ImageReferencePayload? Mask { get; set; }
    [JsonExtensionData] public Dictionary<string, object?> ExtensionData { get; set; } = new();
}

internal sealed class ImageReferencePayload
{
    [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
    [JsonPropertyName("file_id")] public string? FileId { get; set; }
}

internal sealed class ImageGenerationResponse
{
    [JsonPropertyName("data")] public List<ImageGenerationData>? Data { get; set; }
    [JsonPropertyName("output")] public List<ImageGenerationOutput>? Output { get; set; }
}

internal sealed class ImageGenerationData
{
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("b64_json")] public string? Base64Json { get; set; }
    [JsonPropertyName("revised_prompt")] public string? RevisedPrompt { get; set; }
    [JsonPropertyName("mime_type")] public string? MimeType { get; set; }
}

internal sealed class ImageGenerationOutput
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("result")] public string? Result { get; set; }
    [JsonPropertyName("b64_json")] public string? Base64Json { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("mime_type")] public string? MimeType { get; set; }
    [JsonPropertyName("revised_prompt")] public string? RevisedPrompt { get; set; }
}
