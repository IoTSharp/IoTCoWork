using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using IoTCoWork.Workbench.Models;
using IoTSharp.Contracts.Semantic;

namespace IoTCoWork.Workbench.Services;

public sealed class OpcUaNodeSetImporter
{
    public const long DefaultMaxFileSize = 1024 * 1024 * 4;

    public static readonly string SampleNodeSet = """
<?xml version="1.0" encoding="utf-8"?>
<UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
  <NamespaceUris>
    <Uri>urn:iotsharp:sample:factory</Uri>
  </NamespaceUris>
  <UAObject NodeId="ns=1;s=PlantA" BrowseName="1:PlantA">
    <DisplayName>Plant A</DisplayName>
    <References>
      <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
      <Reference ReferenceType="Organizes">ns=1;s=PlantA.CompressorStation01</Reference>
    </References>
  </UAObject>
  <UAObject NodeId="ns=1;s=PlantA.CompressorStation01" BrowseName="1:CompressorStation01">
    <DisplayName>Compressor Station 01</DisplayName>
    <References>
      <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
      <Reference ReferenceType="HasComponent">ns=1;s=PlantA.CompressorStation01.Unit01</Reference>
      <Reference ReferenceType="Organizes" IsForward="false">ns=1;s=PlantA</Reference>
    </References>
  </UAObject>
  <UAObject NodeId="ns=1;s=PlantA.CompressorStation01.Unit01" BrowseName="1:Unit01">
    <DisplayName>Unit 01</DisplayName>
    <References>
      <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
      <Reference ReferenceType="HasComponent">ns=1;s=PlantA.CompressorStation01.Unit01.OutletTemperature</Reference>
      <Reference ReferenceType="HasComponent">ns=1;s=PlantA.CompressorStation01.Unit01.OutletPressure</Reference>
      <Reference ReferenceType="HasComponent" IsForward="false">ns=1;s=PlantA.CompressorStation01</Reference>
    </References>
  </UAObject>
  <UAVariable NodeId="ns=1;s=PlantA.CompressorStation01.Unit01.OutletTemperature" BrowseName="1:OutletTemperature" DataType="Double" AccessLevel="CurrentRead" UserAccessLevel="CurrentRead">
    <DisplayName>Outlet Temperature</DisplayName>
    <References>
      <Reference ReferenceType="HasTypeDefinition">i=63</Reference>
      <Reference ReferenceType="HasProperty">ns=1;s=PlantA.CompressorStation01.Unit01.OutletTemperature.EngineeringUnits</Reference>
      <Reference ReferenceType="HasComponent" IsForward="false">ns=1;s=PlantA.CompressorStation01.Unit01</Reference>
    </References>
  </UAVariable>
  <UAVariable NodeId="ns=1;s=PlantA.CompressorStation01.Unit01.OutletTemperature.EngineeringUnits" BrowseName="0:EngineeringUnits" DataType="EUInformation" AccessLevel="CurrentRead" UserAccessLevel="CurrentRead">
    <DisplayName>EngineeringUnits</DisplayName>
    <Value>
      <uax:ExtensionObject xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
        <uax:Body>
          <uax:EUInformation>
            <uax:NamespaceUri>http://www.opcfoundation.org/UA/units/un/cefact</uax:NamespaceUri>
            <uax:UnitId>4408652</uax:UnitId>
            <uax:DisplayName>
              <uax:LocalizedText>
                <uax:Text>C</uax:Text>
              </uax:LocalizedText>
            </uax:DisplayName>
            <uax:Description>
              <uax:LocalizedText>
                <uax:Text>degree Celsius</uax:Text>
              </uax:LocalizedText>
            </uax:Description>
          </uax:EUInformation>
        </uax:Body>
      </uax:ExtensionObject>
    </Value>
    <References>
      <Reference ReferenceType="HasTypeDefinition">i=68</Reference>
      <Reference ReferenceType="HasProperty" IsForward="false">ns=1;s=PlantA.CompressorStation01.Unit01.OutletTemperature</Reference>
    </References>
  </UAVariable>
  <UAVariable NodeId="ns=1;s=PlantA.CompressorStation01.Unit01.OutletPressure" BrowseName="1:OutletPressure" DataType="Float" AccessLevel="CurrentRead" UserAccessLevel="CurrentRead">
    <DisplayName>Outlet Pressure</DisplayName>
    <References>
      <Reference ReferenceType="HasTypeDefinition">i=63</Reference>
      <Reference ReferenceType="HasProperty">ns=1;s=PlantA.CompressorStation01.Unit01.OutletPressure.EngineeringUnits</Reference>
      <Reference ReferenceType="HasComponent" IsForward="false">ns=1;s=PlantA.CompressorStation01.Unit01</Reference>
    </References>
  </UAVariable>
  <UAVariable NodeId="ns=1;s=PlantA.CompressorStation01.Unit01.OutletPressure.EngineeringUnits" BrowseName="0:EngineeringUnits" DataType="EUInformation" AccessLevel="CurrentRead" UserAccessLevel="CurrentRead">
    <DisplayName>EngineeringUnits</DisplayName>
    <Value>
      <uax:ExtensionObject xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
        <uax:Body>
          <uax:EUInformation>
            <uax:NamespaceUri>http://www.opcfoundation.org/UA/units/un/cefact</uax:NamespaceUri>
            <uax:UnitId>6448748</uax:UnitId>
            <uax:DisplayName>
              <uax:LocalizedText>
                <uax:Text>bar</uax:Text>
              </uax:LocalizedText>
            </uax:DisplayName>
            <uax:Description>
              <uax:LocalizedText>
                <uax:Text>bar</uax:Text>
              </uax:LocalizedText>
            </uax:Description>
          </uax:EUInformation>
        </uax:Body>
      </uax:ExtensionObject>
    </Value>
    <References>
      <Reference ReferenceType="HasTypeDefinition">i=68</Reference>
      <Reference ReferenceType="HasProperty" IsForward="false">ns=1;s=PlantA.CompressorStation01.Unit01.OutletPressure</Reference>
    </References>
  </UAVariable>
</UANodeSet>
""";

    private const string DraftSource = "iotcowork.opcua-nodeset";

