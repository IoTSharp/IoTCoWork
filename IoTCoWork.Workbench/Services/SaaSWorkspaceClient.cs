using System.Net.Http.Json;
using IoTSharp.SaaS.Contracts;
using IoTSharp.SaaS.Contracts.WorkspaceGeneration;

namespace IoTCoWork.Workbench.Services;

public sealed class SaaSWorkspaceClient : ISemanticWorkspaceGenerationClient
{
    private const string ExampleWorkspacePath = "data/modbus-tcp-csharp-aot.workspace.json";
    private readonly HttpClient _httpClient;
    private readonly WorkspaceGenerationApiClient _generationClient;

    public SaaSWorkspaceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _generationClient = new WorkspaceGenerationApiClient(httpClient);
    }

    public async Task<Workspace> LoadExampleWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        var workspace = await _httpClient.GetFromJsonAsync<Workspace>(
            ExampleWorkspacePath,
            ProjectModelJson.CreateOptions(),
            cancellationToken);

        return workspace ?? throw new InvalidOperationException("未能加载示例工程模型。");
    }

    public Task<WorkspaceGenerationTaskDto> CreateTaskAsync(
        string baseAddress,
        CreateWorkspaceGenerationTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        return _generationClient.CreateTaskAsync(baseAddress, request, cancellationToken);
    }

    public Task<WorkspaceGenerationTaskDto?> GetTaskAsync(
        string baseAddress,
        Guid taskId,
        string? tenantId,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        return _generationClient.GetTaskAsync(baseAddress, taskId, tenantId, userId, cancellationToken);
    }

    public Task<IReadOnlyList<WorkspaceGenerationArtifactDto>?> GetArtifactsAsync(
        string baseAddress,
        Guid taskId,
        string? tenantId,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        return _generationClient.GetArtifactsAsync(baseAddress, taskId, tenantId, userId, cancellationToken);
    }

    public string BuildArtifactDownloadUrl(string baseAddress, WorkspaceGenerationArtifactDto artifact)
    {
        return WorkspaceGenerationApiClient.BuildArtifactDownloadUrl(baseAddress, artifact);
    }

    public async Task<WorkspaceGenerationTaskDto?> CreateFailurePreviewAsync(CancellationToken cancellationToken = default)
    {
        var workspace = await LoadExampleWorkspaceAsync(cancellationToken);
        var project = workspace.Projects.FirstOrDefault();
        var target = project?.GenerationTargets.FirstOrDefault();
        if (project is null || target is null)
        {
            throw new InvalidOperationException("示例工程模型缺少项目或生成目标。");
        }

        var failedWorkspace = workspace with
        {
            Projects =
            [
                project with
                {
                    GenerationTargets =
                    [
                        target with
                        {
                            Kind = GenerationTargetKind.EmbeddedC,
                            TargetId = "preview-unsupported-embedded-c"
                        }
                    ]
                }
            ]
        };

        try
        {
            return await CreateTaskAsync(
                WorkspaceGenerationClientDefaults.BaseAddress,
                new CreateWorkspaceGenerationTaskRequest
                {
                    TenantId = failedWorkspace.OwnerTenantId ?? "tenant-demo",
                    RequestedByUserId = "iotcowork-preview",
                    Workspace = failedWorkspace,
                    ProjectId = project.ProjectId,
                    TargetId = "preview-unsupported-embedded-c"
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or WorkspaceGenerationApiException or TaskCanceledException)
        {
            return new WorkspaceGenerationTaskDto
            {
                TaskId = Guid.Empty,
                Status = WorkspaceGenerationTaskStatus.Failed,
                TenantId = failedWorkspace.OwnerTenantId ?? "tenant-demo",
                RequestedByUserId = "iotcowork-preview",
                WorkspaceId = failedWorkspace.WorkspaceId,
                ProjectId = project.ProjectId,
                TargetId = "preview-unsupported-embedded-c",
                TargetKind = "embeddedC",
                RuntimeProfile = target.RuntimeProfile,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                ErrorMessage = exception.Message,
                Artifacts = []
            };
        }
    }

    public static string DescribeWorkspace(Workspace workspace)
    {
        var project = workspace.Projects.FirstOrDefault();
        if (project is null)
        {
            return $"{workspace.Name} · 0 projects";
        }

        return $"{workspace.Name} · {project.Name} · {project.Points.Count} points";
    }

    public static string? SelectDefaultProjectId(Workspace workspace)
    {
        return workspace.Projects.FirstOrDefault()?.ProjectId;
    }

    public static string? SelectDefaultTargetId(Workspace workspace, string? projectId)
    {
        return workspace.Projects
            .FirstOrDefault(project => string.Equals(project.ProjectId, projectId, StringComparison.Ordinal))
            ?.GenerationTargets
            .FirstOrDefault()
            ?.TargetId;
    }
}
