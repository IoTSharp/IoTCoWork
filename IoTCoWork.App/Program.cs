using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
#if WINDOWS
using System.Text.Json;
#endif
using IoTCoWork.App;
using IoTCoWork.App.LocalStore;
using IoTCoWork.App.SaaSProxy;
using IoTCoWork.App.Updater;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
#if WINDOWS
using OmniHost;
using OmniHost.NativeWebView2;
using OmniHost.Windows;
#endif

var headless = args.Any(IsHeadlessSwitch);
var filteredArgs = args.Where(arg => !IsHeadlessSwitch(arg)).ToArray();
var startedAt = DateTimeOffset.Now;

if (headless)
{
    var headlessBuilder = WebApplication.CreateBuilder(filteredArgs);
    var headlessUrl = ResolveListenUrl(headlessBuilder.Configuration);
    headlessBuilder.WebHost.UseUrls(headlessUrl);
    ConfigureLocalServices(headlessBuilder.Services);

    var headlessApp = headlessBuilder.Build();
    ConfigureWebApplication(
        headlessApp,
        CreateWasmFileProvider(ResolveContentRoot(filteredArgs), new EmbeddedWasmFileProvider(Assembly.GetExecutingAssembly())),
        () => ResolveStartedServerUrl(headlessApp, headlessUrl),
        startedAt);
    await headlessApp.RunAsync();
    return;
}

#if WINDOWS
var builder = OmniApplication.CreateBuilder(filteredArgs);
var listenUrl = ResolveListenUrl(builder.Configuration);
builder.WebHost.UseUrls(listenUrl);
ConfigureLocalServices(builder.Web.Services);

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

ConfigureWebApplication(
    app.Web,
    CreateWasmFileProvider(ResolveContentRoot(filteredArgs), new EmbeddedWasmFileProvider(Assembly.GetExecutingAssembly())),
    () => ResolveStartedServerUrl(app.Web, listenUrl),
    startedAt);
await app.RunAsync();
#else
var builder = WebApplication.CreateBuilder(filteredArgs);
var listenUrl = ResolveListenUrl(builder.Configuration);
builder.WebHost.UseUrls(listenUrl);
ConfigureLocalServices(builder.Services);

var app = builder.Build();
ConfigureWebApplication(
    app,
    CreateWasmFileProvider(ResolveContentRoot(filteredArgs), new EmbeddedWasmFileProvider(Assembly.GetExecutingAssembly())),
    () => ResolveStartedServerUrl(app, listenUrl),
    startedAt);

Console.WriteLine($"IoTCoWork 已启动：{NormalizeLocalUrl(listenUrl)}");
Console.WriteLine("按 Ctrl+C 退出。");

if (ShouldOpenBrowser(filteredArgs))
{
    OpenBrowser(NormalizeLocalUrl(listenUrl));
}

await app.RunAsync();
#endif

static void ConfigureWebApplication(
    WebApplication webApp,
    IFileProvider fileProvider,
    Func<string> serverUrlResolver,
    DateTimeOffset startedAt)
{
    webApp.Use(async (context, next) =>
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
    });

    webApp.MapLocalSnapshotEndpoints();
    webApp.MapSaaSProxyEndpoints();
    webApp.MapAppUpdateEndpoints();

    webApp.MapGet("/health", () => Results.Ok(new WorkbenchHealthResponse(
        "IoTCoWork",
        "ok",
        serverUrlResolver(),
        startedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))));

    webApp.MapGet("/api/workbench/shell", () => Results.Ok(new WorkbenchShellResponse(
        "IoTCoWork",
        ResolveHostMode(),
        "Blazor WebAssembly client",
        serverUrlResolver(),
        startedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))));

    webApp.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = fileProvider
    });

    webApp.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = fileProvider
    });

    webApp.MapFallback(async context =>
    {
        var indexFile = fileProvider.GetFileInfo("index.html");
        if (!indexFile.Exists)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        try
        {
            await using var stream = indexFile.CreateReadStream();
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
    });
}

static void ConfigureLocalServices(IServiceCollection services)
{
    services.AddSingleton<ILocalSnapshotStore, JsonLocalSnapshotStore>();
    services.AddSingleton<AppUpdateService>();
    services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.TypeInfoResolverChain.Insert(0, HostJsonSerializerContext.Default);
    });
    services.AddHttpClient("iotsharp-saas-proxy", client =>
    {
        client.Timeout = TimeSpan.FromMinutes(10);
    });
    services.AddHttpClient("image-persist", client =>
    {
        client.Timeout = TimeSpan.FromMinutes(2);
    });
    services.AddHttpClient("app-update", client =>
    {
        client.Timeout = TimeSpan.FromMinutes(5);
    });
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