    public OpcUaNodeSetImportResult ImportText(
        string? nodeSetXml,
        OpcUaNodeSetImportOptions? importOptions = null)
    {
        var options = importOptions ?? new OpcUaNodeSetImportOptions();
        if (string.IsNullOrWhiteSpace(nodeSetXml))
        {
            return CreateEmptyResult(
                [
                    Error(OpcUaNodeSetImportFields.NodeSet, OpcUaNodeSetImportIssueCodes.InputEmpty, "NodeSet content is empty.")
                ],
                options);
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(nodeSetXml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Xml.XmlException)
        {
            return CreateEmptyResult(
                [
                    Error(OpcUaNodeSetImportFields.NodeSet, OpcUaNodeSetImportIssueCodes.InvalidXml, $"NodeSet XML could not be parsed: {exception.Message}")
                ],
                options);
        }

        return ImportDocument(document, options);
    }

    private static OpcUaNodeSetImportResult ImportDocument(
        XDocument document,
        OpcUaNodeSetImportOptions options)
    {
        var issues = new List<OpcUaNodeSetImportIssue>();
        var namespaces = ReadNamespaceUris(document.Root);
        var nodes = ReadNodes(document, namespaces, issues);
        if (nodes.Count == 0)
        {
            return CreateEmptyResult(
                [
                    Error(OpcUaNodeSetImportFields.NodeSet, OpcUaNodeSetImportIssueCodes.NoImportableNodes, "NodeSet does not contain importable UAObject or UAVariable nodes.")
                ],
                options);
        }

        var childLinks = BuildChildLinks(nodes);
        var parentByNodeId = BuildParentMap(nodes, childLinks);
        var variables = nodes.Values
            .Where(node => node.NodeClass == OpcUaNodeClass.Variable && !IsPropertyNode(node, parentByNodeId))
            .OrderBy(node => node.NodeId.Text, StringComparer.Ordinal)
            .ToList();

        if (variables.Count == 0)
        {
            return CreateEmptyResult(
                [
                    Pending(OpcUaNodeSetImportFields.NodeSet, OpcUaNodeSetImportIssueCodes.NoVariableNodes, "NodeSet contains objects but no importable UAVariable nodes.")
                ],
                options);
        }

        var assetDrafts = new Dictionary<string, AssetDraft>(StringComparer.Ordinal);
        var semanticPoints = new List<SemanticPoint>();
        var pointDrafts = new List<SemanticPointDraft>();
        var protocolBindings = new List<ProtocolBinding>();
        var bindingDrafts = new List<ProtocolBindingDraft>();
        var nodeDrafts = new List<OpcUaImportedNodeDraft>();
        var seenSemanticIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var variable in variables)
        {
            var rowIssues = new List<OpcUaNodeSetImportIssue>();
            var nodePath = BuildPath(variable, parentByNodeId, nodes);
            var assetPath = BuildAssetPath(nodePath);
            if (assetPath.Count == 0)
            {
                rowIssues.Add(Pending(OpcUaNodeSetImportFields.AssetPath, OpcUaNodeSetImportIssueCodes.AssetOwnerPending, $"assetPath could not be inferred for NodeId '{variable.NodeId.Text}'."));
            }

            var pointName = NormalizeIdentifierForId(variable.BrowseName.Name);
            var semanticId = BuildSemanticId(assetPath, pointName);
            if (!seenSemanticIds.Add(semanticId))
            {
                semanticId = $"{semanticId}.{NormalizeIdentifierForId(variable.NodeId.Identifier)}";
            }

            var dataType = ToSemanticDataType(variable.DataType);
            var engineeringUnits = ResolveEngineeringUnits(variable, nodes);
            if (engineeringUnits is null)
            {
                rowIssues.Add(Pending(OpcUaNodeSetImportFields.EngineeringUnits, OpcUaNodeSetImportIssueCodes.EngineeringUnitsPending, $"EngineeringUnits property is missing for NodeId '{variable.NodeId.Text}'."));
            }

            var quantityKind = InferQuantityKind(variable.BrowseName.Name, engineeringUnits);
            if (string.IsNullOrWhiteSpace(quantityKind))
            {
                rowIssues.Add(Pending(OpcUaNodeSetImportFields.QuantityKind, OpcUaNodeSetImportIssueCodes.QuantityKindPending, $"quantityKind could not be inferred for BrowseName '{variable.BrowseName.Text ?? variable.BrowseName.Name}'."));
            }

            var unitCode = engineeringUnits?.DisplayName?.Text ?? string.Empty;
            var dimension = InferDimension(quantityKind, unitCode);
            if (string.IsNullOrWhiteSpace(unitCode))
            {
                rowIssues.Add(Pending(OpcUaNodeSetImportFields.Unit, OpcUaNodeSetImportIssueCodes.UnitPending, $"unit could not be inferred for NodeId '{variable.NodeId.Text}'."));
            }

            var assetDraft = assetPath.Count == 0
                ? null
                : CreateOrGetAssetDraft(assetDrafts, assetPath, nodePath.Take(Math.Max(0, nodePath.Count - 1)).ToArray());
            assetDraft?.Points.Add(semanticId);

            var access = ParseAccess(variable);
            var bindingId = $"opcua.{NormalizeIdentifierForId(semanticId)}";
            var importStatus = rowIssues.Count == 0 ? "ready" : "pending";
            issues.AddRange(rowIssues);

            var browsePath = nodePath
                .Select((node, index) => new OpcUaBrowsePathElement
                {
                    BrowseName = node.BrowseName,
                    ReferenceType = index == 0
                        ? ReferenceType("Organizes")
                        : FindTraversalReferenceType(parentByNodeId.TryGetValue(node.NodeId.Text, out var parentId) ? parentId : null, node, nodes),
                    IncludeSubtypes = true
                })
                .ToList();

            var references = variable.References
                .Select(reference => ToOpcUaReference(reference, nodes))
                .ToList();
            if (!references.Any(reference => IsReferenceNamed(reference.ReferenceType, "HasTypeDefinition")))
            {
                references.Add(new OpcUaReference
                {
                    ReferenceType = ReferenceType("HasTypeDefinition"),
                    TargetNodeId = NodeId("i=63", namespaces),
                    TargetBrowseName = QualifiedName("BaseDataVariableType", namespaces),
                    TargetDisplayName = new OpcUaLocalizedText { Text = "BaseDataVariableType" },
                    TargetNodeClass = OpcUaNodeClass.VariableType
                });
            }

            var point = new SemanticPoint
            {
                SemanticId = semanticId,
                Name = pointName,
                DisplayName = variable.DisplayName?.Text ?? ToDisplayName(pointName),
                AssetId = assetDraft?.AssetId,
                Quantity = new Quantity
                {
                    QuantityKind = quantityKind,
                    Name = string.IsNullOrWhiteSpace(quantityKind) ? null : quantityKind,
                    Dimension = string.IsNullOrWhiteSpace(dimension) ? null : dimension
                },
                Unit = new Unit
                {
                    Code = unitCode,
                    DisplayName = engineeringUnits?.DisplayName?.Text,
                    Symbol = engineeringUnits?.DisplayName?.Text,
                    System = engineeringUnits is null ? UnitSystem.Custom : UnitSystem.OpcUa,
                    QuantityKind = string.IsNullOrWhiteSpace(quantityKind) ? null : quantityKind
                },
                DataType = dataType,
                Access = access,
                Quality = new Quality
                {
                    Status = QualityStatus.Unknown,
                    Source = "not-provided",
                    Reason = "Imported OPC UA NodeSet draft."
                },
                Source = new ProtocolSource
                {
                    BindingId = bindingId,
                    Role = "primary"
                },
                Metadata = ToMetadata(rowIssues, variable)
            };

            var opcUaBinding = new OpcUaBinding
            {
                NodeId = variable.NodeId,
                BrowseName = variable.BrowseName,
                BrowsePath = browsePath,
                DisplayName = variable.DisplayName,
                DataType = variable.DataType,
                EngineeringUnits = engineeringUnits,
                References = references,
                VariableType = ResolveVariableType(variable, nodes)
            };

            var binding = new ProtocolBinding
            {
                BindingId = bindingId,
                ProtocolKind = SemanticProtocolKind.OpcUa,
                EndpointRef = options.EndpointRef,
                Address = variable.NodeId.Text,
                SourceDataType = dataType,
                Polling = new ProtocolPolling
                {
                    Subscription = true
                },
                OpcUa = opcUaBinding,
                Quality = point.Quality,
                Metadata = ToMetadata(rowIssues, variable)
            };

            semanticPoints.Add(point);
            protocolBindings.Add(binding);
            pointDrafts.Add(new SemanticPointDraft(
                semanticId,
                point.DisplayName ?? semanticId,
                string.IsNullOrWhiteSpace(quantityKind) ? "待补全" : quantityKind,
                string.IsNullOrWhiteSpace(unitCode) ? "待补全" : unitCode,
                dataType.ToString(),
                ToAccessText(access),
                assetDraft?.DisplayPath ?? "待补全",
                engineeringUnits is null ? "unit pending" : $"engineeringUnits: {unitCode}",
                bindingId,
                importStatus,
                rowIssues.Select(issue => issue.Message).ToArray()));
            bindingDrafts.Add(new ProtocolBindingDraft(
                bindingId,
                "OPC UA",
                $"{variable.NodeId.Text} · {variable.BrowseName.Text ?? variable.BrowseName.Name}",
                semanticId,
                $"Preserved NodeId, BrowseName, DataType {variable.DataType.BrowseName?.Name ?? variable.DataType.NodeId.Text}, EngineeringUnits, and {references.Count} reference(s).",
                importStatus));
            nodeDrafts.Add(new OpcUaImportedNodeDraft(
                variable.NodeId.Text,
                variable.BrowseName.Text ?? variable.BrowseName.Name,
                variable.DisplayName?.Text ?? variable.BrowseName.Name,
                variable.DataType.BrowseName?.Name ?? variable.DataType.NodeId.Text,
                unitCode,
                references.Count,
                assetDraft?.DisplayPath ?? "待补全",
                importStatus));
        }

        var model = new SemanticModel
        {
            ModelId = options.ModelId,
            Name = options.ModelName,
            Description = "Draft Semantic Model imported locally from an OPC UA NodeSet in IoTCoWork.",
            Assets = assetDrafts.Values.Select(asset => asset.ToAsset()).ToList(),
            SemanticPoints = semanticPoints,
            ProtocolBindings = protocolBindings,
            Quantities = semanticPoints
                .Where(point => !string.IsNullOrWhiteSpace(point.Quantity.QuantityKind))
                .Select(point => point.Quantity)
                .DistinctBy(quantity => quantity.QuantityKind, StringComparer.Ordinal)
                .ToList(),
            Units = semanticPoints
                .Where(point => !string.IsNullOrWhiteSpace(point.Unit.Code))
                .Select(point => point.Unit)
                .DistinctBy(unit => unit.Code, StringComparer.Ordinal)
                .ToList(),
            Metadata = new Dictionary<string, JsonElement>
            {
                ["source"] = JsonSerializer.SerializeToElement(DraftSource),
                ["draftStatus"] = JsonSerializer.SerializeToElement(issues.Any(issue => issue.Severity == OpcUaNodeSetImportIssueSeverity.Error)
                    ? "error"
                    : issues.Count > 0 ? "pending" : "ready"),
                ["nodeSetSampleStored"] = JsonSerializer.SerializeToElement(false)
            }
        };

        return new OpcUaNodeSetImportResult(
            model,
            pointDrafts,
            bindingDrafts,
            assetDrafts.Values.Select(asset => asset.ToNode()).ToList(),
            nodeDrafts,
            issues,
            SemanticModelValidator.Validate(model));
    }

