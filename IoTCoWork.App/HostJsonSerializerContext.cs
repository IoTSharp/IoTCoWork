using System.Text.Json;
using System.Text.Json.Serialization;
using IoTCoWork.App.Updater;
using IoTCoWork.Workbench.Models;

namespace IoTCoWork.App;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(LocalHealthResponse))]
[JsonSerializable(typeof(ImageDataResponse))]
[JsonSerializable(typeof(LocalDownloadImageRequest))]
[JsonSerializable(typeof(LocalDownloadImageResponse))]
[JsonSerializable(typeof(LocalRevealFileRequest))]
[JsonSerializable(typeof(LocalErrorResponse))]
[JsonSerializable(typeof(ProxyErrorResponse))]
[JsonSerializable(typeof(AppUpdateCheckResponse))]
[JsonSerializable(typeof(AppUpdateInstallRequest))]
[JsonSerializable(typeof(AppUpdateInstallResponse))]
[JsonSerializable(typeof(AppUpdateAssetInfo))]
[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(List<GitHubReleaseAsset>))]
internal sealed partial class HostJsonSerializerContext : JsonSerializerContext
{
}

public sealed record LocalHealthResponse(
    string Status,
    DateTimeOffset CheckedAt);

public sealed record ImageDataResponse(
    string DataUrl);

public sealed record LocalDownloadImageRequest(
    string? Url,
    string? DataUrl,
    string? FileName);

public sealed record LocalDownloadImageResponse(
    string FilePath,
    string FileName);

public sealed record LocalRevealFileRequest(
    string? FilePath);

public sealed record LocalErrorResponse(
    string Message);

public sealed record ProxyErrorResponse(
    int Code,
    string Message);
