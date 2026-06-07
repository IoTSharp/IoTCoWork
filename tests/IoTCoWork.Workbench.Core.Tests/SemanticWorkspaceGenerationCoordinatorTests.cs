using IoTCoWork.Workbench.Services;
using IoTSharp.Contracts.Semantic;
using IoTSharp.SaaS.Contracts;
using IoTSharp.SaaS.Contracts.WorkspaceGeneration;

namespace IoTCoWork.Workbench.Core.Tests;

public sealed class SemanticWorkspaceGenerationCoordinatorTests
{
    [Fact]
    public void CreateInitialState_WithoutImportedModel_UsesValidStaticDraft()
    {
        var coordinator = new SemanticWorkspaceGenerationCoordinator(new FakeGenerationClient());

        var state = coordinator.CreateInitialState(null);

        Assert.True(state.ValidationReport.IsValid, string.Join(Environment.NewLine, state.ValidationReport.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
        Assert.Equal(SemanticWorkspaceGenerationPhase.Ready, state.Phase);
        Assert.Equal(SemanticWorkspaceGenerationCoordinator.DefaultWorkspaceId, state.Workspace.WorkspaceId);
        Assert.Equal(SemanticWorkspaceGenerationCoordinator.DefaultTargetId, state.Workspace.Projects[0].GenerationTargets[0].TargetId);
    }

    [Fact]
    public async Task SubmitAsync_WithValidDraft_CreatesGenerationTask()
    {
        var client = new FakeGenerationClient
        {
            CreateResponse = CreateTask(WorkspaceGenerationTaskStatus.Created)
        };
        var coordinator = new SemanticWorkspaceGenerationCoordinator(client);

        var state = await coordinator.SubmitAsync("http://localhost:5091", CreateValidSemanticModel());

        Assert.True(state.ValidationReport.IsValid, string.Join(Environment.NewLine, state.ValidationReport.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(SemanticWorkspaceGenerationPhase.Created, state.Phase);
        Assert.NotNull(state.Task);
        Assert.Equal(WorkspaceGenerationTaskStatus.Created, state.Task.Status);
        Assert.Single(client.CreateRequests);
        Assert.Equal(SemanticWorkspaceGenerationCoordinator.DefaultTargetId, client.CreateRequests[0].TargetId);
    }

    [Fact]
    public async Task RefreshAsync_WhenApiReturnsRunning_KeepsRunningState()
    {
        var task = CreateTask(WorkspaceGenerationTaskStatus.Running);
        var client = new FakeGenerationClient
        {
            GetResponse = task
        };
        var coordinator = new SemanticWorkspaceGenerationCoordinator(client);
        var current = coordinator.CreateInitialState(CreateValidSemanticModel()) with
        {
            Task = task,
            Phase = SemanticWorkspaceGenerationPhase.Running
        };

        var refreshed = await coordinator.RefreshAsync("http://localhost:5091", current);

        Assert.Equal(SemanticWorkspaceGenerationPhase.Running, refreshed.Phase);
        Assert.Equal(WorkspaceGenerationTaskStatus.Running, refreshed.Task?.Status);
        Assert.Empty(refreshed.Artifacts);
    }

    [Fact]
    public async Task RefreshAsync_WhenApiReturnsSucceeded_ShowsArtifacts()
    {
        var task = CreateTask(WorkspaceGenerationTaskStatus.Succeeded);
        var artifact = CreateArtifact(task.TaskId);
        var client = new FakeGenerationClient
        {
            GetResponse = task,
            ArtifactsResponse = [artifact]
        };
        var coordinator = new SemanticWorkspaceGenerationCoordinator(client);
        var current = coordinator.CreateInitialState(CreateValidSemanticModel()) with
        {
            Task = task,
            Phase = SemanticWorkspaceGenerationPhase.Running
        };

        var refreshed = await coordinator.RefreshAsync("http://localhost:5091", current);

        Assert.Equal(SemanticWorkspaceGenerationPhase.Succeeded, refreshed.Phase);
        Assert.Equal(WorkspaceGenerationTaskStatus.Succeeded, refreshed.Task?.Status);
        Assert.Equal([artifact], refreshed.Artifacts);
        Assert.Contains("工件 1 个", refreshed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmitAsync_WhenApiReturnsFailed_ShowsFailureMessage()
    {
        var client = new FakeGenerationClient
        {
            CreateResponse = CreateTask(
                WorkspaceGenerationTaskStatus.Failed,
                errorMessage: "Generation target kind 'EmbeddedC' is not supported by the workspace API yet.")
        };
        var coordinator = new SemanticWorkspaceGenerationCoordinator(client);

        var state = await coordinator.SubmitAsync(
            "http://localhost:5091",
            CreateValidSemanticModel(),
            unsupportedTargetPreview: true);

        Assert.Equal(SemanticWorkspaceGenerationPhase.Failed, state.Phase);
        Assert.Equal(SemanticWorkspaceGenerationCoordinator.UnsupportedPreviewTargetId, client.CreateRequests[0].TargetId);
        Assert.Contains("not supported", state.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitAsync_WhenValidationFails_DoesNotCallApi()
    {
        var client = new FakeGenerationClient();
        var coordinator = new SemanticWorkspaceGenerationCoordinator(client);
        var invalidModel = CreateValidSemanticModel() with
        {
            SemanticPoints =
            [
                CreateValidSemanticModel().SemanticPoints[0] with
                {
                    Unit = new Unit()
                }
            ]
        };

        var state = await coordinator.SubmitAsync("http://localhost:5091", invalidModel);

        Assert.Equal(SemanticWorkspaceGenerationPhase.ValidationFailed, state.Phase);
        Assert.False(state.ValidationReport.IsValid);
        Assert.Empty(client.CreateRequests);
        Assert.Contains(state.ValidationReport.Diagnostics, diagnostic =>
            diagnostic.Code == SemanticValidationCodes.SemanticPointUnitRequired);
    }

    private static SemanticModel CreateValidSemanticModel()
    {
        return new SemanticModel
        {
            ModelId = "semantic-model-test",
            Name = "Test semantic model",
            Assets =
            [
                new Asset
                {
                    AssetId = "asset-test",
                    Name = "test_asset",
                    DisplayName = "Test Asset",
                    AssetType = SemanticAssetType.Device,
                    AssetPath = ["plant", "asset-test"],
                    Points = ["test.temperature"]
                }
            ],
            SemanticPoints =
            [
                new SemanticPoint
                {
                    SemanticId = "test.temperature",
                    Name = "test_temperature",
                    DisplayName = "Test temperature",
                    AssetId = "asset-test",
                    Quantity = new Quantity
                    {
                        QuantityKind = "temperature",
                        Name = "Temperature",
                        Dimension = "thermodynamic-temperature"
                    },
                    Unit = new Unit
                    {
                        Code = "Cel",
                        DisplayName = "degree Celsius",
                        Symbol = "C",
                        System = UnitSystem.Ucum,
                        QuantityKind = "temperature"
                    },
                    DataType = SemanticDataType.Float,
                    Access = SemanticPointAccess.Read,
                    Quality = new Quality
                    {
                        Status = QualityStatus.Unknown,
                        Source = "not-provided"
                    },
                    Source = new ProtocolSource
                    {
                        BindingId = "binding-test-temperature",
                        Role = "primary"
                    }
                }
            ],
            ProtocolBindings =
            [
                new ProtocolBinding
                {
                    BindingId = "binding-test-temperature",
                    ProtocolKind = SemanticProtocolKind.ModbusTcp,
                    EndpointRef = "endpoint-main-plc",
                    Address = "holding-register:40001",
                    SourceDataType = SemanticDataType.Float,
                    Modbus = new ModbusBinding
                    {
                        FunctionCode = 3,
                        RegisterType = ModbusRegisterType.HoldingRegister,
                        Address = 0,
                        UnitId = 1,
                        RegisterCount = 2,
                        ByteOrder = ModbusByteOrder.BigEndian,
                        WordOrder = ModbusWordOrder.BigEndian,
                        Scale = 0.1m,
                        Offset = 0
                    }
                }
            ]
        };
    }

    private static WorkspaceGenerationTaskDto CreateTask(
        WorkspaceGenerationTaskStatus status,
        string? errorMessage = null)
    {
        var taskId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        return new WorkspaceGenerationTaskDto
        {
            TaskId = taskId,
            Status = status,
            TenantId = SemanticWorkspaceGenerationCoordinator.DefaultTenantId,
            RequestedByUserId = SemanticWorkspaceGenerationCoordinator.DefaultUserId,
            WorkspaceId = SemanticWorkspaceGenerationCoordinator.DefaultWorkspaceId,
            ProjectId = SemanticWorkspaceGenerationCoordinator.DefaultProjectId,
            TargetId = SemanticWorkspaceGenerationCoordinator.DefaultTargetId,
            TargetKind = "csharpAot",
            RuntimeProfile = "linux-x64",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ErrorMessage = errorMessage,
            Artifacts = []
        };
    }

    private static WorkspaceGenerationArtifactDto CreateArtifact(Guid taskId)
    {
        return new WorkspaceGenerationArtifactDto
        {
            ArtifactId = $"artifact-{taskId:N}-source",
            TaskId = taskId,
            TargetId = SemanticWorkspaceGenerationCoordinator.DefaultTargetId,
            Kind = ArtifactKind.SourceArchive,
            FileName = "iotcowork-semantic-gateway.zip",
            ContentType = "application/zip",
            Uri = $"workspace://generation-tasks/{taskId:N}/artifacts/source",
            SizeBytes = 2048,
            Sha256 = new string('a', 64),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            DownloadPath = $"/api/v1/workspace/generation-tasks/{taskId:D}/artifacts/artifact-{taskId:N}-source/download"
        };
    }

    private sealed class FakeGenerationClient : ISemanticWorkspaceGenerationClient
    {
        public List<CreateWorkspaceGenerationTaskRequest> CreateRequests { get; } = [];

        public WorkspaceGenerationTaskDto? CreateResponse { get; init; }

        public WorkspaceGenerationTaskDto? GetResponse { get; init; }

        public IReadOnlyList<WorkspaceGenerationArtifactDto>? ArtifactsResponse { get; init; }

        public Task<WorkspaceGenerationTaskDto> CreateTaskAsync(
            string baseAddress,
            CreateWorkspaceGenerationTaskRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateRequests.Add(request);

            return Task.FromResult(CreateResponse ?? CreateTask(WorkspaceGenerationTaskStatus.Succeeded));
        }

        public Task<WorkspaceGenerationTaskDto?> GetTaskAsync(
            string baseAddress,
            Guid taskId,
            string? tenantId,
            string? userId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GetResponse);
        }

        public Task<IReadOnlyList<WorkspaceGenerationArtifactDto>?> GetArtifactsAsync(
            string baseAddress,
            Guid taskId,
            string? tenantId,
            string? userId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ArtifactsResponse);
        }
    }
}