    private static IReadOnlyDictionary<int, string> ReadNamespaceUris(XElement? root)
    {
        var result = new Dictionary<int, string>();
        if (root is null)
        {
            return result;
        }

        var namespaceUris = root.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "NamespaceUris");
        if (namespaceUris is null)
        {
            return result;
        }

        var namespaceIndex = 1;
        foreach (var uri in namespaceUris.Elements().Where(element => element.Name.LocalName == "Uri"))
        {
            var text = uri.Value.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                result[namespaceIndex] = text;
            }

            namespaceIndex++;
        }

        return result;
    }

    private static IReadOnlyDictionary<string, OpcUaNodeSetNode> ReadNodes(
        XDocument document,
        IReadOnlyDictionary<int, string> namespaces,
        ICollection<OpcUaNodeSetImportIssue> issues)
    {
        var nodes = new Dictionary<string, OpcUaNodeSetNode>(StringComparer.Ordinal);
        foreach (var element in document.Descendants().Where(IsOpcUaNodeElement))
        {
            var nodeIdText = Attribute(element, "NodeId");
            if (string.IsNullOrWhiteSpace(nodeIdText))
            {
                issues.Add(Error(OpcUaNodeSetImportFields.NodeId, OpcUaNodeSetImportIssueCodes.NodeIdRequired, $"NodeSet element '{element.Name.LocalName}' is missing NodeId."));
                continue;
            }

            var browseNameText = Attribute(element, "BrowseName");
            if (string.IsNullOrWhiteSpace(browseNameText))
            {
                issues.Add(Error(OpcUaNodeSetImportFields.BrowseName, OpcUaNodeSetImportIssueCodes.BrowseNameRequired, $"NodeId '{nodeIdText}' is missing BrowseName."));
                continue;
            }

            var nodeId = NodeId(nodeIdText, namespaces);
            var browseName = QualifiedName(browseNameText, namespaces);
            var nodeClass = ToNodeClass(element.Name.LocalName);
            var displayName = ReadLocalizedText(element.Elements().FirstOrDefault(child => child.Name.LocalName == "DisplayName"));
            var dataType = element.Name.LocalName == "UAVariable"
                ? DataTypeReference(Attribute(element, "DataType"), namespaces)
                : TypeReference(nodeId, browseName, displayName);
            var references = element.Elements()
                .Where(child => child.Name.LocalName == "References")
                .Elements()
                .Where(child => child.Name.LocalName == "Reference")
                .Select(reference => ReadReference(reference, namespaces))
                .ToList();

            nodes[nodeId.Text] = new OpcUaNodeSetNode(
                nodeId,
                browseName,
                displayName,
                nodeClass,
                dataType,
                Attribute(element, "AccessLevel"),
                Attribute(element, "UserAccessLevel"),
                references,
                element);
        }

        return nodes;
    }

    private static bool IsOpcUaNodeElement(XElement element)
        => element.Name.LocalName is "UAObject" or "UAVariable" or "UAMethod" or "UAObjectType" or "UAVariableType" or "UADataType" or "UAReferenceType" or "UAView";

    private static OpcUaNodeClass ToNodeClass(string localName)
        => localName switch
        {
            "UAObject" => OpcUaNodeClass.Object,
            "UAVariable" => OpcUaNodeClass.Variable,
            "UAMethod" => OpcUaNodeClass.Method,
            "UAObjectType" => OpcUaNodeClass.ObjectType,
            "UAVariableType" => OpcUaNodeClass.VariableType,
            "UAReferenceType" => OpcUaNodeClass.ReferenceType,
            "UADataType" => OpcUaNodeClass.DataType,
            "UAView" => OpcUaNodeClass.View,
            _ => OpcUaNodeClass.Object
        };

    private static OpcUaNodeSetReference ReadReference(
        XElement reference,
        IReadOnlyDictionary<int, string> namespaces)
    {
        var referenceTypeText = Attribute(reference, "ReferenceType");
        var targetText = reference.Value.Trim();
        return new OpcUaNodeSetReference(
            ReferenceType(referenceTypeText),
            NodeId(targetText, namespaces),
            !string.Equals(Attribute(reference, "IsForward"), "false", StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, List<OpcUaChildLink>> BuildChildLinks(IReadOnlyDictionary<string, OpcUaNodeSetNode> nodes)
    {
        var childLinks = new Dictionary<string, List<OpcUaChildLink>>(StringComparer.Ordinal);
        foreach (var node in nodes.Values)
        {
            foreach (var reference in node.References.Where(reference => reference.IsForward && IsHierarchyReference(reference.ReferenceType)))
            {
                if (!nodes.TryGetValue(reference.TargetNodeId.Text, out var child))
                {
                    continue;
                }

                if (child.NodeClass != OpcUaNodeClass.Object && child.NodeClass != OpcUaNodeClass.Variable)
                {
                    continue;
                }

                if (!childLinks.TryGetValue(node.NodeId.Text, out var links))
                {
                    links = [];
                    childLinks[node.NodeId.Text] = links;
                }

                links.Add(new OpcUaChildLink(child.NodeId.Text, reference.ReferenceType));
            }
        }

        return childLinks;
    }

    private static Dictionary<string, string> BuildParentMap(
        IReadOnlyDictionary<string, OpcUaNodeSetNode> nodes,
        IReadOnlyDictionary<string, List<OpcUaChildLink>> childLinks)
    {
        var parentByNodeId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (parentId, links) in childLinks)
        {
            foreach (var link in links)
            {
                if (!parentByNodeId.ContainsKey(link.ChildNodeId))
                {
                    parentByNodeId[link.ChildNodeId] = parentId;
                }
            }
        }

        foreach (var node in nodes.Values)
        {
            if (parentByNodeId.ContainsKey(node.NodeId.Text))
            {
                continue;
            }

            var inverseParent = node.References.FirstOrDefault(reference =>
                !reference.IsForward
                && IsHierarchyReference(reference.ReferenceType)
                && nodes.ContainsKey(reference.TargetNodeId.Text));
            if (inverseParent is not null)
            {
                parentByNodeId[node.NodeId.Text] = inverseParent.TargetNodeId.Text;
            }
        }

        return parentByNodeId;
    }

    private static bool IsPropertyNode(
        OpcUaNodeSetNode node,
        IReadOnlyDictionary<string, string> parentByNodeId)
    {
        if (node.BrowseName.Name == "EngineeringUnits")
        {
            return true;
        }

        if (!parentByNodeId.TryGetValue(node.NodeId.Text, out _))
        {
            return false;
        }

        return node.References.Any(reference => !reference.IsForward && IsReferenceNamed(reference.ReferenceType, "HasProperty"));
    }

    private static IReadOnlyList<OpcUaNodeSetNode> BuildPath(
        OpcUaNodeSetNode node,
        IReadOnlyDictionary<string, string> parentByNodeId,
        IReadOnlyDictionary<string, OpcUaNodeSetNode> nodes)
    {
        var path = new List<OpcUaNodeSetNode> { node };
        var visited = new HashSet<string>(StringComparer.Ordinal) { node.NodeId.Text };
        var current = node;
        while (parentByNodeId.TryGetValue(current.NodeId.Text, out var parentId)
            && nodes.TryGetValue(parentId, out var parent)
            && visited.Add(parent.NodeId.Text))
        {
            path.Add(parent);
            current = parent;
        }

        path.Reverse();
        return path;
    }

    private static IReadOnlyList<string> BuildAssetPath(IReadOnlyList<OpcUaNodeSetNode> nodePath)
        => nodePath
            .Take(Math.Max(0, nodePath.Count - 1))
            .Where(node => node.NodeClass == OpcUaNodeClass.Object)
            .Select(node => NormalizeIdentifierForId(node.DisplayName?.Text ?? node.BrowseName.Name))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

    private static AssetDraft CreateOrGetAssetDraft(
        IDictionary<string, AssetDraft> assets,
        IReadOnlyList<string> assetPath,
        IReadOnlyList<OpcUaNodeSetNode> assetNodes)
    {
        var assetId = $"asset.{string.Join(".", assetPath)}";
        if (assets.TryGetValue(assetId, out var existing))
        {
            return existing;
        }

        string? parentAssetId = null;
        for (var index = 0; index < assetPath.Count; index++)
        {
            var currentPath = assetPath.Take(index + 1).ToArray();
            var currentAssetId = $"asset.{string.Join(".", currentPath)}";
            if (assets.TryGetValue(currentAssetId, out var current))
            {
                parentAssetId = current.AssetId;
                continue;
            }

            var node = index < assetNodes.Count ? assetNodes[index] : null;
            var draft = new AssetDraft(
                currentAssetId,
                currentPath[^1],
                node?.DisplayName?.Text ?? ToDisplayName(currentPath[^1]),
                "/" + string.Join("/", currentPath),
                currentPath,
                parentAssetId,
                node?.NodeId.Text,
                []);
            assets[currentAssetId] = draft;
            parentAssetId = currentAssetId;
        }

        return assets[assetId];
    }

    private static OpcUaEngineeringUnits? ResolveEngineeringUnits(
        OpcUaNodeSetNode variable,
        IReadOnlyDictionary<string, OpcUaNodeSetNode> nodes)
    {
        foreach (var reference in variable.References.Where(reference => reference.IsForward && IsReferenceNamed(reference.ReferenceType, "HasProperty")))
        {
            if (!nodes.TryGetValue(reference.TargetNodeId.Text, out var propertyNode))
            {
                continue;
            }

            if (!string.Equals(propertyNode.BrowseName.Name, "EngineeringUnits", StringComparison.Ordinal))
            {
                continue;
            }

            var valueElement = propertyNode.Element.Elements().FirstOrDefault(element => element.Name.LocalName == "Value");
            return ReadEngineeringUnits(valueElement) ?? new OpcUaEngineeringUnits();
        }

        return null;
    }

    private static OpcUaEngineeringUnits? ReadEngineeringUnits(XElement? valueElement)
    {
        var euInformation = valueElement?
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "EUInformation");
        if (euInformation is null)
        {
            return null;
        }

        var namespaceUri = ChildValue(euInformation, "NamespaceUri");
        var unitIdText = ChildValue(euInformation, "UnitId");
        _ = int.TryParse(unitIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unitId);
        var displayNameElement = euInformation.Elements().FirstOrDefault(element => element.Name.LocalName == "DisplayName");
        var descriptionElement = euInformation.Elements().FirstOrDefault(element => element.Name.LocalName == "Description");

        return new OpcUaEngineeringUnits
        {
            NamespaceUri = string.IsNullOrWhiteSpace(namespaceUri) ? null : namespaceUri,
            UnitId = string.IsNullOrWhiteSpace(unitIdText) ? null : unitId,
            DisplayName = ReadLocalizedText(displayNameElement),
            Description = ReadLocalizedText(descriptionElement)
        };
    }

    private static OpcUaTypeReference? ResolveVariableType(
        OpcUaNodeSetNode variable,
        IReadOnlyDictionary<string, OpcUaNodeSetNode> nodes)
    {
        var typeDefinition = variable.References.FirstOrDefault(reference => reference.IsForward && IsReferenceNamed(reference.ReferenceType, "HasTypeDefinition"));
        if (typeDefinition is null)
        {
            return null;
        }

        if (nodes.TryGetValue(typeDefinition.TargetNodeId.Text, out var node))
        {
            return TypeReference(node.NodeId, node.BrowseName, node.DisplayName);
        }

        return TypeReference(
            typeDefinition.TargetNodeId,
            BuiltInTypeBrowseName(typeDefinition.TargetNodeId.Text),
            BuiltInTypeBrowseName(typeDefinition.TargetNodeId.Text) is null
                ? null
                : new OpcUaLocalizedText { Text = BuiltInTypeBrowseName(typeDefinition.TargetNodeId.Text)!.Name });
    }

    private static OpcUaTypeReference? FindTraversalReferenceType(
        string? parentId,
        OpcUaNodeSetNode node,
        IReadOnlyDictionary<string, OpcUaNodeSetNode> nodes)
    {
        if (!string.IsNullOrWhiteSpace(parentId) && nodes.TryGetValue(parentId, out var parent))
        {
            var forward = parent.References.FirstOrDefault(reference => reference.IsForward && reference.TargetNodeId.Text == node.NodeId.Text);
            if (forward is not null)
            {
                return forward.ReferenceType;
            }
        }

        var inverse = node.References.FirstOrDefault(reference => !reference.IsForward && IsHierarchyReference(reference.ReferenceType));
        return inverse?.ReferenceType;
    }

    private static OpcUaReference ToOpcUaReference(
        OpcUaNodeSetReference reference,
        IReadOnlyDictionary<string, OpcUaNodeSetNode> nodes)
    {
        nodes.TryGetValue(reference.TargetNodeId.Text, out var target);
        return new OpcUaReference
        {
            ReferenceType = reference.ReferenceType,
            TargetNodeId = reference.TargetNodeId,
            TargetBrowseName = target?.BrowseName ?? BuiltInTypeBrowseName(reference.TargetNodeId.Text),
            TargetDisplayName = target?.DisplayName ?? (BuiltInTypeBrowseName(reference.TargetNodeId.Text) is { } builtIn
                ? new OpcUaLocalizedText { Text = builtIn.Name }
                : null),
            TargetNodeClass = target?.NodeClass ?? BuiltInNodeClass(reference.TargetNodeId.Text),
            IsForward = reference.IsForward
        };
    }

    private static SemanticPointAccess ParseAccess(OpcUaNodeSetNode variable)
    {
        var text = $"{variable.UserAccessLevel} {variable.AccessLevel}";
        var canRead = text.Contains("CurrentRead", StringComparison.OrdinalIgnoreCase)
            || !text.Contains("CurrentWrite", StringComparison.OrdinalIgnoreCase);
        var canWrite = text.Contains("CurrentWrite", StringComparison.OrdinalIgnoreCase);

        return (canRead, canWrite) switch
        {
            (true, true) => SemanticPointAccess.ReadWrite,
            (false, true) => SemanticPointAccess.Write,
            _ => SemanticPointAccess.Read
        };
    }

    private static string BuildSemanticId(IReadOnlyList<string> assetPath, string pointName)
    {
        var segments = assetPath
            .Append(pointName)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        return segments.Length == 0 ? "opcua.point" : string.Join(".", segments);
    }

    private static string InferQuantityKind(
        string browseName,
        OpcUaEngineeringUnits? engineeringUnits)
    {
        var normalized = NormalizeToken(browseName);
        if (normalized.Contains("temperature", StringComparison.Ordinal) || engineeringUnits?.UnitId == 4408652)
        {
            return "temperature";
        }

        if (normalized.Contains("pressure", StringComparison.Ordinal))
        {
            return "pressure";
        }

        if (normalized.Contains("speed", StringComparison.Ordinal))
        {
            return "speed";
        }

        if (normalized.Contains("flow", StringComparison.Ordinal))
        {
            return "flow";
        }

        if (normalized.Contains("state", StringComparison.Ordinal) || normalized.Contains("status", StringComparison.Ordinal))
        {
            return "state";
        }

        if (normalized.Contains("command", StringComparison.Ordinal) || normalized.StartsWith("start", StringComparison.Ordinal) || normalized.StartsWith("stop", StringComparison.Ordinal))
        {
            return "command";
        }

        return string.Empty;
    }

    private static string InferDimension(string quantityKind, string unitCode)
        => quantityKind switch
        {
            "temperature" => "temperature",
            "pressure" => "pressure",
            "speed" => "speed",
            "flow" => "flow",
            "state" or "command" => "dimensionless",
            _ when string.Equals(unitCode, "1", StringComparison.OrdinalIgnoreCase) => "dimensionless",
            _ => string.Empty
        };

    private static SemanticDataType ToSemanticDataType(OpcUaTypeReference dataType)
    {
        var name = NormalizeToken(dataType.BrowseName?.Name ?? dataType.NodeId.Identifier);
        return name switch
        {
            "boolean" => SemanticDataType.Boolean,
            "sbyte" or "byte" or "int16" or "uint16" or "int32" or "uint32" or "integer" or "uinteger" => SemanticDataType.Int,
            "float" or "double" => SemanticDataType.Float,
            "decimal" => SemanticDataType.Decimal,
            "string" or "localizedtext" or "qualifiedname" => SemanticDataType.String,
            _ => SemanticDataType.String
        };
    }

    private static OpcUaTypeReference DataTypeReference(
        string? dataType,
        IReadOnlyDictionary<int, string> namespaces)
    {
        var text = string.IsNullOrWhiteSpace(dataType) ? "i=12" : dataType.Trim();
        if (BuiltInDataTypeIds.TryGetValue(text, out var builtInId))
        {
            text = $"i={builtInId}";
        }

        var nodeId = NodeId(text, namespaces);
        return TypeReference(nodeId, BuiltInTypeBrowseName(nodeId.Text) ?? QualifiedName(dataType ?? nodeId.Identifier, namespaces), BuiltInTypeBrowseName(nodeId.Text) is { } browseName
            ? new OpcUaLocalizedText { Text = browseName.Name }
            : null);
    }

    private static OpcUaTypeReference ReferenceType(string? referenceType)
    {
        var name = string.IsNullOrWhiteSpace(referenceType) ? "References" : referenceType.Trim();
        var id = ReferenceTypeIds.TryGetValue(name, out var value) ? value : 31;
        return TypeReference(
            NodeId($"i={id}", new Dictionary<int, string>()),
            new OpcUaQualifiedName
            {
                Name = name,
                NamespaceIndex = 0,
                Text = $"0:{name}"
            },
            new OpcUaLocalizedText { Text = name });
    }

    private static OpcUaTypeReference TypeReference(
        OpcUaNodeId nodeId,
        OpcUaQualifiedName? browseName,
        OpcUaLocalizedText? displayName)
        => new()
        {
            NodeId = nodeId,
            BrowseName = browseName,
            DisplayName = displayName
        };

    private static OpcUaNodeId NodeId(
        string? value,
        IReadOnlyDictionary<int, string> namespaces)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "i=0" : value.Trim();
        int? namespaceIndex = null;
        string? namespaceUri = null;
        var body = text;

        if (text.StartsWith("ns=", StringComparison.Ordinal))
        {
            var separatorIndex = text.IndexOf(';', StringComparison.Ordinal);
            if (separatorIndex > 3 && int.TryParse(text[3..separatorIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedNamespaceIndex))
            {
                namespaceIndex = parsedNamespaceIndex;
                namespaces.TryGetValue(parsedNamespaceIndex, out namespaceUri);
                body = text[(separatorIndex + 1)..];
            }
        }
        else if (text.StartsWith("nsu=", StringComparison.Ordinal))
        {
            var separatorIndex = text.IndexOf(';', StringComparison.Ordinal);
            if (separatorIndex > 4)
            {
                namespaceUri = text[4..separatorIndex];
                body = text[(separatorIndex + 1)..];
            }
        }

        var identifierType = OpcUaNodeIdType.String;
        var identifier = body;
        if (body.Length > 2 && body[1] == '=')
        {
            identifier = body[2..];
            identifierType = body[0] switch
            {
                'i' => OpcUaNodeIdType.Numeric,
                'g' => OpcUaNodeIdType.Guid,
                'b' => OpcUaNodeIdType.Opaque,
                _ => OpcUaNodeIdType.String
            };
        }

        return new OpcUaNodeId
        {
            Text = text,
            Identifier = identifier,
            IdentifierType = identifierType,
            NamespaceIndex = namespaceIndex,
            NamespaceUri = namespaceUri
        };
    }

    private static OpcUaQualifiedName QualifiedName(
        string? value,
        IReadOnlyDictionary<int, string> namespaces)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "Node" : value.Trim();
        int? namespaceIndex = null;
        string? namespaceUri = null;
        var name = text;
        var colonIndex = text.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex > 0 && int.TryParse(text[..colonIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedNamespaceIndex))
        {
            namespaceIndex = parsedNamespaceIndex;
            namespaces.TryGetValue(parsedNamespaceIndex, out namespaceUri);
            name = text[(colonIndex + 1)..];
        }

        return new OpcUaQualifiedName
        {
            Name = name,
            NamespaceIndex = namespaceIndex,
            NamespaceUri = namespaceUri,
            Text = text
        };
    }

    private static OpcUaQualifiedName? BuiltInTypeBrowseName(string nodeIdText)
    {
        var name = nodeIdText switch
        {
            "i=1" or "ns=0;i=1" => "Boolean",
            "i=2" or "ns=0;i=2" => "SByte",
            "i=3" or "ns=0;i=3" => "Byte",
            "i=4" or "ns=0;i=4" => "Int16",
            "i=5" or "ns=0;i=5" => "UInt16",
            "i=6" or "ns=0;i=6" => "Int32",
            "i=7" or "ns=0;i=7" => "UInt32",
            "i=10" or "ns=0;i=10" => "Float",
            "i=11" or "ns=0;i=11" => "Double",
            "i=12" or "ns=0;i=12" => "String",
            "i=58" or "ns=0;i=58" => "BaseObjectType",
            "i=62" or "ns=0;i=62" => "BaseVariableType",
            "i=63" or "ns=0;i=63" => "BaseDataVariableType",
            "i=68" or "ns=0;i=68" => "PropertyType",
            _ => null
        };

        return name is null
            ? null
            : new OpcUaQualifiedName
            {
                Name = name,
                NamespaceIndex = 0,
                Text = $"0:{name}"
            };
    }

    private static OpcUaNodeClass? BuiltInNodeClass(string nodeIdText)
        => nodeIdText switch
        {
            "i=58" or "ns=0;i=58" => OpcUaNodeClass.ObjectType,
            "i=62" or "ns=0;i=62" or "i=63" or "ns=0;i=63" or "i=68" or "ns=0;i=68" => OpcUaNodeClass.VariableType,
            _ => null
        };

    private static bool IsHierarchyReference(OpcUaTypeReference reference)
        => IsReferenceNamed(reference, "Organizes")
            || IsReferenceNamed(reference, "HasComponent")
            || IsReferenceNamed(reference, "HasProperty");

    private static bool IsReferenceNamed(OpcUaTypeReference reference, string name)
        => string.Equals(reference.BrowseName?.Name, name, StringComparison.Ordinal)
            || (ReferenceTypeIds.TryGetValue(name, out var value)
                && (string.Equals(reference.NodeId.Text, $"i={value}", StringComparison.Ordinal)
                    || string.Equals(reference.NodeId.Text, $"ns=0;i={value}", StringComparison.Ordinal)));

    private static OpcUaLocalizedText? ReadLocalizedText(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        var locale = element.Descendants().FirstOrDefault(child => child.Name.LocalName == "Locale")?.Value.Trim();
        var text = element.Descendants().FirstOrDefault(child => child.Name.LocalName == "Text")?.Value.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            text = element.Value.Trim();
        }

        return string.IsNullOrWhiteSpace(text)
            ? null
            : new OpcUaLocalizedText
            {
                Text = text,
                Locale = string.IsNullOrWhiteSpace(locale) ? null : locale
            };
    }

    private static string? ChildValue(XElement element, string localName)
        => element.Elements().FirstOrDefault(child => child.Name.LocalName == localName)?.Value.Trim();

    private static string Attribute(XElement element, string localName)
        => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == localName)?.Value.Trim() ?? string.Empty;

    private static OpcUaNodeSetImportResult CreateEmptyResult(
        IReadOnlyList<OpcUaNodeSetImportIssue> issues,
        OpcUaNodeSetImportOptions options)
    {
        var model = new SemanticModel
        {
            ModelId = options.ModelId,
            Name = options.ModelName,
            Description = "Empty OPC UA NodeSet import draft."
        };

        return new OpcUaNodeSetImportResult(
            model,
            [],
            [],
            [],
            [],
            issues,
            SemanticModelValidator.Validate(model));
    }

    private static Dictionary<string, JsonElement> ToMetadata(
        IReadOnlyList<OpcUaNodeSetImportIssue> rowIssues,
        OpcUaNodeSetNode variable)
    {
        var metadata = new Dictionary<string, JsonElement>
        {
            ["source"] = JsonSerializer.SerializeToElement(DraftSource),
            ["draftStatus"] = JsonSerializer.SerializeToElement(rowIssues.Count == 0 ? "ready" : "pending"),
            ["nodeId"] = JsonSerializer.SerializeToElement(variable.NodeId.Text),
            ["browseName"] = JsonSerializer.SerializeToElement(variable.BrowseName.Text ?? variable.BrowseName.Name)
        };

        if (rowIssues.Count > 0)
        {
            metadata["completionIssues"] = JsonSerializer.SerializeToElement(rowIssues.Select(issue => issue.Code).ToArray());
        }

        return metadata;
    }

    private static OpcUaNodeSetImportIssue Error(string field, string code, string message)
        => new(1, code, OpcUaNodeSetImportIssueSeverity.Error, field, message);

    private static OpcUaNodeSetImportIssue Pending(string field, string code, string message)
        => new(1, code, OpcUaNodeSetImportIssueSeverity.Warning, field, message);

    private static string ToAccessText(SemanticPointAccess access)
        => access switch
        {
            SemanticPointAccess.Write => "write",
            SemanticPointAccess.ReadWrite => "readWrite",
            SemanticPointAccess.Command => "command",
            SemanticPointAccess.Config => "config",
            _ => "read"
        };

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

            if (char.IsUpper(character) && builder.Length > 0 && !lastWasSeparator)
            {
                builder.Append('-');
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                lastWasSeparator = false;
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

        return builder.Length == 0 ? "node" : builder.ToString();
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
            ? "OPC UA node"
            : char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static readonly IReadOnlyDictionary<string, int> ReferenceTypeIds = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["References"] = 31,
        ["HierarchicalReferences"] = 33,
        ["Organizes"] = 35,
        ["HasTypeDefinition"] = 40,
        ["HasSubtype"] = 45,
        ["HasProperty"] = 46,
        ["HasComponent"] = 47
    };

    private static readonly IReadOnlyDictionary<string, int> BuiltInDataTypeIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Boolean"] = 1,
        ["SByte"] = 2,
        ["Byte"] = 3,
        ["Int16"] = 4,
        ["UInt16"] = 5,
        ["Int32"] = 6,
        ["UInt32"] = 7,
        ["Float"] = 10,
        ["Double"] = 11,
        ["String"] = 12,
        ["DateTime"] = 13,
        ["Guid"] = 14,
        ["ByteString"] = 15,
        ["XmlElement"] = 16,
        ["NodeId"] = 17,
        ["QualifiedName"] = 20,
        ["LocalizedText"] = 21,
        ["EUInformation"] = 887
    };

    private sealed record OpcUaNodeSetNode(
        OpcUaNodeId NodeId,
        OpcUaQualifiedName BrowseName,
        OpcUaLocalizedText? DisplayName,
        OpcUaNodeClass NodeClass,
        OpcUaTypeReference DataType,
        string AccessLevel,
        string UserAccessLevel,
        IReadOnlyList<OpcUaNodeSetReference> References,
        XElement Element);

    private sealed record OpcUaNodeSetReference(
        OpcUaTypeReference ReferenceType,
        OpcUaNodeId TargetNodeId,
        bool IsForward);

    private sealed record OpcUaChildLink(
        string ChildNodeId,
        OpcUaTypeReference ReferenceType);

    private sealed record AssetDraft(
        string AssetId,
        string Name,
        string DisplayName,
        string DisplayPath,
        IReadOnlyList<string> AssetPath,
        string? ParentAssetId,
        string? SourceNodeId,
        List<string> Points)
    {
        public Asset ToAsset()
            => new()
            {
                AssetId = AssetId,
                Name = Name,
                DisplayName = DisplayName,
                AssetType = AssetPath.Count switch
                {
                    1 => SemanticAssetType.Site,
                    2 => SemanticAssetType.Area,
                    3 => SemanticAssetType.Device,
                    _ => SemanticAssetType.Component
                },
                ParentAssetId = ParentAssetId,
                AssetPath = [.. AssetPath],
                Points = [.. Points],
                ExternalReferences = string.IsNullOrWhiteSpace(SourceNodeId)
                    ? []
                    :
                    [
                        new AssetExternalReference
                        {
                            ReferenceType = "opcua.node",
                            ReferenceId = SourceNodeId,
                            System = "opcua"
                        }
                    ]
            };

        public SemanticAssetNode ToNode()
            => new(Math.Max(0, AssetPath.Count - 1), AssetId, DisplayName, "custom", DisplayPath, $"{Points.Count} imported point(s).", Points.Count > 0);
    }
}

