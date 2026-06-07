namespace IoTCoWork.Workbench.Models;

public sealed record SemanticModelingMetric(string Label, string Value, string Detail, string Icon);

public sealed record SemanticAssetNode(
    int Level,
    string AssetId,
    string DisplayName,
    string AssetType,
    string AssetPath,
    string Summary,
    bool IsSelected = false);

public sealed record SemanticPointDraft(
    string SemanticId,
    string DisplayName,
    string QuantityKind,
    string Unit,
    string DataType,
    string Access,
    string AssetPath,
    string Quality,
    string BindingId,
    string Status = "ready",
    IReadOnlyList<string>? CompletionIssues = null);

public sealed record ProtocolBindingDraft(
    string BindingId,
    string Protocol,
    string Source,
    string TargetSemanticId,
    string Traceability,
    string Status);

public sealed record ProcessRelationDraft(
    string RelationId,
    string From,
    string To,
    string RelationType,
    string DependsOn,
    string ControlPolicy);

public sealed record SemanticModelingStep(string Title, string Detail, string State, string Icon);
