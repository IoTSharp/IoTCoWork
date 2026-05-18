using System.Text.Json;
using System.Text.Json.Serialization;
using IoTCoWork.Workbench.Models;
using Microsoft.JSInterop;
using System.Net;
using System.Net.Http.Json;

namespace IoTCoWork.Workbench.Services;

public sealed class ImageStudioStorage
{
    private const string StorageKey = "IoTCoWork.image-studio.snapshot.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly IJSRuntime _jsRuntime;
    private readonly HttpClient _httpClient;

    public ImageStudioStorage(IJSRuntime jsRuntime, HttpClient httpClient)
    {
        _jsRuntime = jsRuntime;
        _httpClient = httpClient;
    }

    public async ValueTask<StudioSnapshot> LoadAsync()
    {
        var hostSnapshot = await LoadFromHostAsync();
        if (hostSnapshot is not null)
        {
            return hostSnapshot;
        }

        var json = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return CreateDefaultSnapshot();
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<StudioSnapshot>(json, JsonOptions);
            if (snapshot is null)
            {
                return CreateDefaultSnapshot();
            }

            EnsureSnapshot(snapshot);
            return snapshot;
        }
        catch (JsonException)
        {
            return CreateDefaultSnapshot();
        }
    }

    public async ValueTask SaveAsync(StudioSnapshot snapshot)
    {
        EnsureSnapshot(snapshot);
        if (await SaveToHostAsync(snapshot))
        {
            return;
        }

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
    }

    public async ValueTask ClearAsync()
    {
        try
        {
            using var response = await _httpClient.DeleteAsync("api/local/snapshot");
            if (response.IsSuccessStatusCode)
            {
                return;
            }
        }
        catch (HttpRequestException)
        {
        }

        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
    }

    private async Task<StudioSnapshot?> LoadFromHostAsync()
    {
        try
        {
            using var response = await _httpClient.GetAsync("api/local/snapshot");
            if (response.StatusCode == HttpStatusCode.NoContent ||
                response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var snapshot = await response.Content.ReadFromJsonAsync<StudioSnapshot>(JsonOptions);
            if (snapshot is null)
            {
                return null;
            }

            EnsureSnapshot(snapshot);
            return snapshot;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<bool> SaveToHostAsync(StudioSnapshot snapshot)
    {
        try
        {
            using var response = await _httpClient.PutAsJsonAsync("api/local/snapshot", snapshot, JsonOptions);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private static StudioSnapshot CreateDefaultSnapshot()
    {
        var session = new StudioSession
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = "透明玻璃茶杯海报",
            Prompt = "一张中文包装海报，主体是透明背景的玻璃茶杯，冷静商业摄影风格",
        };

        return new StudioSnapshot
        {
            Settings = new StudioSettings(),
            Sessions = [session],
            ActiveSessionId = session.Id,
        };
    }

    private static void EnsureSnapshot(StudioSnapshot snapshot)
    {
        snapshot.Settings ??= new StudioSettings();
        snapshot.Settings.Normalize();
        snapshot.Sessions ??= [];
        if (string.IsNullOrWhiteSpace(snapshot.Settings.Model) ||
            snapshot.Settings.Model.Equals("gpt-5.4", StringComparison.OrdinalIgnoreCase) ||
            snapshot.Settings.Model.Equals("gpt-5.5", StringComparison.OrdinalIgnoreCase))
        {
            snapshot.Settings.Model = "gpt-image-2";
        }

        if (snapshot.Sessions.Count == 0)
        {
            var session = new StudioSession();
            snapshot.Sessions.Add(session);
            snapshot.ActiveSessionId = session.Id;
        }

        if (string.IsNullOrWhiteSpace(snapshot.ActiveSessionId) ||
            snapshot.Sessions.All(session => session.Id != snapshot.ActiveSessionId))
        {
            snapshot.ActiveSessionId = snapshot.Sessions[0].Id;
        }
    }
}
