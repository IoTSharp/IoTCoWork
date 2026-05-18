using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using IoTCoWork.Workbench.Models;

namespace IoTCoWork.App.Updater;

public sealed class AppUpdateService
{
    public const string InstallingStatus = "installing";

    private const string RepositoryOwner = "maikebing";
    private const string RepositoryName = "IoTCoWork";
    private const string Repository = $"{RepositoryOwner}/{RepositoryName}";
    private const long MaxDownloadBytes = 512L * 1024L * 1024L;

    private readonly IHttpClientFactory _httpClientFactory;

    public AppUpdateService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<AppUpdateCheckResponse> CheckAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = GetCurrentVersion();
        var currentVersionDisplay = AppUpdateVersionComparer.ToDisplayVersion(currentVersion);
        var platform = ResolvePlatform();
        if (platform is null)
        {
            return new AppUpdateCheckResponse(
                currentVersion,
                currentVersionDisplay,
                Repository,
                "unsupported",
                Supported: false,
                CanInstall: false,
                UpdateAvailable: false,
                LatestVersion: null,
                LatestVersionDisplay: null,
                LatestTagName: null,
                ReleaseName: null,
                ReleaseUrl: null,
                PublishedAt: null,
                Asset: null,
                Message: "当前平台暂不支持自动更新。");
        }