public sealed record OpcUaNodeSetImportOptions
{
    public string ModelId { get; init; } = "semantic-model-opcua-import-draft";

    public string ModelName { get; init; } = "OPC UA NodeSet import draft";

    public string EndpointRef { get; init; } = "opcua-endpoint.draft";
}

public sealed record OpcUaNodeSetImportResult(
    SemanticModel SemanticModel,
    IReadOnlyList<SemanticPointDraft> PointDrafts,
    IReadOnlyList<ProtocolBindingDraft> BindingDrafts,
    IReadOnlyList<SemanticAssetNode> AssetDrafts,
    IReadOnlyList<OpcUaImportedNodeDraft> ImportedNodes,
    IReadOnlyList<OpcUaNodeSetImportIssue> Issues,
    IReadOnlyList<SemanticValidationDiagnostic> SemanticDiagnostics)
{
    public bool HasErrors => Issues.Any(issue => issue.Severity == OpcUaNodeSetImportIssueSeverity.Error);

    public int PendingCompletionCount => Issues.Count(issue => issue.Severity == OpcUaNodeSetImportIssueSeverity.Warning);
}

public sealed record OpcUaImportedNodeDraft(
    string NodeId,
    string BrowseName,
    string DisplayName,
    string DataType,
    string EngineeringUnits,
    int ReferenceCount,
    string AssetPath,
    string Status);

