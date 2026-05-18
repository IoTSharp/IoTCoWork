using System.Net.Http.Json;
using System.Text.Json;
using IoTCoWork.Workbench.Models;

namespace IoTCoWork.Workbench.Services;

public sealed class AppUpdateClient
{
    private readonly HttpClient _httpClient;

    public AppUpdateClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<AppUpdateCheckResponse?> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("api/app/update", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<AppUpdateCheckResponse>(cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<AppUpdateInstallResponse> InstallAsync(
        AppUpdateCheckResponse update,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/app/update/install",
            new AppUpdateInstallRequest(update.LatestTagName, update.Asset?.Name),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AppUpdateInstallResponse>(cancellationToken);
        return result ?? new AppUpdateInstallResponse("unknown", "更新服务没有返回安装状态。");
    }
}
