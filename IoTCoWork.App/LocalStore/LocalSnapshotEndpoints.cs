using System.Text.Json;
using System.Text;
using System.Diagnostics;
using IoTCoWork.App;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace IoTCoWork.App.LocalStore;

public static class LocalSnapshotEndpoints
{
    private const long MaxImageBytes = 80 * 1024 * 1024;

    public static void MapLocalSnapshotEndpoints(this WebApplication app)
    {
        app.MapGet("/api/local/health", () =>
            Results.Json(
                new LocalHealthResponse("ok", DateTimeOffset.UtcNow),
                HostJsonSerializerContext.Default.LocalHealthResponse));

        app.MapGet("/api/local/snapshot", async (
            ILocalSnapshotStore store,
            CancellationToken cancellationToken) =>
        {
            var snapshotJson = await store.LoadAsync(cancellationToken);
            return snapshotJson is null
                ? Results.NoContent()
                : Results.Text(snapshotJson, "application/json; charset=utf-8");
        });

        app.MapPut("/api/local/snapshot", async (
            HttpRequest request,
            ILocalSnapshotStore store,
            CancellationToken cancellationToken) =>
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            var json = document.RootElement.GetRawText();

            await store.SaveAsync(json, cancellationToken);
            return Results.NoContent();
        });

        app.MapDelete("/api/local/snapshot", async (
            ILocalSnapshotStore store,
            CancellationToken cancellationToken) =>
        {
            return await store.DeleteAsync(cancellationToken)
                ? Results.Ok()
                : Results.NoContent();
        });

