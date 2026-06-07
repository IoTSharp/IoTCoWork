using IoTCoWork.Workbench.Services;
using IoTSharp.Contracts.Semantic;

namespace IoTCoWork.Workbench.Core.Tests;

public sealed class OpcUaNodeSetImporterTests
{
    private readonly OpcUaNodeSetImporter _importer = new();

    [Fact]
    public void SampleNodeSet_CreatesAssetsAndSemanticPointDrafts()
    {
        var result = _importer.ImportText(OpcUaNodeSetImporter.SampleNodeSet);

        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)));
        Assert.DoesNotContain(result.SemanticDiagnostics, diagnostic => diagnostic.Severity == SemanticValidationSeverity.Error);
        Assert.Equal(2, result.SemanticModel.SemanticPoints.Count);
        Assert.Equal(2, result.SemanticModel.ProtocolBindings.Count);
        Assert.Equal(3, result.SemanticModel.Assets.Count);
        Assert.Equal(2, result.PointDrafts.Count);
        Assert.Equal(2, result.BindingDrafts.Count);

        var unitAsset = result.SemanticModel.Assets.Single(asset => asset.AssetPath.SequenceEqual(["plant-a", "compressor-station-01", "unit-01"]));
        Assert.Contains("plant-a.compressor-station-01.unit-01.outlettemperature", unitAsset.Points);
        Assert.Contains("plant-a.compressor-station-01.unit-01.outletpressure", unitAsset.Points);

        var temperature = result.SemanticModel.SemanticPoints.Single(point => point.SemanticId.EndsWith("outlettemperature", StringComparison.Ordinal));
        Assert.Equal(SemanticDataType.Float, temperature.DataType);
        Assert.Equal("temperature", temperature.Quantity.QuantityKind);
        Assert.Equal("temperature", temperature.Quantity.Dimension);
        Assert.Equal("C", temperature.Unit.Code);
        Assert.Equal(UnitSystem.OpcUa, temperature.Unit.System);
        Assert.Equal(unitAsset.AssetId, temperature.AssetId);

        var pressure = result.SemanticModel.SemanticPoints.Single(point => point.SemanticId.EndsWith("outletpressure", StringComparison.Ordinal));
        Assert.Equal("pressure", pressure.Quantity.QuantityKind);
        Assert.Equal("bar", pressure.Unit.Code);
        Assert.DoesNotContain(result.Issues, issue => issue.Code == OpcUaNodeSetImportIssueCodes.EngineeringUnitsPending);
    }

    [Fact]
    public void SampleNodeSet_PreservesOpcUaBindingTraceability()
    {
        var result = _importer.ImportText(OpcUaNodeSetImporter.SampleNodeSet);

        var temperature = result.SemanticModel.SemanticPoints.Single(point => point.SemanticId.EndsWith("outlettemperature", StringComparison.Ordinal));
        var binding = result.SemanticModel.ProtocolBindings.Single(item => item.BindingId == temperature.Source.BindingId);

        Assert.Equal(SemanticProtocolKind.OpcUa, binding.ProtocolKind);
        Assert.NotNull(binding.OpcUa);
        Assert.Equal("ns=1;s=PlantA.CompressorStation01.Unit01.OutletTemperature", binding.Address);
        Assert.Equal(binding.Address, binding.OpcUa.NodeId.Text);
        Assert.Equal("PlantA.CompressorStation01.Unit01.OutletTemperature", binding.OpcUa.NodeId.Identifier);
        Assert.Equal(OpcUaNodeIdType.String, binding.OpcUa.NodeId.IdentifierType);
        Assert.Equal(1, binding.OpcUa.NodeId.NamespaceIndex);
        Assert.Equal("urn:iotsharp:sample:factory", binding.OpcUa.NodeId.NamespaceUri);
        Assert.Equal("OutletTemperature", binding.OpcUa.BrowseName.Name);
        Assert.Equal("1:OutletTemperature", binding.OpcUa.BrowseName.Text);
        Assert.Equal("Double", binding.OpcUa.DataType.BrowseName?.Name);
        Assert.Equal("i=11", binding.OpcUa.DataType.NodeId.Text);

        Assert.NotNull(binding.OpcUa.EngineeringUnits);
        Assert.Equal("http://www.opcfoundation.org/UA/units/un/cefact", binding.OpcUa.EngineeringUnits.NamespaceUri);
        Assert.Equal(4408652, binding.OpcUa.EngineeringUnits.UnitId);
        Assert.Equal("C", binding.OpcUa.EngineeringUnits.DisplayName?.Text);
        Assert.Equal("degree Celsius", binding.OpcUa.EngineeringUnits.Description?.Text);

        Assert.Equal(["PlantA", "CompressorStation01", "Unit01", "OutletTemperature"], binding.OpcUa.BrowsePath.Select(path => path.BrowseName.Name));
        Assert.Contains(binding.OpcUa.References, reference =>
            reference.ReferenceType.BrowseName?.Name == "HasTypeDefinition"
            && reference.TargetNodeId.Text == "i=63");
        Assert.Contains(binding.OpcUa.References, reference =>
            reference.ReferenceType.BrowseName?.Name == "HasProperty"
            && reference.TargetNodeId.Text.EndsWith(".EngineeringUnits", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidXml_ReturnsImportErrorWithoutThrowing()
    {
        var result = _importer.ImportText("<UANodeSet>");

        Assert.True(result.HasErrors);
        Assert.Empty(result.SemanticModel.SemanticPoints);
        Assert.Contains(result.Issues, issue => issue.Code == OpcUaNodeSetImportIssueCodes.InvalidXml);
    }

    [Fact]
    public void VariableWithoutEngineeringUnits_IsImportedAsPendingDraft()
    {
        var xml = """
<UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
  <UAObject NodeId="ns=1;s=Line01" BrowseName="1:Line01">
    <DisplayName>Line 01</DisplayName>
    <References>
      <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
      <Reference ReferenceType="HasComponent">ns=1;s=Line01.UnknownMetric</Reference>
    </References>
  </UAObject>
  <UAVariable NodeId="ns=1;s=Line01.UnknownMetric" BrowseName="1:UnknownMetric" DataType="Double" AccessLevel="CurrentRead" UserAccessLevel="CurrentRead">
    <DisplayName>Unknown Metric</DisplayName>
    <References>
      <Reference ReferenceType="HasTypeDefinition">i=63</Reference>
      <Reference ReferenceType="HasComponent" IsForward="false">ns=1;s=Line01</Reference>
    </References>
  </UAVariable>
</UANodeSet>
""";

        var result = _importer.ImportText(xml);

        Assert.False(result.HasErrors);
        var draft = Assert.Single(result.PointDrafts);
        Assert.Equal("pending", draft.Status);
        Assert.Contains(result.Issues, issue => issue.Code == OpcUaNodeSetImportIssueCodes.EngineeringUnitsPending);
        Assert.Contains(result.Issues, issue => issue.Code == OpcUaNodeSetImportIssueCodes.QuantityKindPending);
    }
}