public sealed record OpcUaNodeSetImportIssue(
    int RowNumber,
    string Code,
    OpcUaNodeSetImportIssueSeverity Severity,
    string Field,
    string Message);

public enum OpcUaNodeSetImportIssueSeverity
{
    Info,
    Warning,
    Error
}

public static class OpcUaNodeSetImportFields
{
    public const string NodeSet = "nodeSet";
    public const string NodeId = "nodeId";
    public const string BrowseName = "browseName";
    public const string AssetPath = "assetPath";
    public const string QuantityKind = "quantityKind";
    public const string Unit = "unit";
    public const string EngineeringUnits = "engineeringUnits";
}

public static class OpcUaNodeSetImportIssueCodes
{
    public const string InputEmpty = "opcua_nodeset.input.empty";
    public const string InvalidXml = "opcua_nodeset.xml.invalid";
    public const string NoImportableNodes = "opcua_nodeset.nodes.empty";
    public const string NoVariableNodes = "opcua_nodeset.variables.empty";
    public const string NodeIdRequired = "opcua_nodeset.node_id.required";
    public const string BrowseNameRequired = "opcua_nodeset.browse_name.required";
    public const string QuantityKindPending = "opcua_nodeset.quantity_kind.pending";
    public const string UnitPending = "opcua_nodeset.unit.pending";
    public const string EngineeringUnitsPending = "opcua_nodeset.engineering_units.pending";
    public const string AssetOwnerPending = "opcua_nodeset.asset_owner.pending";
}
