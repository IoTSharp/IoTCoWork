using IoTCoWork.Workbench.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace IoTCoWork.App.Updater;

public static class AppUpdateEndpoints
{
    public static void MapAppUpdateEndpoints(this WebApplication app)
    {
        app.MapGet("/api/app/update", async (
            AppUpdateService updateService,
            CancellationToken cancellationToken) =>
        {
            var result = await updateService.CheckAsync(cancellationToken);
            return Results.Json(
                result,
                HostJsonSerializerContext.Default.AppUpdateCheckResponse);
        });

        app.MapPost("/api/app/update/install", async (
            AppUpdateInstallRequest request,
            AppUpdateService updateService,
            IHostApplicationLifetime lifetime,
            CancellationToken cancellationToken) =>
        {
            var result = await updateService.InstallAsync(request, cancellationToken);
            if (string.Equals(result.Status, AppUpdateService.InstallingStatus, StringComparison.Ordinal))
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(800));
                    lifetime.StopApplication();
                });
            }

            return Results.Json(
                result,
                HostJsonSerializerContext.Default.AppUpdateInstallResponse);
        });
    }
}