        app.MapGet("/api/local/image-data", async (
            string url,
            IHttpClientFactory httpClientFactory,
            CancellationToken cancellationToken) =>
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https"))
            {
                return Results.BadRequest(new LocalErrorResponse("图片地址无效。"));
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await httpClientFactory.CreateClient("image-persist")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Results.StatusCode((int)response.StatusCode);
            }

            if (response.Content.Headers.ContentLength is > MaxImageBytes)
            {
                return Results.BadRequest(new LocalErrorResponse("图片超过本地保存大小限制。"));
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.LongLength > MaxImageBytes)
            {
                return Results.BadRequest(new LocalErrorResponse("图片超过本地保存大小限制。"));
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrWhiteSpace(contentType) ||
                !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                contentType = GuessImageContentType(uri.AbsolutePath);
            }

            return Results.Json(
                new ImageDataResponse($"data:{contentType};base64,{Convert.ToBase64String(bytes)}"),
                HostJsonSerializerContext.Default.ImageDataResponse);
        });

        app.MapPost("/api/local/download-image", async (
            LocalDownloadImageRequest request,
            IHttpClientFactory httpClientFactory,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var image = await ReadDownloadImageAsync(request, httpClientFactory, cancellationToken);
                var path = await SaveDownloadImageAsync(request.FileName, image, cancellationToken);
                return Results.Json(
                    new LocalDownloadImageResponse(path, Path.GetFileName(path)),
                    HostJsonSerializerContext.Default.LocalDownloadImageResponse);
            }
            catch (LocalDownloadException ex)
            {
                return Results.BadRequest(new LocalErrorResponse(ex.Message));
            }
            catch (UnauthorizedAccessException)
            {
                return Results.BadRequest(new LocalErrorResponse("没有权限写入下载目录。"));
            }
            catch (IOException ex)
            {
                return Results.BadRequest(new LocalErrorResponse($"保存图片失败：{ex.Message}"));
            }
        });

        app.MapPost("/api/local/reveal-file", (
            LocalRevealFileRequest request) =>
        {
            try
            {
                var path = ResolveRevealFilePath(request.FilePath);
                RevealInFileManager(path);
                return Results.NoContent();
            }
            catch (LocalDownloadException ex)
            {
                return Results.BadRequest(new LocalErrorResponse(ex.Message));
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                return Results.BadRequest(new LocalErrorResponse($"打开文件位置失败：{ex.Message}"));
            }
        });
    }

    private static async Task<LocalImagePayload> ReadDownloadImageAsync(
        LocalDownloadImageRequest request,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.DataUrl))
        {
            return ReadDataUrl(request.DataUrl);
        }

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new LocalDownloadException("图片地址无效。");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await httpClientFactory.CreateClient("image-persist")
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new LocalDownloadException($"图片下载失败：{(int)response.StatusCode}。");
        }

        if (response.Content.Headers.ContentLength is > MaxImageBytes)
        {
            throw new LocalDownloadException("图片超过本地保存大小限制。");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.LongLength > MaxImageBytes)
        {
            throw new LocalDownloadException("图片超过本地保存大小限制。");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (string.IsNullOrWhiteSpace(contentType) ||
            !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            contentType = GuessImageContentType(uri.AbsolutePath);
        }

        return new LocalImagePayload(bytes, contentType);
    }

    private static LocalImagePayload ReadDataUrl(string value)
    {
        var dataUrl = value.Trim();
        if (!dataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            throw new LocalDownloadException("图片数据无效。");
        }

        var comma = dataUrl.IndexOf(',');
        if (comma < 0)
        {
            throw new LocalDownloadException("图片数据无效。");
        }

        var metadata = dataUrl[5..comma];
        var encoded = dataUrl[(comma + 1)..];
        var parts = metadata.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var contentType = parts.FirstOrDefault(part => part.Contains('/', StringComparison.Ordinal)) ?? "image/png";
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new LocalDownloadException("只支持保存图片。");
        }

        byte[] bytes;
        if (parts.Any(part => part.Equals("base64", StringComparison.OrdinalIgnoreCase)))
        {
            bytes = Convert.FromBase64String(encoded);
        }
        else
        {
            bytes = Encoding.UTF8.GetBytes(Uri.UnescapeDataString(encoded));
        }

        if (bytes.LongLength > MaxImageBytes)
        {
            throw new LocalDownloadException("图片超过本地保存大小限制。");
        }

        return new LocalImagePayload(bytes, contentType);
    }

    private static async Task<string> SaveDownloadImageAsync(
        string? requestedFileName,
        LocalImagePayload image,
        CancellationToken cancellationToken)
    {
        var directory = ResolveDownloadsDirectory();
        Directory.CreateDirectory(directory);

        var fileName = SanitizeDownloadFileName(requestedFileName, image.ContentType);
        var path = GetUniqueFilePath(directory, fileName);
        await File.WriteAllBytesAsync(path, image.Bytes, cancellationToken);
        return path;
    }

    private static string ResolveDownloadsDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.Combine(userProfile, "Downloads");
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents))
        {
            return documents;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IoTCoWork",
            "ImageStudio",
            "Downloads");
    }

    private static string ResolveRevealFilePath(string? requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            throw new LocalDownloadException("文件路径无效。");
        }

        var path = Path.GetFullPath(requestedPath);
        if (!File.Exists(path))
        {
            throw new LocalDownloadException("文件不存在，可能已被移动或删除。");
        }

        var downloadsDirectory = Path.GetFullPath(ResolveDownloadsDirectory());
        if (!IsPathWithinDirectory(path, downloadsDirectory))
        {
            throw new LocalDownloadException("只能打开 IoTCoWork 保存到下载目录的图片。");
        }

        return path;
    }

    private static bool IsPathWithinDirectory(string path, string directory)
    {
        var relativePath = Path.GetRelativePath(directory, path);
        return relativePath.Length > 0 &&
            !relativePath.StartsWith("..", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relativePath);
    }

    private static void RevealInFileManager(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            StartShellProcess("explorer.exe", $"/select,{path}");
            return;
        }

        var directory = Path.GetDirectoryName(path) ?? ResolveDownloadsDirectory();
        if (OperatingSystem.IsMacOS())
        {
            StartShellProcess("open", "-R", path);
            return;
        }

        StartShellProcess("xdg-open", directory);
    }

    private static void StartShellProcess(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);

        if (process is null)
        {
            throw new InvalidOperationException("无法启动系统文件管理器。");
        }
    }

    private static string SanitizeDownloadFileName(string? requestedFileName, string contentType)
    {
        var fileName = Path.GetFileName((requestedFileName ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "IoTCoWork-image";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var invalidChar in invalidChars)
        {
            fileName = fileName.Replace(invalidChar, '_');
        }

        fileName = fileName.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "IoTCoWork-image";
        }

        var extension = Path.GetExtension(fileName);
        if (!IsKnownImageExtension(extension))
        {
            fileName = Path.GetFileNameWithoutExtension(fileName) + ExtensionForContentType(contentType);
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        extension = Path.GetExtension(fileName);
        if (name.Length > 96)
        {
            name = name[..96];
        }

        return name + extension;
    }

    private static bool IsKnownImageExtension(string extension)
    {
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".svg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtensionForContentType(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/svg+xml" => ".svg",
            "image/bmp" => ".bmp",
            _ => ".png",
        };
    }

    private static string GetUniqueFilePath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            return path;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; index < 1000; index++)
        {
            path = Path.Combine(directory, $"{name}-{index}{extension}");
            if (!File.Exists(path))
            {
                return path;
            }
        }

        return Path.Combine(directory, $"{name}-{Guid.NewGuid():N}{extension}");
    }

    private static string GuessImageContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            _ => "image/png",
        };
    }

    private sealed record LocalImagePayload(byte[] Bytes, string ContentType);

    private sealed class LocalDownloadException : Exception
    {
        public LocalDownloadException(string message)
            : base(message)
        {
        }
    }
}