        try
        {
            var release = await GetLatestReleaseAsync(cancellationToken);
            var latestVersion = NormalizeTagVersion(release.TagName);
            var updateAvailable = AppUpdateVersionComparer.IsNewer(latestVersion, currentVersion);
            var asset = SelectAsset(release.Assets, platform);
            var canInstall = updateAvailable && asset is not null && CanInstall(platform);
            var message = BuildCheckMessage(updateAvailable, asset, platform, canInstall);

            return new AppUpdateCheckResponse(
                currentVersion,
                currentVersionDisplay,
                Repository,
                platform.Id,
                Supported: true,
                CanInstall: canInstall,
                UpdateAvailable: updateAvailable,
                LatestVersion: latestVersion,
                LatestVersionDisplay: AppUpdateVersionComparer.ToDisplayVersion(latestVersion),
                LatestTagName: release.TagName,
                ReleaseName: release.Name,
                ReleaseUrl: release.HtmlUrl,
                PublishedAt: release.PublishedAt,
                Asset: asset is null ? null : new AppUpdateAssetInfo(asset.Name, asset.Size),
                Message: message);
        }
        catch (HttpRequestException ex)
        {
            return BuildFailureResponse(currentVersion, currentVersionDisplay, platform.Id, $"检查更新失败：{ex.Message}");
        }
        catch (JsonException ex)
        {
            return BuildFailureResponse(currentVersion, currentVersionDisplay, platform.Id, $"无法解析 GitHub Release：{ex.Message}");
        }
    }

    public async Task<AppUpdateInstallResponse> InstallAsync(
        AppUpdateInstallRequest request,
        CancellationToken cancellationToken = default)
    {
        var check = await CheckAsync(cancellationToken);
        if (!check.Supported)
        {
            return new AppUpdateInstallResponse("unsupported", check.Message);
        }

        if (!check.UpdateAvailable)
        {
            return new AppUpdateInstallResponse("up-to-date", "当前已经是最新版本。");
        }

        if (!check.CanInstall)
        {
            return new AppUpdateInstallResponse("unavailable", check.Message);
        }

        var platform = ResolvePlatform() ??
            throw new InvalidOperationException("当前平台暂不支持自动更新。");
        var release = await GetLatestReleaseAsync(cancellationToken);
        var asset = SelectAsset(release.Assets, platform);
        if (asset is null)
        {
            return new AppUpdateInstallResponse("unavailable", "最新 Release 没有适用于当前平台的安装包。");
        }

        if (!string.IsNullOrWhiteSpace(request.TagName) &&
            !string.Equals(request.TagName, release.TagName, StringComparison.OrdinalIgnoreCase))
        {
            return new AppUpdateInstallResponse("changed", "最新版本已经变化，请重新检查更新。");
        }

        if (!string.IsNullOrWhiteSpace(request.AssetName) &&
            !string.Equals(request.AssetName, asset.Name, StringComparison.OrdinalIgnoreCase))
        {
            return new AppUpdateInstallResponse("changed", "安装包已经变化，请重新检查更新。");
        }

        var stagingDirectory = CreateStagingDirectory(release.TagName);
        var packagePath = Path.Combine(stagingDirectory, asset.Name);
        await DownloadAssetAsync(asset, packagePath, cancellationToken);

        if (platform.PackageKind == AppPackageKind.WindowsZip)
        {
            var extractedDirectory = Path.Combine(stagingDirectory, "extracted");
            ZipFile.ExtractToDirectory(packagePath, extractedDirectory, overwriteFiles: true);
            StartWindowsInstaller(extractedDirectory);
        }
        else if (platform.PackageKind == AppPackageKind.MacDmg)
        {
            StartMacInstaller(packagePath);
        }
        else
        {
            return new AppUpdateInstallResponse("unsupported", "当前安装包类型暂不支持自动安装。");
        }

        return new AppUpdateInstallResponse(
            InstallingStatus,
            "更新包已下载，IoTCoWork 将退出并自动完成安装。");
    }

    private static AppUpdateCheckResponse BuildFailureResponse(
        string currentVersion,
        string currentVersionDisplay,
        string platform,
        string message) =>
        new(
            currentVersion,
            currentVersionDisplay,
            Repository,
            platform,
            Supported: true,
            CanInstall: false,
            UpdateAvailable: false,
            LatestVersion: null,
            LatestVersionDisplay: null,
            LatestTagName: null,
            ReleaseName: null,
            ReleaseUrl: null,
            PublishedAt: null,
            Asset: null,
            Message: message);

    private async Task<GitHubRelease> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("IoTCoWork", GetCurrentVersion()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await _httpClientFactory.CreateClient("app-update")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var release = await response.Content.ReadFromJsonAsync(
            HostJsonSerializerContext.Default.GitHubRelease,
            cancellationToken);
        return release ?? throw new JsonException("GitHub Release 响应为空。");
    }

    private async Task DownloadAssetAsync(
        GitHubReleaseAsset asset,
        string packagePath,
        CancellationToken cancellationToken)
    {
        if (asset.Size <= 0 || asset.Size > MaxDownloadBytes)
        {
            throw new InvalidOperationException("更新包大小异常，已取消下载。");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, asset.BrowserDownloadUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("IoTCoWork", GetCurrentVersion()));

        using var response = await _httpClientFactory.CreateClient("app-update")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaxDownloadBytes)
        {
            throw new InvalidOperationException("更新包超过大小限制，已取消下载。");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(packagePath);
        var buffer = new byte[1024 * 128];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaxDownloadBytes)
            {
                throw new InvalidOperationException("更新包超过大小限制，已取消下载。");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static string BuildCheckMessage(
        bool updateAvailable,
        GitHubReleaseAsset? asset,
        PlatformUpdateTarget platform,
        bool canInstall)
    {
        if (!updateAvailable)
        {
            return "当前已经是最新版本。";
        }

        if (asset is null)
        {
            return "发现新版本，但最新 Release 没有适用于当前平台的安装包。";
        }

        if (!canInstall)
        {
            return platform.PackageKind == AppPackageKind.WindowsZip
                ? "发现新版本。开发运行目录不支持自动替换，请使用 Release 安装包更新。"
                : "发现新版本。当前运行方式不支持自动安装，请手动下载更新包。";
        }

        return "发现新版本，可以自动更新。";
    }

    private static GitHubReleaseAsset? SelectAsset(
        IReadOnlyList<GitHubReleaseAsset> assets,
        PlatformUpdateTarget platform)
    {
        return assets.FirstOrDefault(asset =>
            platform.AssetNames.Any(name =>
                string.Equals(asset.Name, name, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool CanInstall(PlatformUpdateTarget platform)
    {
        if (platform.PackageKind == AppPackageKind.WindowsZip)
        {
            var appPath = Environment.ProcessPath;
            return !string.IsNullOrWhiteSpace(appPath) &&
                File.Exists(appPath) &&
                appPath.EndsWith("IoTCoWork.exe", StringComparison.OrdinalIgnoreCase);
        }

        if (platform.PackageKind == AppPackageKind.MacDmg)
        {
            return TryResolveMacAppBundle(out _);
        }

        return false;
    }

    private static void StartWindowsInstaller(string extractedDirectory)
    {
        var currentExe = Environment.ProcessPath ??
            throw new InvalidOperationException("无法定位当前程序。");
        var installDirectory = Path.GetDirectoryName(currentExe) ??
            throw new InvalidOperationException("无法定位安装目录。");
        var newExe = Path.Combine(extractedDirectory, Path.GetFileName(currentExe));
        if (!File.Exists(newExe))
        {
            throw new InvalidOperationException("更新包中没有找到 IoTCoWork.exe。");
        }

        var scriptPath = Path.Combine(Path.GetTempPath(), $"IoTCoWork-update-{Guid.NewGuid():N}.ps1");
        var logPath = Path.Combine(Path.GetTempPath(), "IoTCoWork-update.log");
        var script = $$"""
$ErrorActionPreference = 'Stop'
$installDir = {{PowerShellString(installDirectory)}}
$sourceDir = {{PowerShellString(extractedDirectory)}}
$exePath = {{PowerShellString(currentExe)}}
$logPath = {{PowerShellString(logPath)}}
Start-Sleep -Seconds 2
for ($attempt = 0; $attempt -lt 90; $attempt++) {
    try {
        $stream = [System.IO.File]::Open($exePath, 'Open', 'ReadWrite', 'None')
        $stream.Dispose()
        break
    } catch {
        Start-Sleep -Milliseconds 500
    }
}
Copy-Item -Path (Join-Path $sourceDir '*') -Destination $installDir -Recurse -Force
Start-Process -FilePath (Join-Path $installDir 'IoTCoWork.exe') -WorkingDirectory $installDir
"Updated IoTCoWork at $(Get-Date -Format o)" | Set-Content -LiteralPath $logPath -Encoding UTF8
Remove-Item -LiteralPath $PSCommandPath -Force
""";

        File.WriteAllText(scriptPath, script);
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File {QuoteArgument(scriptPath)}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    private static void StartMacInstaller(string dmgPath)
    {
        if (!TryResolveMacAppBundle(out var appBundlePath))
        {
            throw new InvalidOperationException("无法定位当前 .app，不能自动安装。");
        }

        var appParent = Path.GetDirectoryName(appBundlePath) ??
            throw new InvalidOperationException("无法定位 Applications 目录。");
        var scriptPath = Path.Combine(Path.GetTempPath(), $"IoTCoWork-update-{Guid.NewGuid():N}.sh");
        var logPath = Path.Combine(Path.GetTempPath(), "IoTCoWork-update.log");
        var script = $$"""
#!/usr/bin/env bash
set -euo pipefail
dmg_path={{BashString(dmgPath)}}
app_path={{BashString(appBundlePath)}}
app_parent={{BashString(appParent)}}
log_path={{BashString(logPath)}}
sleep 2
mount_point="$(mktemp -d /tmp/IoTCoWork-update.XXXXXX)"
cleanup() {
  hdiutil detach "$mount_point" -quiet || true
  rmdir "$mount_point" 2>/dev/null || true
  rm -f "$0"
}
trap cleanup EXIT
hdiutil attach "$dmg_path" -mountpoint "$mount_point" -nobrowse -quiet
new_app="$mount_point/IoTCoWork.app"
if [[ ! -d "$new_app" ]]; then
  echo "IoTCoWork.app not found in dmg" > "$log_path"
  exit 1
fi
rm -rf "$app_path"
cp -R "$new_app" "$app_parent/"
open "$app_path"
echo "Updated IoTCoWork at $(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$log_path"
""";

        File.WriteAllText(scriptPath, script);
        Process.Start("chmod", $"+x {QuoteArgument(scriptPath)}")?.WaitForExit(3000);
        Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = QuoteArgument(scriptPath),
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    private static bool TryResolveMacAppBundle([NotNullWhen(true)] out string? appBundlePath)
    {
        appBundlePath = null;
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var directory = new DirectoryInfo(baseDirectory);
        while (directory is not null)
        {
            if (directory.Extension.Equals(".app", StringComparison.OrdinalIgnoreCase) &&
                directory.Name.Equals("IoTCoWork.app", StringComparison.OrdinalIgnoreCase))
            {
                appBundlePath = directory.FullName;
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }

    private static PlatformUpdateTarget? ResolvePlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return new PlatformUpdateTarget(
                "windows-x64",
                AppPackageKind.WindowsZip,
                ["IoTCoWork-windows-x64.zip"]);
        }

        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => new PlatformUpdateTarget(
                    "macos-arm64",
                    AppPackageKind.MacDmg,
                    ["IoTCoWork-osx-arm64.dmg", "IoTCoWork-macos-arm64.dmg"]),
                Architecture.X64 => new PlatformUpdateTarget(
                    "macos-x64",
                    AppPackageKind.MacDmg,
                    ["IoTCoWork-osx-x64.dmg", "IoTCoWork-macos-x64.dmg"]),
                _ => null,
            };
        }

        return null;
    }

    private static string CreateStagingDirectory(string tagName)
    {
        var safeTag = string.Join("_", tagName.Split(Path.GetInvalidFileNameChars()));
        var directory = Path.Combine(
            Path.GetTempPath(),
            "IoTCoWork",
            "Updates",
            $"{safeTag}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string GetCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.1.0";
        return NormalizeTagVersion(version);
    }

    private static string NormalizeTagVersion(string? version)
    {
        var normalized = AppUpdateVersionComparer.NormalizeForDisplay(version);
        return normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? normalized[1..]
            : normalized;
    }

    private static string PowerShellString(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string BashString(string value) =>
        "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static string QuoteArgument(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private sealed record PlatformUpdateTarget(
        string Id,
        AppPackageKind PackageKind,
        string[] AssetNames);

    private enum AppPackageKind
    {
        WindowsZip,
        MacDmg,
    }

}

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset> Assets { get; set; } = [];
}

internal sealed class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;
}