#if !WINDOWS
static bool ShouldOpenBrowser(string[] args)
{
    return !args.Any(arg =>
        string.Equals(arg, "--no-open", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "--no-browser", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "--open-browser=false", StringComparison.OrdinalIgnoreCase));
}

static void OpenBrowser(string url)
{
    try
    {
        if (OperatingSystem.IsMacOS())
        {
            Process.Start("open", url);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            Process.Start("xdg-open", url);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{url}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            return;
        }

        Console.WriteLine($"请在浏览器中打开：{url}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"无法自动打开浏览器：{ex.Message}");
        Console.WriteLine($"请手动打开：{url}");
    }
}
#endif

static string ResolveHostMode()
{
    if (OperatingSystem.IsWindows())
    {
        return "OmniHost + Win32Runtime";
    }

    if (OperatingSystem.IsMacOS())
    {
        return "ASP.NET Core + WebKit wrapper";
    }

    return "ASP.NET Core browser host";
}

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

static IFileProvider CreateWasmFileProvider(string contentRoot, EmbeddedWasmFileProvider embeddedWasmProvider)
{
    var hasEmbeddedAssets = embeddedWasmProvider.HasIndex;
    var hasPhysicalAssets = File.Exists(Path.Combine(contentRoot, "index.html"));

    if (hasEmbeddedAssets && hasPhysicalAssets)
    {
        return new CompositeFileProvider(
            new PhysicalFileProvider(contentRoot),
            embeddedWasmProvider);
    }

    if (hasPhysicalAssets)
    {
        return new PhysicalFileProvider(contentRoot);
    }

    return embeddedWasmProvider.HasIndex
        ? embeddedWasmProvider
        : new PhysicalFileProvider(contentRoot);
}

static string ResolveContentRoot(string[] args)
{
    var explicitRoot = args.FirstOrDefault(arg =>
        arg.StartsWith("--content-root=", StringComparison.OrdinalIgnoreCase));
    if (explicitRoot is not null)
    {
        var value = explicitRoot["--content-root=".Length..].Trim('"');
        if (!string.IsNullOrWhiteSpace(value))
        {
            return NormalizeContentRoot(value);
        }
    }

    var contentRootIndex = Array.FindIndex(args, arg =>
        string.Equals(arg, "--content-root", StringComparison.OrdinalIgnoreCase));
    if (contentRootIndex >= 0 &&
        contentRootIndex + 1 < args.Length &&
        !string.IsNullOrWhiteSpace(args[contentRootIndex + 1]))
    {
        return NormalizeContentRoot(args[contentRootIndex + 1].Trim('"'));
    }

    var baseDirectory = AppContext.BaseDirectory;
    var publishRoot = Path.GetFullPath(Path.Combine(baseDirectory, "wwwroot"));
    if (File.Exists(Path.Combine(publishRoot, "index.html")))
    {
        return publishRoot;
    }

    var nestedPublishRoot = Path.Combine(publishRoot, "wwwroot");
    if (File.Exists(Path.Combine(nestedPublishRoot, "index.html")))
    {
        return nestedPublishRoot;
    }

    var siblingArtifactRoot = Path.GetFullPath(Path.Combine(baseDirectory, "..", "wwwroot"));
    if (File.Exists(Path.Combine(siblingArtifactRoot, "index.html")))
    {
        return siblingArtifactRoot;
    }

    var nestedSiblingArtifactRoot = Path.Combine(siblingArtifactRoot, "wwwroot");
    if (File.Exists(Path.Combine(nestedSiblingArtifactRoot, "index.html")))
    {
        return nestedSiblingArtifactRoot;
    }

    var debugBuildRoot = Path.GetFullPath(Path.Combine(
        baseDirectory,
        "..",
        "..",
        "..",
        "..",
        "IoTCoWork.Workbench",
        "bin",
        "Debug",
        "net10.0",
        "wwwroot"));

    if (File.Exists(Path.Combine(debugBuildRoot, "index.html")))
    {
        return debugBuildRoot;
    }

    return publishRoot;
}

static string NormalizeContentRoot(string path)
{
    var fullPath = Path.GetFullPath(path);
    if (File.Exists(Path.Combine(fullPath, "index.html")))
    {
        return fullPath;
    }

    var nestedWwwroot = Path.Combine(fullPath, "wwwroot");
    return File.Exists(Path.Combine(nestedWwwroot, "index.html"))
        ? nestedWwwroot
        : fullPath;
}

#if WINDOWS
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
#endif

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
