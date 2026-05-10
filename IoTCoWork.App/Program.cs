using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using OmniHost;
using OmniHost.NativeWebView2;
using OmniHost.Windows;

var headless = args.Any(IsHeadlessSwitch);
var filteredArgs = args.Where(arg => !IsHeadlessSwitch(arg)).ToArray();
var startedAt = DateTimeOffset.Now;

if (headless)
{
    var headlessBuilder = WebApplication.CreateBuilder(filteredArgs);
    var headlessUrl = ResolveListenUrl(headlessBuilder.Configuration);
    headlessBuilder.WebHost.UseStaticWebAssets();
    headlessBuilder.WebHost.UseUrls(headlessUrl);

    var headlessApp = headlessBuilder.Build();
    ConfigureWebApplication(headlessApp, () => ResolveStartedServerUrl(headlessApp, headlessUrl), startedAt);
    await headlessApp.RunAsync();
    return;
}

var builder = OmniApplication.CreateBuilder(filteredArgs);
var listenUrl = ResolveListenUrl(builder.Configuration);
builder.WebHost.UseStaticWebAssets();
builder.WebHost.UseUrls(listenUrl);

var app = builder
    .ConfigureDesktop((options, webApp) =>
    {
        var serverUrl = ResolveStartedServerUrl(webApp, listenUrl);
        options.Title = "IoTCoWork";
        options.StartUrl = serverUrl;
        options.Width = 1280;
        options.Height = 820;
        options.EnableDevTools = webApp.Environment.IsDevelopment();
        options.WindowStyle = OmniWindowStyle.Normal;
        options.BuiltInTitleBarStyle = OmniBuiltInTitleBarStyle.None;
        options.ScrollBarMode = OmniScrollBarMode.Auto;
        options.UserDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IoTCoWork",
            "WebView2");
    })
    .UseAdapter(new NativeWebView2AdapterFactory())
    .UseRuntime(new Win32Runtime())
    .UseDesktopApp(webApp => new IoTCoWorkDesktopApp(
        () => ResolveStartedServerUrl(webApp, listenUrl),
        startedAt))
    .Build();

ConfigureWebApplication(app.Web, () => ResolveStartedServerUrl(app.Web, listenUrl), startedAt);
await app.RunAsync();

static void ConfigureWebApplication(
    WebApplication webApp,
    Func<string> serverUrlResolver,
    DateTimeOffset startedAt)
{
    webApp.UseBlazorFrameworkFiles();
    webApp.UseStaticFiles();

    webApp.MapGet("/health", () => Results.Ok(new WorkbenchHealthResponse(
        "IoTCoWork",
        "ok",
        serverUrlResolver(),
        startedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))));

    webApp.MapGet("/api/workbench/shell", () => Results.Ok(new WorkbenchShellResponse(
        "IoTCoWork",
        "OmniHost + Win32Runtime",
        "Blazor WebAssembly client",
        serverUrlResolver(),
        startedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))));

    webApp.MapFallbackToFile("index.html");
}

static string ResolveListenUrl(IConfiguration configuration)
{
    var candidate = FirstNonEmpty(
        configuration["urls"],
        Environment.GetEnvironmentVariable("ASPNETCORE_URLS"),
        configuration["IoTCoWork:Url"]);

    if (string.IsNullOrWhiteSpace(candidate))
    {
        return $"http://127.0.0.1:{GetFreeTcpPort()}";
    }

    var url = candidate.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
    return ValidateLocalUrl(url);
}

static string ResolveStartedServerUrl(WebApplication webApp, string fallbackUrl)
{
    var addresses = webApp.Services
        .GetRequiredService<IServer>()
        .Features
        .Get<IServerAddressesFeature>()?
        .Addresses;

    if (addresses is not null)
    {
        var address = addresses.FirstOrDefault(IsLocalHttpUrl);
        if (!string.IsNullOrWhiteSpace(address))
        {
            return NormalizeLocalUrl(address);
        }
    }

    return NormalizeLocalUrl(fallbackUrl);
}

static bool IsHeadlessSwitch(string value)
    => string.Equals(value, "--headless", StringComparison.OrdinalIgnoreCase)
       || string.Equals(value, "/headless", StringComparison.OrdinalIgnoreCase);

static string FirstNonEmpty(params string?[] values)
    => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

static string ValidateLocalUrl(string configuredUrl)
{
    if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri))
    {
        throw new InvalidOperationException($"监听地址无效：{configuredUrl}。");
    }

    if (uri.Scheme is not ("http" or "https"))
    {
        throw new InvalidOperationException("仅支持本地 http 或 https 地址。");
    }

    if (!uri.IsLoopback)
    {
        throw new InvalidOperationException("宿主只允许绑定到本机回环地址。");
    }

    return NormalizeLocalUrl(uri.AbsoluteUri);
}

static bool IsLocalHttpUrl(string value)
    => Uri.TryCreate(value, UriKind.Absolute, out var uri)
       && uri.IsLoopback
       && uri.Scheme is "http" or "https";

static string NormalizeLocalUrl(string url)
    => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        ? new UriBuilder(uri)
        {
            Host = string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ? "127.0.0.1" : uri.Host,
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri.AbsoluteUri.TrimEnd('/')
        : url.TrimEnd('/');

static int GetFreeTcpPort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

sealed class IoTCoWorkDesktopApp(Func<string> serverUrlResolver, DateTimeOffset startedAt) : IWindowAwareDesktopApp
{
    public Task OnStartAsync(IWebViewAdapter adapter, CancellationToken cancellationToken = default)
    {
        adapter.JsBridge.RegisterHandler("host.info", _ =>
        {
            var payload = JsonSerializer.Serialize(new HostInfoResponse(
                serverUrlResolver(),
                "windows-win32-webview2",
                startedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")));

            return Task.FromResult(payload);
        });

        return Task.CompletedTask;
    }

    public Task OnClosingAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task OnWindowStartAsync(OmniWindowContext window, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task OnWindowClosingAsync(OmniWindowContext window, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

public sealed record WorkbenchHealthResponse(
    string Product,
    string Status,
    string ServerUrl,
    string StartedAtLocal);

public sealed record WorkbenchShellResponse(
    string Product,
    string HostMode,
    string RenderingMode,
    string ServerUrl,
    string StartedAtLocal);

public sealed record HostInfoResponse(
    string ServerUrl,
    string Platform,
    string StartedAtLocal);
