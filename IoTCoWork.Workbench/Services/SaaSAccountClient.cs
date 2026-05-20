using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using IoTCoWork.Workbench.Models;

namespace IoTCoWork.Workbench.Services;

public sealed class SaaSAccountClient
{
    private const string LocalPlatformProxyRoot = "/api/iotsharp/platform/";
    private const string LocalProxyHeader = "X-IoTCoWork-SaaS-Proxy";
    private const string UpstreamBaseHeader = "X-IoTCoWork-SaaS-Base";
    private const string HttpProxyHeader = "X-IoTCoWork-SaaS-Http-Proxy";
    private const string DefaultClientName = "IoTCoWork";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;

    public SaaSAccountClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<SaaSAuthResponse> LoginAsync(
        StudioSettings settings,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("请填写邮箱。");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("请填写密码。");
        }

        var token = await SendAsync<PlatformBearerTokenResponse>(
            settings,
            HttpMethod.Post,
            "auth/login",
            new
            {
                email = email.Trim(),
                password,
            },
            accessToken: null,
            cancellationToken);

        ApplyPlatformToken(settings, token);
        var user = await RefreshProfileAsync(settings, cancellationToken);
        await EnsureCloudTokenAsync(settings, cancellationToken);

        return new SaaSAuthResponse
        {
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken,
            ExpiresIn = token.ExpiresIn,
            TokenType = token.TokenType,
            User = user,
        };
    }

    public async Task<SaaSAuthResponse> RegisterAsync(
        StudioSettings settings,
        string email,
        string password,
        string? promoCode,
        string? invitationCode,
        string? affiliateCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("请填写邮箱。");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("请填写密码。");
        }

        await SendWithoutResultAsync(
            settings,
            HttpMethod.Post,
            "auth/register",
            new
            {
                email = email.Trim(),
                password,
            },
            accessToken: null,
            cancellationToken);

        return await LoginAsync(settings, email, password, cancellationToken);
    }

    public async Task<SaaSAccountProfile> RefreshProfileAsync(
        StudioSettings settings,
        CancellationToken cancellationToken = default)
    {
        await EnsurePlatformTokenAsync(settings, cancellationToken);
        var user = await SendAsync<SaaSAccountProfile>(
            settings,
            HttpMethod.Get,
            "account/me",
            body: null,
            settings.PlatformAccessToken,
            cancellationToken);

        settings.SaaSUser = user;
        return user;
    }

    public async Task EnsureCloudTokenAsync(
        StudioSettings settings,
        CancellationToken cancellationToken = default)
    {
        await EnsurePlatformTokenAsync(settings, cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.CloudAccessToken) &&
            settings.CloudTokenExpiresAt is not null &&
            settings.CloudTokenExpiresAt > DateTimeOffset.Now.AddMinutes(5))
        {
            return;
        }

        var deviceCode = await SendAsync<SaaSDeviceCodeResponse>(
            settings,
            HttpMethod.Post,
            "device-codes",
            new SaaSDeviceCodeCreateRequest
            {
                ClientName = DefaultClientName,
                ClientVersion = GetType().Assembly.GetName().Version?.ToString(3),
                DeviceName = Environment.MachineName,
                DeviceLocalId = settings.DeviceLocalId,
                Scopes = ["platform.cloud", "ai.invoke"],
            },
            accessToken: null,
            cancellationToken);

        await SendAsync<JsonElement>(
            settings,
            HttpMethod.Post,
            $"device-codes/{Uri.EscapeDataString(deviceCode.UserCode)}/approve",
            new SaaSDeviceCodeApproveRequest(),
            settings.PlatformAccessToken,
            cancellationToken);

        var token = await SendAsync<SaaSDeviceTokenResponse>(
            settings,
            HttpMethod.Post,
            "device-tokens",
            new SaaSDeviceTokenRequest
            {
                DeviceCode = deviceCode.DeviceCode,
            },
            accessToken: null,
            cancellationToken);

        settings.CloudAccessToken = token.AccessToken;
        settings.CloudRefreshToken = token.RefreshToken;
        settings.CloudTokenExpiresAt = token.ExpiresIn > 0
            ? DateTimeOffset.Now.AddSeconds(token.ExpiresIn)
            : null;
    }

    public async Task<SaaSRechargeOrder> CreateRechargeOrderAsync(
        StudioSettings settings,
        decimal amount,
        string? tradeType,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            throw new InvalidOperationException("充值金额必须大于 0。");
        }

        await EnsurePlatformTokenAsync(settings, cancellationToken);
        var amountFen = checked((long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero));
        var resolvedTradeType = string.IsNullOrWhiteSpace(tradeType) ? "native" : tradeType.Trim();
        settings.PaymentTradeType = "native";

        return await SendAsync<SaaSRechargeOrder>(
            settings,
            HttpMethod.Post,
            "recharge-orders",
            new SaaSRechargeOrderCreateRequest
            {
                AmountFen = amountFen,
                TradeType = resolvedTradeType,
            },
            settings.PlatformAccessToken,
            cancellationToken);
    }

    public async Task<SaaSRechargeOrder> GetPaymentOrderAsync(
        StudioSettings settings,
        string orderNo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderNo))
        {
            throw new InvalidOperationException("订单号无效。");
        }

        await EnsurePlatformTokenAsync(settings, cancellationToken);
        return await SendAsync<SaaSRechargeOrder>(
            settings,
            HttpMethod.Get,
            $"recharge-orders/{Uri.EscapeDataString(orderNo.Trim())}",
            body: null,
            settings.PlatformAccessToken,
            cancellationToken);
    }

    public Task<SaaSRechargeOrder> VerifyPaymentOrderAsync(
        StudioSettings settings,
        string orderNo,
        CancellationToken cancellationToken = default)
    {
        return GetPaymentOrderAsync(settings, orderNo, cancellationToken);
    }

    public Task CancelPaymentOrderAsync(
        StudioSettings settings,
        string orderNo,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("当前充值通道暂不支持从工作台取消订单；订单超时后会自动关闭。");
    }

    public void SignOut(StudioSettings settings)
    {
        settings.PlatformAccessToken = string.Empty;
        settings.PlatformRefreshToken = string.Empty;
        settings.PlatformTokenExpiresAt = null;
        settings.CloudAccessToken = string.Empty;
        settings.CloudRefreshToken = string.Empty;
        settings.CloudTokenExpiresAt = null;
        settings.SaaSUser = null;
    }

    private async Task EnsurePlatformTokenAsync(StudioSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.PlatformAccessToken))
        {
            throw new InvalidOperationException("请先登录账户。");
        }

        if (string.IsNullOrWhiteSpace(settings.PlatformRefreshToken) ||
            settings.PlatformTokenExpiresAt is null ||
            settings.PlatformTokenExpiresAt > DateTimeOffset.Now.AddMinutes(2))
        {
            return;
        }

        var token = await SendAsync<PlatformBearerTokenResponse>(
            settings,
            HttpMethod.Post,
            "auth/refresh",
            new
            {
                refreshToken = settings.PlatformRefreshToken,
            },
            accessToken: null,
            cancellationToken);

        ApplyPlatformToken(settings, token);
    }

    private async Task<T> SendAsync<T>(
        StudioSettings settings,
        HttpMethod method,
        string path,
        object? body,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SendOnceAsync<T>(
                settings,
                method,
                path,
                body,
                accessToken,
                useLocalProxy: true,
                cancellationToken);
        }
        catch (SaaSProxyUnavailableException)
        {
            return await SendOnceAsync<T>(
                settings,
                method,
                path,
                body,
                accessToken,
                useLocalProxy: false,
                cancellationToken);
        }
    }

    private async Task SendWithoutResultAsync(
        StudioSettings settings,
        HttpMethod method,
        string path,
        object? body,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        await SendAsync<EmptyResponse>(
            settings,
            method,
            path,
            body,
            accessToken,
            cancellationToken);
    }

    private async Task<T> SendOnceAsync<T>(
        StudioSettings settings,
        HttpMethod method,
        string path,
        object? body,
        string? accessToken,
        bool useLocalProxy,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            method,
            useLocalProxy ? BuildLocalProxyEndpoint(path) : BuildEndpoint(settings.PlatformBaseUrl, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.AcceptLanguage.ParseAdd("zh-CN");
        if (useLocalProxy)
        {
            request.Headers.TryAddWithoutValidation(UpstreamBaseHeader, BuildAbsoluteUrl(settings.PlatformBaseUrl, string.Empty));
            if (!string.IsNullOrWhiteSpace(settings.NetworkProxyUrl))
            {
                request.Headers.TryAddWithoutValidation(HttpProxyHeader, settings.NetworkProxyUrl.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (useLocalProxy && !response.Headers.Contains(LocalProxyHeader))
        {
            throw new SaaSProxyUnavailableException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"账户服务返回 {(int)response.StatusCode} {response.ReasonPhrase}: {ExtractErrorMessage(raw)}");
        }

        if (typeof(T) == typeof(EmptyResponse))
        {
            return (T)(object)EmptyResponse.Value;
        }

        if (typeof(T) == typeof(JsonElement))
        {
            return (T)(object)(string.IsNullOrWhiteSpace(raw)
                ? JsonDocument.Parse("{}").RootElement.Clone()
                : JsonDocument.Parse(raw).RootElement.Clone());
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(raw, JsonOptions);
            return value ?? throw new InvalidOperationException("账户服务响应为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"无法解析账户服务响应：{ex.Message}", ex);
        }
    }

    private static void ApplyPlatformToken(StudioSettings settings, PlatformBearerTokenResponse token)
    {
        settings.PlatformAccessToken = token.AccessToken;
        settings.PlatformRefreshToken = token.RefreshToken;
        settings.PlatformTokenExpiresAt = token.ExpiresIn > 0
            ? DateTimeOffset.Now.AddSeconds(token.ExpiresIn)
            : null;
    }

    private static Uri BuildEndpoint(string baseUrl, string path)
    {
        var root = BuildAbsoluteUrl(baseUrl, string.Empty);
        if (!root.EndsWith("api/v1/", StringComparison.OrdinalIgnoreCase))
        {
            root += "api/v1/";
        }

        return new Uri(new Uri(root, UriKind.Absolute), path);
    }

    private static Uri BuildLocalProxyEndpoint(string path)
    {
        return new Uri(LocalPlatformProxyRoot + path.TrimStart('/'), UriKind.Relative);
    }

    private static string BuildAbsoluteUrl(string baseUrl, string path)
    {
        var root = string.IsNullOrWhiteSpace(baseUrl) ? StudioSettings.DefaultPlatformBaseUrl : baseUrl.Trim();
        if (!root.EndsWith('/'))
        {
            root += "/";
        }

        return new Uri(new Uri(root, UriKind.Absolute), path).ToString();
    }

    private static string ExtractErrorMessage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "响应体为空";
        }

        try
        {
            var root = JsonNode.Parse(raw);
            var message =
                JsonText(root?["detail"]) ??
                JsonText(root?["message"]) ??
                JsonText(root?["title"]);
            var error = JsonText(root?["error"]);
            var code = JsonText(root?["code"]);

            if (!string.IsNullOrWhiteSpace(message))
            {
                return !string.IsNullOrWhiteSpace(error) && !string.Equals(error, message, StringComparison.OrdinalIgnoreCase)
                    ? $"{message} ({error})"
                    : !string.IsNullOrWhiteSpace(code) && !string.Equals(code, message, StringComparison.OrdinalIgnoreCase)
                        ? $"{message} ({code})"
                        : message;
            }
        }
        catch (JsonException)
        {
        }

        return raw;
    }

    private static string? JsonText(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;
    }

    private sealed class SaaSProxyUnavailableException : Exception
    {
    }

    private readonly struct EmptyResponse
    {
        public static readonly EmptyResponse Value = new();
    }
}
