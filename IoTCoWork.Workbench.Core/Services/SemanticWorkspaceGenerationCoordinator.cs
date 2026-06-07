using System.Text.Json;
using IoTSharp.Contracts.Semantic;
using IoTSharp.SaaS.Contracts;
using IoTSharp.SaaS.Contracts.WorkspaceGeneration;

namespace IoTCoWork.Workbench.Services;

public interface ISemanticWorkspaceGenerationClient
{
    Task<WorkspaceGenerationTaskDto> CreateTaskAsync(
        string baseAddress,
        CreateWorkspaceGenerationTaskRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceGenerationTaskDto?> GetTaskAsync(
        string baseAddress,
        Guid taskId,
        string? tenantId,
        string? userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkspaceGenerationArtifactDto>?> GetArtifactsAsync(
        string baseAddress,
        Guid taskId,
        string? tenantId,
        string? userId,
        CancellationToken cancellationToken = default);
}

public sealed class SemanticWorkspaceGenerationCoordinator
{
    public const string DefaultWorkspaceId = "iotcowork-semantic-workspace";
    public const string DefaultProjectId = "iotcowork-semantic-project";
    public const string DefaultTenantId = "tenant-demo";
    public const string DefaultUserId = "iotcowork-local";
    public const string DefaultTargetId = "iotedge-csharp-aot-linux-x64";
    public const string UnsupportedPreviewTargetId = "preview-unsupported-embedded-c";

    private readonly ISemanticWorkspaceGenerationClient _client;

    public SemanticWorkspaceGenerationCoordinator(ISemanticWorkspaceGenerationClient client)
    {
        _client = client;
    }

    public SemanticWorkspaceGenerationState CreateInitialState(SemanticModel? semanticModel)
    {
        var workspace = BuildWorkspace(semanticModel);
        var report = SemanticWorkspaceValidator.CreateReport(workspace);

        return new SemanticWorkspaceGenerationState
        {
            Workspace = workspace,
            ValidationReport = report,
            Phase = report.IsValid
                ? SemanticWorkspaceGenerationPhase.Ready
                : SemanticWorkspaceGenerationPhase.ValidationFailed,
            Message = report.IsValid ? "语义工作区可提交生成。" : "语义校验未通过，请先补齐诊断项。"
        };
    }

    public SemanticWorkspaceGenerationState Validate(SemanticModel? semanticModel)
        => CreateInitialState(semanticModel);

    public async Task<SemanticWorkspaceGenerationState> SubmitAsync(
        string baseAddress,
        SemanticModel? semanticModel,
        bool unsupportedTargetPreview = false,
        CancellationToken cancellationToken = default)
    {
        var state = CreateInitialState(semanticModel);
        if (!state.ValidationReport.IsValid)
        {
            return state with
            {
                Phase = SemanticWorkspaceGenerationPhase.ValidationFailed,
                Message = "语义校验失败，未提交到 SaaS Workspace API。"
            };
        }

        var project = SelectProject(state.Workspace);
        var target = unsupportedTargetPreview
            ? project.GenerationTargets.First(target => string.Equals(target.TargetId, UnsupportedPreviewTargetId, StringComparison.Ordinal))
            : project.GenerationTargets.First(target => string.Equals(target.TargetId, DefaultTargetId, StringComparison.Ordinal));

        var runningTask = CreateLocalTaskPreview(
            state.Workspace,
            project,
            target,
            WorkspaceGenerationTaskStatus.Running);

        state = state with
        {
            Phase = SemanticWorkspaceGenerationPhase.Running,
            Task = runningTask,
            Artifacts = [],
            Message = "生成任务已提交，等待 SaaS 返回状态。"
        };

        try
        {
            var task = await _client.CreateTaskAsync(
                baseAddress,
                new CreateWorkspaceGenerationTaskRequest
                {
                    TenantId = state.Workspace.OwnerTenantId ?? DefaultTenantId,
                    RequestedByUserId = DefaultUserId,
                    Workspace = state.Workspace,
                    ProjectId = project.ProjectId,
                    TargetId = target.TargetId
                },
                cancellationToken);

            return ApplyTask(state, task, task.Artifacts);
        }
        catch (Exception exception) when (exception is HttpRequestException or WorkspaceGenerationApiException or TaskCanceledException or InvalidOperationException)
        {
            return state with
            {
                Phase = SemanticWorkspaceGenerationPhase.Failed,
                Task = runningTask with
                {
                    Status = WorkspaceGenerationTaskStatus.Failed,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    ErrorMessage = exception.Message,
                    Artifacts = []
                },
                Artifacts = [],
                Message = $"生成任务失败：{exception.Message}"
            };
        }
    }

    public async Task<SemanticWorkspaceGenerationState> RefreshAsync(
        string baseAddress,
        SemanticWorkspaceGenerationState current,
        CancellationToken cancellationToken = default)
    {
        if (current.Task is null || current.Task.TaskId == Guid.Empty)
        {
            return current with
            {
                Phase = SemanticWorkspaceGenerationPhase.Failed,
                Message = "没有可刷新的 SaaS 生成任务。"
            };
        }

        try
        {
            var task = await _client.GetTaskAsync(
                baseAddress,
                current.Task.TaskId,
                current.Task.TenantId,
                current.Task.RequestedByUserId,
                cancellationToken);

            if (task is null)
            {
                return current with
                {
                    Phase = SemanticWorkspaceGenerationPhase.Failed,
                    Message = "未找到生成任务，可能是租户上下文不匹配或服务已重启。"
                };
            }

            var artifacts = await _client.GetArtifactsAsync(
                baseAddress,
                task.TaskId,
                task.TenantId,
                task.RequestedByUserId,
                cancellationToken);

            return ApplyTask(current, task, artifacts ?? task.Artifacts);
        }
        catch (Exception exception) when (exception is HttpRequestException or WorkspaceGenerationApiException or TaskCanceledException)
        {
            return current with
            {
                Phase = SemanticWorkspaceGenerationPhase.Failed,
                Message = $"刷新任务失败：{exception.Message}"
            };
        }
    }

    public static Workspace BuildWorkspace(SemanticModel? semanticModel)
    {
        var normalizedModel = NormalizeForGeneration(semanticModel ?? CreateStaticSemanticModel());
        var targetPointIds = normalizedModel.SemanticPoints
            .Select(point => point.SemanticId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var targetProcessNodeIds = normalizedModel.ProcessGraph?.Nodes
            .Select(node => node.NodeId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];

        var project = new Project
        {
            ProjectId = DefaultProjectId,
            Name = "IoTCoWork semantic generation draft",
            Description = "Local Semantic Workspace submitted from IoTCoWork. No live telemetry, attributes, events, alarms, or device lifecycle state is included.",
            ExternalIoTSharpInstanceUrl = "https://iotsharp.example.com",
            ExternalIoTSharpReferences =
            [
                new ExternalIoTSharpReference
                {
                    ReferenceId = "iotsharp-user-instance",
                    Kind = ExternalIoTSharpReferenceKind.Device,
                    IoTSharpInstanceUrl = "https://iotsharp.example.com",
                    OpaqueReference = "user-owned-gateway-context",
                    JumpUrl = "https://iotsharp.example.com/devices/user-owned-gateway-context",
                    DisplayName = "用户自有 IoTSharp 实例",
                    ContextHint = "Navigation-only pointer. Device management remains in the user's IoTSharp instance."
                }
            ],
            SemanticModel = normalizedModel,
            GenerationTargets =
            [
                new GenerationTarget
                {
                    TargetId = DefaultTargetId,
                    Kind = GenerationTargetKind.CSharpAot,
                    RuntimeProfile = "linux-x64",
                    OutputName = "IoTCoWorkSemanticGateway",
                    SemanticPointIds = targetPointIds,
                    ProcessNodeIds = targetProcessNodeIds,
                    Settings =
                    {
                        ["publishAot"] = JsonSerializer.SerializeToElement(true),
                        ["trimMode"] = JsonSerializer.SerializeToElement("full")
                    }
                },
                new GenerationTarget
                {
                    TargetId = UnsupportedPreviewTargetId,
                    Kind = GenerationTargetKind.EmbeddedC,
                    RuntimeProfile = "linux-arm64",
                    OutputName = "UnsupportedPreview",
                    SemanticPointIds = targetPointIds,
                    ProcessNodeIds = targetProcessNodeIds
                }
            ],
            MeteringContext = new MeteringContext
            {
                TenantId = DefaultTenantId,
                WorkspaceId = DefaultWorkspaceId,
                ProjectId = DefaultProjectId,
                Operation = MeteringOperation.CodeGeneration,
                RequestedByUserId = DefaultUserId,
                CorrelationId = "iotcowork-local-semantic-generation",
                Units =
                {
                    ["semanticPoints"] = targetPointIds.Count,
                    ["generationTargets"] = 1,
                    ["artifactRequests"] = 1
                },
                Metadata =
                {
                    ["source"] = JsonSerializer.SerializeToElement("iotcowork.semantic-modeling")
                }
            },
            Metadata =
            {
                ["source"] = JsonSerializer.SerializeToElement("iotcowork.semantic-modeling"),
                ["deviceEntryMode"] = JsonSerializer.SerializeToElement("external-iotsharp-navigation-only")
            }
        };

        return new Workspace
        {
            Schema = "../project-model.v1.schema.json",
            SchemaVersion = ProjectModelJson.SchemaVersion,
            WorkspaceId = DefaultWorkspaceId,
            Name = "IoTCoWork Semantic Workspace",
            Description = "Semantic Workspace generated locally by IoTCoWork for SaaS code generation.",
            OwnerTenantId = DefaultTenantId,
            Projects = [project],
            Metadata =
            {
                ["source"] = JsonSerializer.SerializeToElement("iotcowork.semantic-modeling")
            }
        };
    }

    public static string DescribeStatus(WorkspaceGenerationTaskStatus status) => status switch
    {
        WorkspaceGenerationTaskStatus.Created => "已创建",
        WorkspaceGenerationTaskStatus.Running => "运行中",
        WorkspaceGenerationTaskStatus.Succeeded => "成功",
        WorkspaceGenerationTaskStatus.Failed => "失败",
        WorkspaceGenerationTaskStatus.Canceled => "已取消",
        _ => status.ToString()
    };

    public static string FormatBytes(long value)
    {
        if (value < 1024)
        {
            return $"{value} B";
        }

        if (value < 1024 * 1024)
        {
            return $"{value / 1024d:0.#} KB";
        }

        return $"{value / 1024d / 1024d:0.##} MB";
    }

    public static string ShortHash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "--" : value[..Math.Min(12, value.Length)];

    private static SemanticWorkspaceGenerationState ApplyTask(
        SemanticWorkspaceGenerationState state,
        WorkspaceGenerationTaskDto task,
        IReadOnlyList<WorkspaceGenerationArtifactDto> artifacts)
    {
        return state with
        {
            Phase = task.Status switch
            {
                WorkspaceGenerationTaskStatus.Created => SemanticWorkspaceGenerationPhase.Created,
                WorkspaceGenerationTaskStatus.Running => SemanticWorkspaceGenerationPhase.Running,
                WorkspaceGenerationTaskStatus.Succeeded => SemanticWorkspaceGenerationPhase.Succeeded,
                WorkspaceGenerationTaskStatus.Failed => SemanticWorkspaceGenerationPhase.Failed,
                WorkspaceGenerationTaskStatus.Canceled => SemanticWorkspaceGenerationPhase.Canceled,
                _ => state.Phase
            },
            Task = task,
            Artifacts = artifacts,
            Message = task.Status switch
            {
                WorkspaceGenerationTaskStatus.Succeeded => $"生成成功，工件 {artifacts.Count} 个。",
                WorkspaceGenerationTaskStatus.Failed => string.IsNullOrWhiteSpace(task.ErrorMessage) ? "生成任务失败。" : $"生成任务失败：{task.ErrorMessage}",
                WorkspaceGenerationTaskStatus.Canceled => "生成任务已取消。",
                WorkspaceGenerationTaskStatus.Running => "生成任务运行中。",
                WorkspaceGenerationTaskStatus.Created => "生成任务已创建。",
                _ => DescribeStatus(task.Status)
            }
        };
    }

    private static WorkspaceGenerationTaskDto CreateLocalTaskPreview(
        Workspace workspace,
        Project project,
        GenerationTarget target,
        WorkspaceGenerationTaskStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        return new WorkspaceGenerationTaskDto
        {
            TaskId = Guid.Empty,
            Status = status,
            TenantId = workspace.OwnerTenantId ?? DefaultTenantId,
            RequestedByUserId = DefaultUserId,
            WorkspaceId = workspace.WorkspaceId,
            ProjectId = project.ProjectId,
            TargetId = target.TargetId,
            TargetKind = ToContractName(target.Kind),
            RuntimeProfile = target.RuntimeProfile,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Artifacts = []
        };
    }

    private static Project SelectProject(Workspace workspace)
        => workspace.Projects.FirstOrDefault()
            ?? throw new InvalidOperationException("Semantic Workspace 缺少项目。");

    private static SemanticModel NormalizeForGeneration(SemanticModel model)
    {
        var points = model.SemanticPoints.Select(point =>
        {
            var metadata = new Dictionary<string, JsonElement>(point.Metadata);
            var controlPolicyId = ControlPolicyIdFor(point);
            if (!string.IsNullOrWhiteSpace(controlPolicyId) && !metadata.ContainsKey("controlPolicyId"))
            {
                metadata["controlPolicyId"] = JsonSerializer.SerializeToElement(controlPolicyId);
            }

            return point with
            {
                Metadata = metadata
            };
        }).ToList();

        var processGraph = model.ProcessGraph ?? CreateProcessGraph(model with { SemanticPoints = points });
        var controlPolicies = processGraph.ControlPolicies.ToList();
        foreach (var point in points.Where(point => RequiresControlPolicy(point)))
        {
            var policyId = ControlPolicyIdFor(point);
            if (controlPolicies.Any(policy =>
                    policy.AppliesToSemanticIds.Contains(point.SemanticId, StringComparer.Ordinal)
                    || string.Equals(policy.ControlPolicyId, policyId, StringComparison.Ordinal)))
            {
                continue;
            }

            controlPolicies.Add(new ControlPolicy
            {
                ControlPolicyId = policyId,
                Name = $"{point.DisplayName ?? point.Name} control policy",
                AppliesToSemanticIds = [point.SemanticId],
                Risk = ControlRisk.Hazardous,
                RequiresApproval = true,
                AiOperationMode = AiOperationMode.RecommendOnly
            });
        }

        processGraph = processGraph with
        {
            ControlPolicies = controlPolicies
        };

        return model with
        {
            SchemaVersion = SemanticCoreJson.SchemaVersion,
            ModelId = string.IsNullOrWhiteSpace(model.ModelId) ? "semantic-model-iotcowork-draft" : model.ModelId,
            Name = string.IsNullOrWhiteSpace(model.Name) ? "IoTCoWork semantic draft" : model.Name,
            SemanticPoints = points,
            ProcessGraph = processGraph
        };
    }

    private static ProcessGraph CreateProcessGraph(SemanticModel model)
    {
        var primaryAsset = model.SemanticPoints
            .Select(point => point.AssetId)
            .Where(assetId => !string.IsNullOrWhiteSpace(assetId))
            .Select(assetId => model.Assets.FirstOrDefault(asset => string.Equals(asset.AssetId, assetId, StringComparison.Ordinal)))
            .FirstOrDefault(asset => asset is not null);

        var nodes = new List<ProcessNode>
        {
            new()
            {
                NodeId = primaryAsset is null ? "node-semantic-points" : $"node-{SafeId(primaryAsset.AssetId)}",
                Name = primaryAsset?.DisplayName ?? primaryAsset?.Name ?? "Semantic points",
                NodeType = primaryAsset is null ? "semantic-draft" : ToProcessNodeType(primaryAsset.AssetType),
                AssetId = primaryAsset?.AssetId,
                InputSemanticIds = model.SemanticPoints
                    .Where(point => IsWritable(point.Access))
                    .Select(point => point.SemanticId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                OutputSemanticIds = model.SemanticPoints
                    .Where(point => !IsWritable(point.Access))
                    .Select(point => point.SemanticId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                Tags = ["iotcowork-draft"]
            }
        };

        var controlPolicies = model.SemanticPoints
            .Where(RequiresControlPolicy)
            .Select(point => new ControlPolicy
            {
                ControlPolicyId = ControlPolicyIdFor(point),
                Name = $"{point.DisplayName ?? point.Name} control policy",
                AppliesToSemanticIds = [point.SemanticId],
                Risk = ControlRisk.Hazardous,
                RequiresApproval = true,
                AiOperationMode = AiOperationMode.RecommendOnly
            })
            .ToList();

        return new ProcessGraph
        {
            ProcessGraphId = "process-iotcowork-draft",
            Name = "IoTCoWork process graph draft",
            Description = "Minimum L3 draft generated by IoTCoWork for Semantic Workspace submission.",
            Nodes = nodes,
            Edges = [],
            DerivedPoints = [],
            StateModels = [],
            Alarms = [],
            ControlPolicies = controlPolicies,
            Metadata =
            {
                ["source"] = JsonSerializer.SerializeToElement("iotcowork.generated-minimal-l3")
            }
        };
    }

    private static SemanticModel CreateStaticSemanticModel()
    {
        return new SemanticModel
        {
            SchemaVersion = SemanticCoreJson.SchemaVersion,
            ModelId = "semantic-model-iotcowork-static",
            Name = "IoTCoWork static semantic draft",
            Description = "Local static draft used before a protocol import is selected.",
            Assets =
            [
                new Asset
                {
                    AssetId = "asset-plant-a",
                    Name = "plant_a",
                    DisplayName = "工厂 A",
                    AssetType = SemanticAssetType.Site,
                    AssetPath = ["plant-a"],
                    Points = []
                },
                new Asset
                {
                    AssetId = "asset-compressor-unit-01",
                    Name = "compressor_unit_01",
                    DisplayName = "空压站 1 号机",
                    AssetType = SemanticAssetType.Device,
                    ParentAssetId = "asset-plant-a",
                    AssetPath = ["plant-a", "energy", "compressor-station-01", "unit-01"],
                    Points =
                    [
                        "compressor.unit01.outlet.temperature",
                        "compressor.unit01.outlet.pressure",
                        "compressor.unit01.running.state",
                        "compressor.unit01.start.command"
                    ]
                }
            ],
            SemanticPoints =
            [
                CreateStaticPoint("compressor.unit01.outlet.temperature", "outlet_temperature", "出口温度", "temperature", "thermodynamic-temperature", "Cel", SemanticDataType.Float, SemanticPointAccess.Read, "asset-compressor-unit-01", "modbus.unit01.temp"),
                CreateStaticPoint("compressor.unit01.outlet.pressure", "outlet_pressure", "出口压力", "pressure", "pressure", "bar", SemanticDataType.Float, SemanticPointAccess.Read, "asset-compressor-unit-01", "modbus.unit01.pressure"),
                CreateStaticPoint("compressor.unit01.running.state", "running_state", "运行状态", "state", "dimensionless", "1", SemanticDataType.Boolean, SemanticPointAccess.Read, "asset-compressor-unit-01", "mqtt.unit01.state"),
                CreateStaticPoint("compressor.unit01.start.command", "start_command", "启动命令", "command", "dimensionless", "1", SemanticDataType.Boolean, SemanticPointAccess.Command, "asset-compressor-unit-01", "opcua.unit01.start", "control-policy-start-command")
            ],
            ProtocolBindings =
            [
                new ProtocolBinding
                {
                    BindingId = "modbus.unit01.temp",
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
                },
                new ProtocolBinding
                {
                    BindingId = "modbus.unit01.pressure",
                    ProtocolKind = SemanticProtocolKind.ModbusTcp,
                    EndpointRef = "endpoint-main-plc",
                    Address = "holding-register:40003",
                    SourceDataType = SemanticDataType.Float,
                    Modbus = new ModbusBinding
                    {
                        FunctionCode = 3,
                        RegisterType = ModbusRegisterType.HoldingRegister,
                        Address = 2,
                        UnitId = 1,
                        RegisterCount = 2,
                        ByteOrder = ModbusByteOrder.BigEndian,
                        WordOrder = ModbusWordOrder.BigEndian,
                        Scale = 0.01m,
                        Offset = 0
                    }
                },
                new ProtocolBinding
                {
                    BindingId = "mqtt.unit01.state",
                    ProtocolKind = SemanticProtocolKind.Mqtt,
                    EndpointRef = "mqtt-endpoint.draft",
                    Address = "uns/plant-a/energy/compressor-station-01/unit-01/compressor-unit01-running-state",
                    SourceDataType = SemanticDataType.Boolean,
                    Mqtt = new MqttBinding
                    {
                        Topic = "uns/plant-a/energy/compressor-station-01/unit-01/compressor-unit01-running-state",
                        NamespaceStyle = MqttNamespaceStyle.Uns,
                        PayloadSchema = MqttPayloadSchema.Json,
                        ValueField = "$.value",
                        QualityField = "$.quality"
                    }
                },
                new ProtocolBinding
                {
                    BindingId = "opcua.unit01.start",
                    ProtocolKind = SemanticProtocolKind.Local,
                    EndpointRef = "local-command-draft",
                    Address = "local:compressor.unit01.start.command",
                    SourceDataType = SemanticDataType.Boolean
                }
            ],
            Quantities =
            [
                new Quantity { QuantityKind = "temperature", Name = "Temperature", Dimension = "thermodynamic-temperature", Standard = "ucum" },
                new Quantity { QuantityKind = "pressure", Name = "Pressure", Dimension = "pressure", Standard = "ucum" },
                new Quantity { QuantityKind = "state", Name = "State", Dimension = "dimensionless" },
                new Quantity { QuantityKind = "command", Name = "Command", Dimension = "dimensionless" }
            ],
            Units =
            [
                new Unit { Code = "Cel", DisplayName = "degree Celsius", Symbol = "C", System = UnitSystem.Ucum, QuantityKind = "temperature" },
                new Unit { Code = "bar", DisplayName = "bar", Symbol = "bar", System = UnitSystem.Ucum, QuantityKind = "pressure" },
                new Unit { Code = "1", DisplayName = "dimensionless", Symbol = "1", System = UnitSystem.Ucum, QuantityKind = "state" }
            ]
        };
    }

    private static SemanticPoint CreateStaticPoint(
        string semanticId,
        string name,
        string displayName,
        string quantityKind,
        string dimension,
        string unit,
        SemanticDataType dataType,
        SemanticPointAccess access,
        string assetId,
        string bindingId,
        string? controlPolicyId = null)
    {
        var metadata = new Dictionary<string, JsonElement>();
        if (!string.IsNullOrWhiteSpace(controlPolicyId))
        {
            metadata["controlPolicyId"] = JsonSerializer.SerializeToElement(controlPolicyId);
        }

        return new SemanticPoint
        {
            SemanticId = semanticId,
            Name = name,
            DisplayName = displayName,
            AssetId = assetId,
            Quantity = new Quantity
            {
                QuantityKind = quantityKind,
                Name = quantityKind,
                Dimension = dimension
            },
            Unit = new Unit
            {
                Code = unit,
                DisplayName = unit,
                Symbol = unit,
                System = UnitSystem.Ucum,
                QuantityKind = quantityKind
            },
            DataType = dataType,
            Access = access,
            Quality = new Quality
            {
                Status = QualityStatus.Unknown,
                Source = "not-provided"
            },
            Source = new ProtocolSource
            {
                BindingId = bindingId,
                Role = "primary"
            },
            Tags = ["iotcowork-draft"],
            Metadata = metadata
        };
    }

    private static bool RequiresControlPolicy(SemanticPoint point)
        => point.Access is SemanticPointAccess.Command or SemanticPointAccess.Config
            || (IsWritable(point.Access)
                && (MetadataEquals(point.Metadata, "risk", "hazardous")
                    || MetadataEquals(point.Metadata, "controlRisk", "hazardous")
                    || MetadataFlag(point.Metadata, "hazardous")
                    || MetadataFlag(point.Metadata, "requiresApproval")));

    private static bool IsWritable(SemanticPointAccess access)
        => access is SemanticPointAccess.Write or SemanticPointAccess.ReadWrite or SemanticPointAccess.Command or SemanticPointAccess.Config;

    private static string ControlPolicyIdFor(SemanticPoint point)
    {
        if (point.Metadata.TryGetValue("controlPolicyId", out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return $"control-policy-{SafeId(point.SemanticId)}";
    }

    private static string ToProcessNodeType(SemanticAssetType assetType)
        => assetType switch
        {
            SemanticAssetType.Site => "site",
            SemanticAssetType.Area => "area",
            SemanticAssetType.Line => "line",
            SemanticAssetType.Device => "device",
            SemanticAssetType.Component => "component",
            SemanticAssetType.Sensor => "sensor",
            SemanticAssetType.Actuator => "actuator",
            _ => "custom"
        };

    private static string ToContractName(GenerationTargetKind value)
        => value switch
        {
            GenerationTargetKind.CSharpAot => "csharpAot",
            GenerationTargetKind.EmbeddedC => "embeddedC",
            GenerationTargetKind.BasicScript => "basicScript",
            GenerationTargetKind.Custom => "custom",
            _ => value.ToString()
        };

    private static string SafeId(string value)
    {
        var characters = value
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray();
        var collapsed = string.Join('-', new string(characters).Split('-', StringSplitOptions.RemoveEmptyEntries));

        return string.IsNullOrWhiteSpace(collapsed) ? "draft" : collapsed;
    }

    private static bool MetadataEquals(IReadOnlyDictionary<string, JsonElement> metadata, string key, string expected)
        => metadata.TryGetValue(key, out var value)
            && value.ValueKind == JsonValueKind.String
            && string.Equals(value.GetString(), expected, StringComparison.OrdinalIgnoreCase);

    private static bool MetadataFlag(IReadOnlyDictionary<string, JsonElement> metadata, string key)
        => metadata.TryGetValue(key, out var value)
            && value.ValueKind == JsonValueKind.True;
}

public sealed record SemanticWorkspaceGenerationState
{
    public required Workspace Workspace { get; init; }

    public required WorkspaceValidationReport ValidationReport { get; init; }

    public SemanticWorkspaceGenerationPhase Phase { get; init; } = SemanticWorkspaceGenerationPhase.Ready;

    public WorkspaceGenerationTaskDto? Task { get; init; }

    public IReadOnlyList<WorkspaceGenerationArtifactDto> Artifacts { get; init; } = [];

    public string Message { get; init; } = string.Empty;

    public bool IsBusy => Phase is SemanticWorkspaceGenerationPhase.Created or SemanticWorkspaceGenerationPhase.Running;

    public bool CanSubmit => ValidationReport.IsValid && !IsBusy;

    public string StatusLabel => Task is null ? "待提交" : SemanticWorkspaceGenerationCoordinator.DescribeStatus(Task.Status);
}

public enum SemanticWorkspaceGenerationPhase
{
    Ready,
    ValidationFailed,
    Created,
    Running,
    Succeeded,
    Failed,
    Canceled
}
