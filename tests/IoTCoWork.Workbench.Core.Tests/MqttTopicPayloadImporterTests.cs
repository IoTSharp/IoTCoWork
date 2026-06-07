using IoTCoWork.Workbench.Services;
using IoTSharp.Contracts.Semantic;

namespace IoTCoWork.Workbench.Core.Tests;

public sealed class MqttTopicPayloadImporterTests
{
    private readonly MqttTopicPayloadImporter _importer = new();

    [Fact]
    public void UnsTopic_CreatesMqttBindingWithAssetPathAndFieldCandidates()
    {
        var result = _importer.Import(
            "uns/plant-a/energy/compressor-station-01/unit-01/outlet/temperature",
            """
{
  "value": 42.5,
  "timestamp": "2026-06-07T08:30:00Z",
  "quality": "good"
}
""");

        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)));
        Assert.Single(result.SemanticModel.SemanticPoints);
        Assert.Single(result.SemanticModel.ProtocolBindings);
        Assert.Single(result.SemanticModel.Assets);
        Assert.DoesNotContain(result.SemanticDiagnostics, diagnostic =>
            diagnostic.Code is SemanticValidationCodes.ProtocolBindingMqttUnsTopicMismatch or SemanticValidationCodes.ProtocolBindingMqttTopicInvalid);

        var point = Assert.Single(result.SemanticModel.SemanticPoints);
        Assert.Equal("temperature", point.SemanticId);
        Assert.Equal(SemanticDataType.Float, point.DataType);
        Assert.Contains(result.Issues, issue => issue.Code == MqttTopicPayloadImportIssueCodes.UnitPending);
        Assert.Contains(result.Issues, issue => issue.Code == MqttTopicPayloadImportIssueCodes.QuantityKindPending);

        var asset = Assert.Single(result.SemanticModel.Assets);
        Assert.Equal(["plant-a", "energy", "compressor-station-01", "unit-01", "outlet"], asset.AssetPath);
        Assert.Equal([point.SemanticId], asset.Points);

        var binding = Assert.Single(result.SemanticModel.ProtocolBindings);
        Assert.Equal(SemanticProtocolKind.Mqtt, binding.ProtocolKind);
        Assert.NotNull(binding.Mqtt);
        Assert.Equal(MqttNamespaceStyle.Uns, binding.Mqtt.NamespaceStyle);
        Assert.Equal("$.value", binding.Mqtt.ValueField);
        Assert.Equal("$.timestamp", binding.Mqtt.TimestampField);
        Assert.Equal("$.quality", binding.Mqtt.QualityField);
        Assert.Equal("uns/plant-a/energy/compressor-station-01/unit-01/outlet/temperature", binding.Address);
        Assert.False(binding.Metadata["payloadSampleStored"].GetBoolean());

        Assert.Contains(result.FieldCandidates, candidate =>
            candidate.Role == MqttTopicPayloadCandidateRoles.ValueField && candidate.FieldPath == "$.value");
        Assert.Contains(result.FieldCandidates, candidate =>
            candidate.Role == MqttTopicPayloadCandidateRoles.TimestampField && candidate.FieldPath == "$.timestamp");
        Assert.Contains(result.FieldCandidates, candidate =>
            candidate.Role == MqttTopicPayloadCandidateRoles.QualityField && candidate.FieldPath == "$.quality");
    }

    [Fact]
    public void NonStandardTopic_UsesCustomNamespaceAndKeepsTopic()
    {
        var result = _importer.Import(
            "factoryA/line01/pump07/temp",
            """
{
  "value": 18,
  "ts": "2026-06-07T08:30:00Z",
  "status": "good"
}
""");

        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)));
        var binding = Assert.Single(result.SemanticModel.ProtocolBindings);
        Assert.NotNull(binding.Mqtt);
        Assert.Equal(MqttNamespaceStyle.Custom, binding.Mqtt.NamespaceStyle);
        Assert.Equal("factoryA/line01/pump07/temp", binding.Mqtt.Topic);
        Assert.Equal("factoryA/line01/pump07/temp", binding.Address);
        Assert.Equal("$.ts", binding.Mqtt.TimestampField);
        Assert.Equal("$.status", binding.Mqtt.QualityField);
        Assert.Contains(result.Issues, issue => issue.Code == MqttTopicPayloadImportIssueCodes.AssetOwnerReview);

        var point = Assert.Single(result.SemanticModel.SemanticPoints);
        Assert.Equal("factorya.line01.pump07.temp", point.SemanticId);
        var assetCandidate = Assert.Single(result.AssetPathCandidates);
        Assert.Equal("pending", assetCandidate.Status);
        Assert.Equal(["factorya", "line01", "pump07"], assetCandidate.Segments);
    }

    [Fact]
    public void MissingTimestamp_AddsWarningAndNullTimestampField()
    {
        var result = _importer.Import(
            "uns/plant-a/line-01/pump-01/pressure",
            """
{
  "value": 321.5,
  "quality": "good"
}
""");

        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)));
        Assert.Contains(result.Issues, issue => issue.Code == MqttTopicPayloadImportIssueCodes.TimestampFieldPending);
        var binding = Assert.Single(result.SemanticModel.ProtocolBindings);
        Assert.NotNull(binding.Mqtt);
        Assert.Null(binding.Mqtt.TimestampField);
        Assert.Equal("$.value", binding.Mqtt.ValueField);
        Assert.Equal("$.quality", binding.Mqtt.QualityField);
    }

    [Fact]
    public void NestedPayload_UsesNestedJsonPathCandidates()
    {
        var result = _importer.Import(
            "uns/site-a/utilities/air-compressor-01/outlet/temperature",
            """
{
  "metrics": {
    "temperature": {
      "value": 36.8,
      "unit": "Cel"
    }
  },
  "meta": {
    "timestamp": "2026-06-07T08:30:00Z",
    "quality": {
      "status": "good"
    }
  }
}
""");

        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)));
        var binding = Assert.Single(result.SemanticModel.ProtocolBindings);
        Assert.NotNull(binding.Mqtt);
        Assert.Equal("$.metrics.temperature.value", binding.Mqtt.ValueField);
        Assert.Equal("$.meta.timestamp", binding.Mqtt.TimestampField);
        Assert.Equal("$.meta.quality.status", binding.Mqtt.QualityField);

        var point = Assert.Single(result.SemanticModel.SemanticPoints);
        Assert.Equal("temperature", point.SemanticId);
        Assert.Equal(SemanticDataType.Float, point.DataType);
    }
}
