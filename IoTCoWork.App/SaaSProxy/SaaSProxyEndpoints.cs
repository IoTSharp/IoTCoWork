using System.Net;
using System.Text.Json;
using IoTCoWork.App;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace IoTCoWork.App.SaaSProxy;

public static class SaaSProxyEndpoints
{
    public const string UpstreamBaseHeader = "X-IoTCoWork-SaaS-Base";
    public const string HttpProxyHeader = "X-IoTCoWork-SaaS-Http-Proxy";
    public const string ProxyMarkerHeader = "X-IoTCoWork-SaaS-Proxy";

    private const string DefaultPlatformBaseUrl = "https://api.iotsharp.net/";
    private const string DefaultAiGatewayBaseUrl = "https://ai.iotsharp.net/";
    private const long MaxProxyRequestBodyBytes = 96L * 1024 * 1024;

    private static readonly string[] SupportedMethods =
    [
        HttpMethods.Get,
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete,
    ];

    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade",
    };

    private static readonly HashSet<string> LocalOnlyHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cookie",
        "Host",
        "Origin",
        "Referer",
        HttpProxyHeader,
        UpstreamBaseHeader,
    };

    private static readonly HashSet<string> AllowedPlatformExactPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "auth/login",
        "auth/logout",
        "auth/refresh",
        "auth/register",
        "account/me",
        "wallet",
        "wallet/transactions",
        "recharge-orders",
        "device-codes",
        "device-tokens",
    };

    private static readonly string[] AllowedPlatformPathPrefixes =
    [
        "recharge-orders/",
        "device-codes/",
    ];

    public static void MapSaaSProxyEndpoints(this WebApplication app)
    {
        app.MapMethods("/api/iotsharp/platform/{**path}", SupportedMethods, ProxyPlatformAsync);
        app.MapMethods("/api/iotsharp/ai/{**path}", SupportedMethods, ProxyAiAsync);
    }

    private static Task ProxyPlatformAsync(
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        string? path,
        CancellationToken cancellationToken)
    {
        return ProxyAsync(
            context,
            httpClientFactory,
            path,
            DefaultPlatformBaseUrl,
            BuildPlatformTargetUri,
            IsAllowedPlatformPath,
            "账户服务",
            cancellationToken);
    }

    private static Task ProxyAiAsync(
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        string? path,
        CancellationToken cancellationToken)
    {
        return ProxyAsync(
            context,
            httpClientFactory,
            path,
            DefaultAiGatewayBaseUrl,
            BuildAbsoluteTargetUri,
            IsAllowedAiPath,
            "AI 网关",
            cancellationToken);
    }

    private static async Task ProxyAsync(
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        string? path,
        string defaultBaseUrl,
        Func<HttpRequest, string, string, Uri> targetBuilder,
        Func<string, bool> isAllowedPath,
        string serviceLabel,
        CancellationToken cancellationToken)
    {
        context.Response.Headers[ProxyMarkerHeader] = "1";

        var cleanPath = NormalizePath(path);
        if (!isAllowedPath(cleanPath))
        {
            await WriteProxyErrorAsync(context, StatusCodes.Status404NotFound, $"{serviceLabel}代理不支持这个接口。", cancellationToken);
            return;
        }

        Uri target;
        try
        {
            target = targetBuilder(context.Request, cleanPath, defaultBaseUrl);
        }
        catch (InvalidOperationException ex)
        {
            await WriteProxyErrorAsync(context, StatusCodes.Status400BadRequest, ex.Message, cancellationToken);
            return;
        }

        using var proxyRequest = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
        CopyRequestHeaders(context.Request, proxyRequest);

        if (HasRequestBody(context.Request))
        {
            try
            {
                proxyRequest.Content = await BufferRequestContentAsync(context.Request, cancellationToken);
            }
            catch (ProxyRequestBodyTooLargeException ex)
            {
                await WriteProxyErrorAsync(context, StatusCodes.Status413PayloadTooLarge, ex.Message, cancellationToken);
                return;
            }
        }

        using var proxyHandler = CreateProxyHandler(context.Request);
        using var proxyHttpClient = proxyHandler is null
            ? null
            : new HttpClient(proxyHandler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromMinutes(10),
            };
        var httpClient = proxyHttpClient ?? httpClientFactory.CreateClient("iotsharp-saas-proxy");

        try
        {
            using var proxyResponse = await httpClient.SendAsync(
                proxyRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            await CopyProxyResponseAsync(context, proxyResponse, cancellationToken);
        }
        catch (OperationCanceledException) when (IsClientAbort(context, cancellationToken))
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await TryWriteProxyErrorAsync(
                context,
                StatusCodes.Status502BadGateway,
                $"无法连接{serviceLabel}：{ex.Message}",
                cancellationToken);
        }
    }

    private static Uri BuildPlatformTargetUri(HttpRequest request, string path, string defaultBaseUrl)
    {
        var apiRoot = BuildApiRoot(request.Headers[UpstreamBaseHeader].FirstOrDefault(), defaultBaseUrl);
        return AppendQuery(new Uri(apiRoot, path), request);
    }

    private static Uri BuildAbsoluteTargetUri(HttpRequest request, string path, string defaultBaseUrl)
    {
        var root = BuildAbsoluteRoot(request.Headers[UpstreamBaseHeader].FirstOrDefault(), defaultBaseUrl);
        return AppendQuery(new Uri(root, path), request);
    }

    private static Uri AppendQuery(Uri target, HttpRequest request)
    {
        if (!request.QueryString.HasValue)
        {
            return target;
        }

        var builder = new UriBuilder(target)
        {
            Query = request.QueryString.Value!.TrimStart('?'),
        };
        return builder.Uri;
    }

    private static HttpMessageHandler? CreateProxyHandler(HttpRequest request)
    {
        if (!TryReadHttpProxy(request.Headers[HttpProxyHeader].FirstOrDefault(), out var proxyUri))
        {
            return null;
        }

        return new SocketsHttpHandler
        {
            Proxy = new WebProxy(proxyUri),
            UseProxy = true,
            AutomaticDecompression = DecompressionMethods.All,
        };
    }

    private static bool TryReadHttpProxy(string? value, out Uri proxyUri)
    {
        proxyUri = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var proxy = value.Trim();
        if (!proxy.Contains("://", StringComparison.Ordinal))
        {
            proxy = "http://" + proxy;
        }

        return Uri.TryCreate(proxy, UriKind.Absolute, out proxyUri!) &&
            proxyUri.Scheme is "http" or "https" &&
            !string.IsNullOrWhiteSpace(proxyUri.Host);
    }

    private static Uri BuildApiRoot(string? baseUrl, string defaultBaseUrl)
    {
        var root = NormalizeRoot(baseUrl, defaultBaseUrl);
        if (!root.EndsWith("api/v1/", StringComparison.OrdinalIgnoreCase))
        {
            root += "api/v1/";
        }

        return CreateHttpUri(root, "账户服务地址必须是 http 或 https 绝对地址。");
    }

    private static Uri BuildAbsoluteRoot(string? baseUrl, string defaultBaseUrl)
    {
        var root = NormalizeRoot(baseUrl, defaultBaseUrl);
        return CreateHttpUri(root, "AI 网关地址必须是 http 或 https 绝对地址。");
    }

    private static string NormalizeRoot(string? baseUrl, string defaultBaseUrl)
    {
        var root = string.IsNullOrWhiteSpace(baseUrl) ? defaultBaseUrl : baseUrl.Trim();
        return root.EndsWith('/') ? root : root + "/";
    }

    private static Uri CreateHttpUri(string value, string errorMessage)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return uri;
    }

    private static string NormalizePath(string? path)
    {
        return (path ?? string.Empty).Trim().TrimStart('/');
    }

    private static bool IsAllowedPlatformPath(string path)
    {
        return IsSafeRelativePath(path) &&
            (AllowedPlatformExactPaths.Contains(path) ||
            AllowedPlatformPathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsAllowedAiPath(string path)
    {
        return IsSafeRelativePath(path) &&
            (IsAiEndpoint(path, "chat/completions") ||
            IsAiEndpoint(path, "images/generations") ||
            IsAiEndpoint(path, "images/edits") ||
            IsAiEndpoint(path, "images/variations") ||
            path.Equals("v1/copilot/chat", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("copilot/chat", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAiEndpoint(string path, string endpoint)
    {
        return path.Equals(endpoint, StringComparison.OrdinalIgnoreCase) ||
            path.Equals($"v1/{endpoint}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeRelativePath(string path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
            !path.Contains("..", StringComparison.Ordinal) &&
            !path.Contains('\\', StringComparison.Ordinal) &&
            !path.Contains("://", StringComparison.Ordinal);
    }

    private static bool HasRequestBody(HttpRequest request)
    {
        return !HttpMethods.IsGet(request.Method) &&
            !HttpMethods.IsHead(request.Method) &&
            (request.ContentLength is > 0 || request.Headers.ContainsKey("Transfer-Encoding"));
    }

    private static async Task<HttpContent> BufferRequestContentAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength is > MaxProxyRequestBodyBytes)
        {
            throw new ProxyRequestBodyTooLargeException("请求体超过本地代理大小限制。");
        }

        using var buffer = new MemoryStream(
            request.ContentLength is > 0 and <= int.MaxValue ? (int)request.ContentLength.Value : 0);
        await CopyRequestBodyAsync(request.Body, buffer, cancellationToken);
        var content = new ByteArrayContent(buffer.ToArray());
        CopyRequestContentHeaders(request, content);
        return content;
    }

    private static async Task CopyRequestBodyAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                return;
            }

            if (destination.Length + read > MaxProxyRequestBodyBytes)
            {
                throw new ProxyRequestBodyTooLargeException("请求体超过本地代理大小限制。");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static void CopyRequestHeaders(HttpRequest source, HttpRequestMessage target)
    {
        foreach (var header in source.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key) ||
                LocalOnlyHeaders.Contains(header.Key) ||
                header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            target.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
    }

    private static void CopyRequestContentHeaders(HttpRequest source, HttpContent target)
    {
        foreach (var header in source.Headers)
        {
            if (!header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            target.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
    }

    private static async Task CopyProxyResponseAsync(
        HttpContext context,
        HttpResponseMessage proxyResponse,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = (int)proxyResponse.StatusCode;

        foreach (var header in proxyResponse.Headers)
        {
            if (!HopByHopHeaders.Contains(header.Key))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        foreach (var header in proxyResponse.Content.Headers)
        {
            if (!HopByHopHeaders.Contains(header.Key))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        context.Response.Headers.Remove("transfer-encoding");
        await proxyResponse.Content.CopyToAsync(context.Response.Body, cancellationToken);
    }

    private static async Task WriteProxyErrorAsync(
        HttpContext context,
        int statusCode,
        string message,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        var body = JsonSerializer.Serialize(
            new ProxyErrorResponse(statusCode, message),
            HostJsonSerializerContext.Default.ProxyErrorResponse);

        await context.Response.WriteAsync(body, cancellationToken);
    }

    private static async Task TryWriteProxyErrorAsync(
        HttpContext context,
        int statusCode,
        string message,
        CancellationToken cancellationToken)
    {
        if (IsClientAbort(context, cancellationToken))
        {
            return;
        }

        try
        {
            await WriteProxyErrorAsync(context, statusCode, message, cancellationToken);
        }
        catch (OperationCanceledException) when (IsClientAbort(context, cancellationToken))
        {
        }
    }

    private static bool IsClientAbort(HttpContext context, CancellationToken cancellationToken)
    {
        return context.RequestAborted.IsCancellationRequested ||
            cancellationToken.IsCancellationRequested;
    }

    private sealed class ProxyRequestBodyTooLargeException(string message) : Exception(message);
}
