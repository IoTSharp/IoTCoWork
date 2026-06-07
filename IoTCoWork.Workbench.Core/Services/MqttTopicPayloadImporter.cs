using System.Globalization;
using System.Text;
using System.Text.Json;
using IoTCoWork.Workbench.Models;
using IoTSharp.Contracts.Semantic;

namespace IoTCoWork.Workbench.Services;

public sealed class MqttTopicPayloadImporter
{
    public static readonly string SampleTopic = "uns/plant-a/energy/compressor-station-01/unit-01/outlet/temperature";

    public static readonly string SamplePayload = """
{
  "value": 42.5,
  "timestamp": "2026-06-07T08:30:00Z",
  "quality": "good"
}
""";

    private const string DraftSource = "iotcowork.mqtt-topic-payload";

    public MqttTopicPayloadImportResult Import(
        string? topic,
        string? payloadJson,
        MqttTopicPayloadImportOptions? importOptions = null)
    {
        var options = importOptions ?? new MqttTopicPayloadImportOptions();
        var issues = new List<MqttTopicPayloadImportIssue>();
        var normalizedTopic = topic?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedTopic))
        {
            issues.Add(Error(MqttTopicPayloadImportFields.Topic, MqttTopicPayloadImportIssueCodes.TopicRequired, "MQTT topic is required."));
        }
        else if (!MqttUnsTopicBuilder.IsValidPublishTopic(normalizedTopic))
        {
            issues.Add(Error(MqttTopicPayloadImportFields.Topic, MqttTopicPayloadImportIssueCodes.TopicInvalid, "MQTT topic must be a publish topic without wildcards, null characters, or empty hierarchy levels."));
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            issues.Add(Error(MqttTopicPayloadImportFields.Payload, MqttTopicPayloadImportIssueCodes.PayloadRequired, "JSON payload sample is required for field-path inference."));
        }

        if (issues.Any(issue => issue.Severity == MqttTopicPayloadImportIssueSeverity.Error))
        {
            return CreateEmptyResult(issues, options);
        }

        using var payload = ParsePayload(payloadJson!, issues);
        if (payload is null)
        {
            return CreateEmptyResult(issues, options);
        }

        var leaves = FlattenPayload(payload.RootElement);
        if (leaves.Count == 0)
        {
            issues.Add(Error(MqttTopicPayloadImportFields.Payload, MqttTopicPayloadImportIssueCodes.PayloadFieldMissing, "JSON payload does not contain primitive value fields."));
            return CreateEmptyResult(issues, options);
        }

        var valueCandidate = SelectFieldCandidate(leaves, MqttTopicPayloadCandidateRoles.ValueField);
        if (valueCandidate is null)
        {
            issues.Add(Error(MqttTopicPayloadImportFields.ValueField, MqttTopicPayloadImportIssueCodes.ValueFieldMissing, "No value field candidate could be inferred from the JSON payload."));
            return CreateEmptyResult(issues, options);
        }

        var timestampCandidate = SelectFieldCandidate(leaves, MqttTopicPayloadCandidateRoles.TimestampField);
        if (timestampCandidate is null)
        {
            issues.Add(Pending(MqttTopicPayloadImportFields.TimestampField, MqttTopicPayloadImportIssueCodes.TimestampFieldPending, "timestampField is missing and must be completed manually when the source timestamp is required."));
        }

        var qualityCandidate = SelectFieldCandidate(leaves, MqttTopicPayloadCandidateRoles.QualityField);
        if (qualityCandidate is null)
        {
            issues.Add(Pending(MqttTopicPayloadImportFields.QualityField, MqttTopicPayloadImportIssueCodes.QualityFieldPending, "qualityField is missing and must be completed manually if payload quality is available elsewhere."));
        }

        var topicSegments = normalizedTopic.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var namespaceStyle = MqttUnsTopicBuilder.IsValidUnsTopic(normalizedTopic)
            ? MqttNamespaceStyle.Uns
            : MqttNamespaceStyle.Custom;
        var assetPathCandidate = InferAssetPathCandidate(topicSegments, namespaceStyle);
        var pointName = InferPointName(topicSegments, valueCandidate);
        var semanticId = InferSemanticId(topicSegments, namespaceStyle, assetPathCandidate, pointName);

        issues.Add(Pending(MqttTopicPayloadImportFields.QuantityKind, MqttTopicPayloadImportIssueCodes.QuantityKindPending, "quantityKind is missing and must be completed manually."));
        issues.Add(Pending(MqttTopicPayloadImportFields.Dimension, MqttTopicPayloadImportIssueCodes.DimensionPending, "dimension is missing and must be completed manually."));
        issues.Add(Pending(MqttTopicPayloadImportFields.Unit, MqttTopicPayloadImportIssueCodes.UnitPending, "unit is missing and must be completed manually."));

        AssetDraft? assetDraft = null;
        if (assetPathCandidate is null)
        {
            issues.Add(Pending(MqttTopicPayloadImportFields.AssetPath, MqttTopicPayloadImportIssueCodes.AssetOwnerPending, "asset ownership is missing and must be completed manually."));
        }
        else
        {
            assetDraft = AssetDraft.FromAssetPath(assetPathCandidate.Segments, semanticId);
            if (namespaceStyle != MqttNamespaceStyle.Uns)
            {
                issues.Add(Pending(MqttTopicPayloadImportFields.AssetPath, MqttTopicPayloadImportIssueCodes.AssetOwnerReview, "assetPath was inferred from a non-standard topic and must be reviewed manually."));
            }
        }

        var bindingId = $"mqtt.{NormalizeIdentifierForId(semanticId)}";
        var sourceDataType = valueCandidate.SemanticDataType;
        var rowIssues = issues.Where(issue => issue.Severity == MqttTopicPayloadImportIssueSeverity.Warning).ToArray();
        var pointStatus = rowIssues.Length == 0 ? "ready" : "pending";
        assetDraft?.Points.Add(semanticId);

        var point = new SemanticPoint
        {
            SemanticId = semanticId,
            Name = NormalizeIdentifierForId(pointName),
            DisplayName = ToDisplayName(pointName),
            AssetId = assetDraft?.AssetId,
            Quantity = new Quantity(),
            Unit = new Unit(),
            DataType = sourceDataType,
            Access = SemanticPointAccess.Read,
            Quality = new Quality
            {
                Status = QualityStatus.Unknown,
                Source = qualityCandidate is null ? "not-provided" : "payload-field",
                FieldPath = qualityCandidate?.FieldPath,
                Reason = "Imported MQTT topic/payload draft."
            },
            Source = new ProtocolSource
            {
                BindingId = bindingId,
                Role = "primary"
            },
            Metadata = ToMetadata(rowIssues)
        };

        var mqtt = new MqttBinding
        {
            Topic = normalizedTopic,
            NamespaceStyle = namespaceStyle,
            PayloadSchema = MqttPayloadSchema.Json,
            ValueField = valueCandidate.FieldPath,
            TimestampField = timestampCandidate?.FieldPath,
            QualityField = qualityCandidate?.FieldPath,
            Retain = false,
            Qos = 0
        };

        var binding = new ProtocolBinding
        {
            BindingId = bindingId,
            ProtocolKind = SemanticProtocolKind.Mqtt,
            EndpointRef = options.EndpointRef,
            Address = normalizedTopic,
            FieldPath = valueCandidate.FieldPath,
            SourceDataType = sourceDataType,
            Polling = new ProtocolPolling
            {
                Subscription = true
            },
            Mqtt = mqtt,
            Quality = point.Quality,
            Metadata = ToMetadata(rowIssues)
        };

        var fieldCandidates = new List<MqttTopicPayloadFieldCandidate> { valueCandidate };
        if (timestampCandidate is not null)
        {
            fieldCandidates.Add(timestampCandidate);
        }

        if (qualityCandidate is not null)
        {
            fieldCandidates.Add(qualityCandidate);
        }

        var model = new SemanticModel
        {
            ModelId = options.ModelId,
            Name = options.ModelName,
            Description = "Draft Semantic Model imported locally from an MQTT topic and JSON payload shape in IoTCoWork.",
            Assets = assetDraft is null ? [] : [assetDraft.ToAsset()],
            SemanticPoints = [point],
            ProtocolBindings = [binding],
            Metadata = new Dictionary<string, JsonElement>
            {
                ["source"] = JsonSerializer.SerializeToElement(DraftSource),
                ["draftStatus"] = JsonSerializer.SerializeToElement(pointStatus),
                ["payloadSampleStored"] = JsonSerializer.SerializeToElement(false)
            }
        };

        return new MqttTopicPayloadImportResult(
            model,
            [
                new SemanticPointDraft(
                    semanticId,
                    point.DisplayName ?? semanticId,
                    "pending",
                    "pending",
                    sourceDataType.ToString(),
                    "read",
                    assetPathCandidate is null ? "pending" : $"{assetPathCandidate.DisplayPath} ({assetPathCandidate.Status})",
                    qualityCandidate is null ? "quality pending" : $"quality: {qualityCandidate.FieldPath}",
                    bindingId,
                    pointStatus,
                    rowIssues.Select(issue => issue.Message).ToArray())
            ],
            [
                new ProtocolBindingDraft(
                    bindingId,
                    namespaceStyle == MqttNamespaceStyle.Uns ? "MQTT UNS" : "MQTT Custom",
                    $"{normalizedTopic} -> {valueCandidate.FieldPath}",
                    semanticId,
                    "Imported locally from topic and JSON payload shape. Payload sample values are not stored.",
                    pointStatus)
            ],
            assetDraft is null ? [] : [assetDraft.ToNode()],
            assetPathCandidate is null ? [] : [assetPathCandidate],
            fieldCandidates,
            issues,
            SemanticModelValidator.Validate(model));
    }

    private static JsonDocument? ParsePayload(string payloadJson, ICollection<MqttTopicPayloadImportIssue> issues)
    {
        try
        {
            var document = JsonDocument.Parse(payloadJson, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (document.RootElement.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array)
            {
                document.Dispose();
                issues.Add(Error(MqttTopicPayloadImportFields.Payload, MqttTopicPayloadImportIssueCodes.PayloadRootUnsupported, "JSON payload root must be an object or array."));
                return null;
            }

            return document;
        }
        catch (JsonException exception)
        {
            issues.Add(Error(MqttTopicPayloadImportFields.Payload, MqttTopicPayloadImportIssueCodes.PayloadInvalidJson, $"JSON payload could not be parsed: {exception.Message}"));
            return null;
        }
    }

    private static IReadOnlyList<JsonLeaf> FlattenPayload(JsonElement root)
    {
        var leaves = new List<JsonLeaf>();
        FlattenPayload(root, "$", [], leaves, depth: 0);
        return leaves;
    }

    private static void FlattenPayload(
        JsonElement element,
        string path,
        IReadOnlyList<string> propertySegments,
        ICollection<JsonLeaf> leaves,
        int depth)
    {
        if (depth > 12 || leaves.Count >= 128)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    FlattenPayload(
                        property.Value,
                        path + ToJsonPathSegment(property.Name),
                        [.. propertySegments, property.Name],
                        leaves,
                        depth + 1);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray().Take(4))
                {
                    FlattenPayload(
                        item,
                        $"{path}[{index}]",
                        propertySegments,
                        leaves,
                        depth + 1);
                    index++;
                }

                break;
            case JsonValueKind.String:
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                leaves.Add(new JsonLeaf(
                    path,
                    propertySegments,
                    element.ValueKind,
                    ToSemanticDataType(element)));
                break;
        }
    }

    private static MqttTopicPayloadFieldCandidate? SelectFieldCandidate(
        IReadOnlyList<JsonLeaf> leaves,
        string role)
    {
        var scored = leaves
            .Select(leaf => new
            {
                Leaf = leaf,
                Score = ScoreFieldCandidate(leaf, role)
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Leaf.PropertySegments.Count)
            .ThenBy(candidate => candidate.Leaf.Path, StringComparer.Ordinal)
            .FirstOrDefault();

        if (scored is null)
        {
            return null;
        }

        return new MqttTopicPayloadFieldCandidate(
            role,
            scored.Leaf.Path,
            scored.Leaf.SemanticDataType.ToString(),
            scored.Leaf.SemanticDataType,
            Math.Min(100, scored.Score),
            FieldCandidateReason(role, scored.Leaf));
    }

    private static int ScoreFieldCandidate(JsonLeaf leaf, string role)
    {
        var last = NormalizeToken(leaf.PropertySegments.LastOrDefault() ?? string.Empty);
        var normalizedPath = NormalizeToken(string.Join(".", leaf.PropertySegments));
        var depthPenalty = Math.Min(16, leaf.PropertySegments.Count * 2);

        if (role == MqttTopicPayloadCandidateRoles.TimestampField)
        {
            if (TimestampNames.Contains(last))
            {
                return 100 - depthPenalty;
            }

            return TimestampNames.Any(name => normalizedPath.EndsWith(name, StringComparison.Ordinal))
                ? 78 - depthPenalty
                : 0;
        }

        if (role == MqttTopicPayloadCandidateRoles.QualityField)
        {
            if (last == "quality")
            {
                return 100 - depthPenalty;
            }

            if (last is "q" or "qualitystatus")
            {
                return 90 - depthPenalty;
            }

            if (last == "status" && normalizedPath.Contains("quality", StringComparison.Ordinal))
            {
                return 86 - depthPenalty;
            }

            if (last is "status" or "health")
            {
                return 70 - depthPenalty;
            }

            return 0;
        }

        if (TimestampNames.Contains(last))
        {
            return 0;
        }

        if (last is "quality" or "q" or "qualitystatus")
        {
            return 0;
        }

        if (last is "value" or "val")
        {
            return 100 - depthPenalty;
        }

        if (last is "reading" or "measurement")
        {
            return 88 - depthPenalty;
        }

        if (leaf.SemanticDataType is SemanticDataType.Int or SemanticDataType.Float or SemanticDataType.Decimal or SemanticDataType.Boolean)
        {
            return 72 - depthPenalty;
        }

        if (leaf.SemanticDataType == SemanticDataType.String && last is not "status" and not "state")
        {
            return 45 - depthPenalty;
        }

        return 0;
    }

    private static string FieldCandidateReason(string role, JsonLeaf leaf)
    {
        var last = leaf.PropertySegments.LastOrDefault() ?? leaf.Path;
        return role switch
        {
            MqttTopicPayloadCandidateRoles.ValueField => $"Selected from payload field '{last}' as the semantic value candidate.",
            MqttTopicPayloadCandidateRoles.TimestampField => $"Selected from payload field '{last}' as the source timestamp candidate.",
            MqttTopicPayloadCandidateRoles.QualityField => $"Selected from payload field '{last}' as the quality candidate.",
            _ => "Selected from payload shape."
        };
    }

    private static MqttTopicPayloadAssetPathCandidate? InferAssetPathCandidate(
        IReadOnlyList<string> topicSegments,
        MqttNamespaceStyle namespaceStyle)
    {
        if (namespaceStyle == MqttNamespaceStyle.Uns)
        {
            var unsAssetPath = topicSegments.Skip(1).Take(Math.Max(0, topicSegments.Count - 2)).ToArray();
            if (unsAssetPath.Length == 0)
            {
                return null;
            }

            return new MqttTopicPayloadAssetPathCandidate(
                unsAssetPath,
                "/" + string.Join("/", unsAssetPath),
                100,
                "uns-topic",
                "ready");
        }

        if (topicSegments.Count < 2)
        {
            return null;
        }

        var candidateSegments = topicSegments
            .Take(topicSegments.Count - 1)
            .Select(NormalizeIdentifierForId)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        if (candidateSegments.Length == 0)
        {
            return null;
        }

        return new MqttTopicPayloadAssetPathCandidate(
            candidateSegments,
            "/" + string.Join("/", candidateSegments),
            64,
            "topic-prefix",
            "pending");
    }

    private static string InferPointName(
        IReadOnlyList<string> topicSegments,
        MqttTopicPayloadFieldCandidate valueCandidate)
    {
        var fieldSegments = valueCandidate.FieldPath
            .Split(['.', '[', ']'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => segment != "$" && !int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .Select(segment => segment.Trim('\'', '"'))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        for (var index = fieldSegments.Length - 1; index >= 0; index--)
        {
            var current = NormalizeIdentifierForId(fieldSegments[index]);
            if (string.IsNullOrWhiteSpace(current))
            {
                continue;
            }

            if (GenericValueFieldNames.Contains(NormalizeToken(current)) && index > 0)
            {
                var previous = NormalizeIdentifierForId(fieldSegments[index - 1]);
                if (!string.IsNullOrWhiteSpace(previous) && !GenericPayloadNames.Contains(NormalizeToken(previous)))
                {
                    return previous;
                }
            }

            if (!GenericValueFieldNames.Contains(NormalizeToken(current)) && !GenericPayloadNames.Contains(NormalizeToken(current)))
            {
                return current;
            }
        }

        var topicLast = NormalizeIdentifierForId(topicSegments.LastOrDefault() ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(topicLast) && !GenericPayloadNames.Contains(NormalizeToken(topicLast)))
        {
            return topicLast;
        }

        return "value";
    }

    private static string InferSemanticId(
        IReadOnlyList<string> topicSegments,
        MqttNamespaceStyle namespaceStyle,
        MqttTopicPayloadAssetPathCandidate? assetPathCandidate,
        string pointName)
    {
        if (namespaceStyle == MqttNamespaceStyle.Uns)
        {
            return NormalizeIdentifierForId(topicSegments.LastOrDefault() ?? pointName);
        }

        var semanticSegments = new List<string>();
        if (assetPathCandidate is not null)
        {
            semanticSegments.AddRange(assetPathCandidate.Segments);
        }
        else
        {
            semanticSegments.AddRange(topicSegments.Select(NormalizeIdentifierForId));
        }

        if (semanticSegments.Count == 0 || !string.Equals(semanticSegments[^1], pointName, StringComparison.Ordinal))
        {
            semanticSegments.Add(pointName);
        }

        return string.Join('.', semanticSegments.Where(segment => !string.IsNullOrWhiteSpace(segment)));
    }

    private static SemanticDataType ToSemanticDataType(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.False => SemanticDataType.Boolean,
            JsonValueKind.Number => element.TryGetInt64(out _) ? SemanticDataType.Int : SemanticDataType.Float,
            JsonValueKind.String => SemanticDataType.String,
            _ => SemanticDataType.String
        };

    private static string ToJsonPathSegment(string propertyName)
    {
        if (IsSimpleJsonPathName(propertyName))
        {
            return "." + propertyName;
        }

        return "['" + propertyName.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "']";
    }

    private static bool IsSimpleJsonPathName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !(char.IsAsciiLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        return value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }

    private static Dictionary<string, JsonElement> ToMetadata(IReadOnlyList<MqttTopicPayloadImportIssue> rowIssues)
    {
        var metadata = new Dictionary<string, JsonElement>
        {
            ["source"] = JsonSerializer.SerializeToElement(DraftSource),
            ["draftStatus"] = JsonSerializer.SerializeToElement(rowIssues.Count == 0 ? "ready" : "pending"),
            ["payloadSampleStored"] = JsonSerializer.SerializeToElement(false)
        };

        if (rowIssues.Count > 0)
        {
            metadata["completionIssues"] = JsonSerializer.SerializeToElement(rowIssues.Select(issue => issue.Code).ToArray());
        }

        return metadata;
    }

    private static MqttTopicPayloadImportResult CreateEmptyResult(
        IReadOnlyList<MqttTopicPayloadImportIssue> issues,
        MqttTopicPayloadImportOptions options)
    {
        var model = new SemanticModel
        {
            ModelId = options.ModelId,
            Name = options.ModelName,
            Description = "Empty MQTT topic/payload import draft."
        };

        return new MqttTopicPayloadImportResult(
            model,
            [],
            [],
            [],
            [],
            [],
            issues,
            SemanticModelValidator.Validate(model));
    }

    private static MqttTopicPayloadImportIssue Error(string field, string code, string message)
        => new(1, code, MqttTopicPayloadImportIssueSeverity.Error, field, message);

    private static MqttTopicPayloadImportIssue Pending(string field, string code, string message)
        => new(1, code, MqttTopicPayloadImportIssueSeverity.Warning, field, message);

    private static string NormalizeIdentifierForId(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSeparator = false;
        foreach (var character in value.Trim())
        {
            var lower = char.ToLowerInvariant(character);
            if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9'))
            {
                builder.Append(lower);
                lastWasSeparator = false;
                continue;
            }

            if (lower is '.' or '_' or '-')
            {
                if (!lastWasSeparator && builder.Length > 0)
                {
                    builder.Append(lower);
                    lastWasSeparator = true;
                }

                continue;
            }

            if (!lastWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        while (builder.Length > 0 && (builder[^1] is '.' or '_' or '-'))
        {
            builder.Length--;
        }

        return builder.Length == 0 ? "point" : builder.ToString();
    }

    private static string NormalizeToken(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static string ToDisplayName(string value)
    {
        var text = value.Replace('.', ' ').Replace('-', ' ').Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(text)
            ? "MQTT value"
            : char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static readonly ISet<string> TimestampNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "timestamp",
        "ts",
        "time",
        "eventtime",
        "createdat",
        "updatedat",
        "observedat",
        "collectedat",
        "sampletime"
    };

    private static readonly ISet<string> GenericValueFieldNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "value",
        "val",
        "reading",
        "measurement",
        "data"
    };

    private static readonly ISet<string> GenericPayloadNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "telemetry",
        "telemetries",
        "event",
        "events",
        "payload",
        "data",
        "metrics",
        "metric",
        "values",
        "value",
        "reported",
        "properties",
        "property",
        "state",
        "status"
    };

    private sealed record JsonLeaf(
        string Path,
        IReadOnlyList<string> PropertySegments,
        JsonValueKind ValueKind,
        SemanticDataType SemanticDataType);

    private sealed record AssetDraft(
        string AssetId,
        string Name,
        string DisplayPath,
        IReadOnlyList<string> AssetPath,
        List<string> Points)
    {
        public static AssetDraft FromAssetPath(IReadOnlyList<string> assetPath, string semanticId)
        {
            var normalized = assetPath
                .Select(NormalizeIdentifierForId)
                .Where(segment => !string.IsNullOrWhiteSpace(segment))
                .ToArray();
            var assetId = $"asset.{string.Join(".", normalized)}";
            return new AssetDraft(
                assetId,
                normalized.LastOrDefault() ?? NormalizeIdentifierForId(semanticId),
                "/" + string.Join("/", normalized),
                normalized,
                []);
        }

        public Asset ToAsset()
            => new()
            {
                AssetId = AssetId,
                Name = Name,
                DisplayName = DisplayPath,
                AssetType = SemanticAssetType.Custom,
                AssetPath = [.. AssetPath],
                Points = [.. Points]
            };

        public SemanticAssetNode ToNode()
            => new(Math.Max(0, AssetPath.Count - 1), AssetId, DisplayPath, "custom", DisplayPath, $"{Points.Count} imported point(s).", true);
    }
}

public sealed record MqttTopicPayloadImportOptions
{
    public string ModelId { get; init; } = "semantic-model-mqtt-import-draft";

    public string ModelName { get; init; } = "MQTT topic/payload import draft";

    public string EndpointRef { get; init; } = "mqtt-endpoint.draft";
}

public sealed record MqttTopicPayloadImportResult(
    SemanticModel SemanticModel,
    IReadOnlyList<SemanticPointDraft> PointDrafts,
    IReadOnlyList<ProtocolBindingDraft> BindingDrafts,
    IReadOnlyList<SemanticAssetNode> AssetDrafts,
    IReadOnlyList<MqttTopicPayloadAssetPathCandidate> AssetPathCandidates,
    IReadOnlyList<MqttTopicPayloadFieldCandidate> FieldCandidates,
    IReadOnlyList<MqttTopicPayloadImportIssue> Issues,
    IReadOnlyList<SemanticValidationDiagnostic> SemanticDiagnostics)
{
    public bool HasErrors => Issues.Any(issue => issue.Severity == MqttTopicPayloadImportIssueSeverity.Error);

    public int PendingCompletionCount => Issues.Count(issue => issue.Severity == MqttTopicPayloadImportIssueSeverity.Warning);
}

public sealed record MqttTopicPayloadAssetPathCandidate(
    IReadOnlyList<string> Segments,
    string DisplayPath,
    int Confidence,
    string Source,
    string Status);

public sealed record MqttTopicPayloadFieldCandidate(
    string Role,
    string FieldPath,
    string DataType,
    SemanticDataType SemanticDataType,
    int Confidence,
    string Reason);

public sealed record MqttTopicPayloadImportIssue(
    int RowNumber,
    string Code,
    MqttTopicPayloadImportIssueSeverity Severity,
    string Field,
    string Message);

public enum MqttTopicPayloadImportIssueSeverity
{
    Info,
    Warning,
    Error
}

public static class MqttTopicPayloadCandidateRoles
{
    public const string AssetPath = "assetPath";
    public const string ValueField = "valueField";
    public const string TimestampField = "timestampField";
    public const string QualityField = "qualityField";
}

public static class MqttTopicPayloadImportFields
{
    public const string Topic = "topic";
    public const string Payload = "payload";
    public const string AssetPath = "assetPath";
    public const string ValueField = "valueField";
    public const string TimestampField = "timestampField";
    public const string QualityField = "qualityField";
    public const string QuantityKind = "quantityKind";
    public const string Dimension = "dimension";
    public const string Unit = "unit";
}

public static class MqttTopicPayloadImportIssueCodes
{
    public const string TopicRequired = "mqtt_topic_payload.topic.required";
    public const string TopicInvalid = "mqtt_topic_payload.topic.invalid";
    public const string PayloadRequired = "mqtt_topic_payload.payload.required";
    public const string PayloadInvalidJson = "mqtt_topic_payload.payload.invalid_json";
    public const string PayloadRootUnsupported = "mqtt_topic_payload.payload.root_unsupported";
    public const string PayloadFieldMissing = "mqtt_topic_payload.payload.field_missing";
    public const string ValueFieldMissing = "mqtt_topic_payload.value_field.missing";
    public const string TimestampFieldPending = "mqtt_topic_payload.timestamp.pending";
    public const string QualityFieldPending = "mqtt_topic_payload.quality.pending";
    public const string QuantityKindPending = "mqtt_topic_payload.quantity_kind.pending";
    public const string DimensionPending = "mqtt_topic_payload.dimension.pending";
    public const string UnitPending = "mqtt_topic_payload.unit.pending";
    public const string AssetOwnerPending = "mqtt_topic_payload.asset_owner.pending";
    public const string AssetOwnerReview = "mqtt_topic_payload.asset_owner.review";
}
