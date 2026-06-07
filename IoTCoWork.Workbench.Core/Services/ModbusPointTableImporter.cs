using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using IoTCoWork.Workbench.Models;
using IoTSharp.Contracts.Semantic;

namespace IoTCoWork.Workbench.Services;

public sealed class ModbusPointTableImporter
{
    public const long DefaultMaxFileSize = 1024 * 1024 * 4;

    public static readonly string SampleCsv = string.Join(Environment.NewLine,
        "semanticId,displayName,quantityKind,dimension,unit,dataType,access,assetPath,registerType,address,functionCode,unitId,registerCount,scale,offset,byteOrder,wordOrder",
        "compressor.unit01.outlet.temperature,Outlet temperature,temperature,temperature,Cel,float,read,/plant-a/energy/compressor-station-01/unit-01/outlet,holding-register,40001,3,1,2,0.1,0,bigEndian,littleEndian",
        "compressor.unit01.outlet.pressure,Outlet pressure,pressure,pressure,bar,float,read,/plant-a/energy/compressor-station-01/unit-01/outlet,holding-register,40003,3,1,2,0.01,0,bigEndian,littleEndian",
        "compressor.unit01.running.state,Running state,state,dimensionless,1,boolean,read,/plant-a/energy/compressor-station-01/unit-01,coil,00001,1,1,1,1,0,bigEndian,bigEndian");

    public ModbusPointTableImportResult ImportText(
        string? text,
        ModbusPointTableTextFormat format = ModbusPointTableTextFormat.Auto,
        ModbusPointTableImportOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return CreateEmptyResult([
                new ModbusPointTableImportIssue(
                    0,
                    ModbusPointTableImportIssueCodes.InputEmpty,
                    ModbusPointTableImportIssueSeverity.Error,
                    string.Empty,
                    "Point table content is empty.")
            ], options);
        }

        var delimiter = format switch
        {
            ModbusPointTableTextFormat.Csv => ',',
            ModbusPointTableTextFormat.Tsv => '\t',
            _ => DetectDelimiter(text)
        };

        var table = ParseDelimitedText(text, delimiter);
        return ImportTable(table, options);
    }

    public async Task<ModbusPointTableImportResult> ImportXlsxAsync(
        Stream stream,
        ModbusPointTableImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var table = await ReadXlsxFirstSheetAsync(stream, cancellationToken);
        return ImportTable(table, options);
    }

    public ModbusPointTableImportResult ImportRows(
        IEnumerable<ModbusPointTableInputRow> rows,
        ModbusPointTableImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var tableRows = rows
            .Select(row => new PointTableRow(
                row.RowNumber <= 0 ? 1 : row.RowNumber,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [Fields.SemanticId] = row.SemanticId ?? string.Empty,
                    [Fields.DisplayName] = row.DisplayName ?? string.Empty,
                    [Fields.QuantityKind] = row.QuantityKind ?? string.Empty,
                    [Fields.Dimension] = row.Dimension ?? string.Empty,
                    [Fields.Unit] = row.Unit ?? string.Empty,
                    [Fields.DataType] = row.DataType ?? string.Empty,
                    [Fields.Access] = row.Access ?? string.Empty,
                    [Fields.AssetId] = row.AssetId ?? string.Empty,
                    [Fields.AssetPath] = row.AssetPath ?? string.Empty,
                    [Fields.RegisterType] = row.RegisterType ?? string.Empty,
                    [Fields.Address] = row.Address ?? string.Empty,
                    [Fields.FunctionCode] = row.FunctionCode ?? string.Empty,
                    [Fields.UnitId] = row.UnitId ?? string.Empty,
                    [Fields.RegisterCount] = row.RegisterCount ?? string.Empty,
                    [Fields.Scale] = row.Scale ?? string.Empty,
                    [Fields.Offset] = row.Offset ?? string.Empty,
                    [Fields.ByteOrder] = row.ByteOrder ?? string.Empty,
                    [Fields.WordOrder] = row.WordOrder ?? string.Empty,
                    [Fields.EndpointRef] = row.EndpointRef ?? string.Empty
                }))
            .ToList();

        return BuildResult(tableRows, [], options);
    }

    private static ModbusPointTableImportResult ImportTable(
        ParsedPointTable table,
        ModbusPointTableImportOptions? options)
    {
        if (table.Rows.Count == 0)
        {
            return CreateEmptyResult([
                new ModbusPointTableImportIssue(
                    0,
                    ModbusPointTableImportIssueCodes.InputEmpty,
                    ModbusPointTableImportIssueSeverity.Error,
                    string.Empty,
                    "Point table does not contain data rows.")
            ], options);
        }

        var issues = new List<ModbusPointTableImportIssue>();
        foreach (var requiredColumn in RequiredColumnNames)
        {
            if (!table.PresentCanonicalColumns.Contains(requiredColumn))
            {
                issues.Add(new ModbusPointTableImportIssue(
                    1,
                    ModbusPointTableImportIssueCodes.RequiredColumnMissing,
                    ModbusPointTableImportIssueSeverity.Error,
                    requiredColumn,
                    $"Required column '{requiredColumn}' is missing."));
            }
        }

        foreach (var pendingColumn in PendingColumnNames)
        {
            if (!table.PresentCanonicalColumns.Contains(pendingColumn))
            {
                issues.Add(new ModbusPointTableImportIssue(
                    1,
                    ModbusPointTableImportIssueCodes.CompletionColumnMissing,
                    ModbusPointTableImportIssueSeverity.Warning,
                    pendingColumn,
                    $"Column '{pendingColumn}' is missing; imported points will be marked for semantic completion."));
            }
        }

        return BuildResult(table.Rows, issues, options);
    }

    private static ModbusPointTableImportResult BuildResult(
        IReadOnlyList<PointTableRow> rows,
        IReadOnlyList<ModbusPointTableImportIssue> initialIssues,
        ModbusPointTableImportOptions? importOptions)
    {
        var options = importOptions ?? new ModbusPointTableImportOptions();
        var issues = new List<ModbusPointTableImportIssue>(initialIssues);
        var semanticPoints = new List<SemanticPoint>();
        var pointDrafts = new List<SemanticPointDraft>();
        var protocolBindings = new List<ProtocolBinding>();
        var bindingDrafts = new List<ProtocolBindingDraft>();
        var importedRows = new List<ModbusPointTableImportedRow>();
        var assetDrafts = new Dictionary<string, AssetDraft>(StringComparer.Ordinal);
        var seenSemanticIds = new HashSet<string>(StringComparer.Ordinal);
        var seenBindingKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var rowIssues = new List<ModbusPointTableImportIssue>();
            var semanticId = Get(row, Fields.SemanticId).Trim();
            if (string.IsNullOrWhiteSpace(semanticId))
            {
                rowIssues.Add(Error(row.RowNumber, Fields.SemanticId, ModbusPointTableImportIssueCodes.SemanticIdRequired, "semanticId is required."));
            }

            if (!string.IsNullOrWhiteSpace(semanticId) && !seenSemanticIds.Add(semanticId))
            {
                rowIssues.Add(Error(row.RowNumber, Fields.SemanticId, ModbusPointTableImportIssueCodes.DuplicateSemanticId, $"semanticId '{semanticId}' is duplicated."));
            }

            var displayName = Coalesce(Get(row, Fields.DisplayName), semanticId);
            var quantityKind = Get(row, Fields.QuantityKind).Trim();
            var dimension = Get(row, Fields.Dimension).Trim();
            var unit = Get(row, Fields.Unit).Trim();
            var assetIdValue = Get(row, Fields.AssetId).Trim();
            var assetPathValue = Get(row, Fields.AssetPath).Trim();

            if (string.IsNullOrWhiteSpace(quantityKind))
            {
                rowIssues.Add(Pending(row.RowNumber, Fields.QuantityKind, ModbusPointTableImportIssueCodes.QuantityKindPending, "quantityKind is missing and must be completed manually."));
            }

            if (string.IsNullOrWhiteSpace(dimension))
            {
                rowIssues.Add(Pending(row.RowNumber, Fields.Dimension, ModbusPointTableImportIssueCodes.DimensionPending, "dimension is missing and must be completed manually."));
            }

            if (string.IsNullOrWhiteSpace(unit))
            {
                rowIssues.Add(Pending(row.RowNumber, Fields.Unit, ModbusPointTableImportIssueCodes.UnitPending, "unit is missing and must be completed manually."));
            }

            if (string.IsNullOrWhiteSpace(assetIdValue) && string.IsNullOrWhiteSpace(assetPathValue))
            {
                rowIssues.Add(Pending(row.RowNumber, Fields.AssetPath, ModbusPointTableImportIssueCodes.AssetOwnerPending, "asset ownership is missing and must be completed manually."));
            }

            var registerTypeText = Get(row, Fields.RegisterType);
            var addressText = Get(row, Fields.Address);
            var registerTypeParsed = TryParseRegisterType(registerTypeText, out var registerType);
            if (!registerTypeParsed)
            {
                rowIssues.Add(Error(row.RowNumber, Fields.RegisterType, ModbusPointTableImportIssueCodes.InvalidRegister, $"registerType '{registerTypeText}' is not supported."));
            }

            var addressParsed = false;
            var zeroBasedAddress = 0;
            var canonicalAddress = string.Empty;
            if (registerTypeParsed)
            {
                addressParsed = TryParseAddress(registerType, addressText, out zeroBasedAddress, out canonicalAddress, out var addressError);
                if (!addressParsed)
                {
                    rowIssues.Add(Error(row.RowNumber, Fields.Address, ModbusPointTableImportIssueCodes.InvalidRegister, addressError));
                }
            }

            var access = ParseAccess(Get(row, Fields.Access), registerTypeParsed ? registerType : ModbusRegisterType.HoldingRegister, Get(row, Fields.FunctionCode));
            var functionCode = ParseFunctionCode(Get(row, Fields.FunctionCode), registerTypeParsed ? registerType : ModbusRegisterType.HoldingRegister, access);
            var registerCount = ParsePositiveInt(Get(row, Fields.RegisterCount), 1);
            if (registerTypeParsed)
            {
                ValidateFunctionAndCount(row.RowNumber, registerType, functionCode, registerCount, rowIssues);
            }

            var bindingKey = registerTypeParsed && addressParsed
                ? $"{registerType}:{zeroBasedAddress}:{functionCode}:{ParseNonNegativeInt(Get(row, Fields.UnitId), options.DefaultUnitId)}"
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(bindingKey) && !seenBindingKeys.Add(bindingKey))
            {
                rowIssues.Add(Error(row.RowNumber, Fields.Address, ModbusPointTableImportIssueCodes.DuplicateBindingSource, "Modbus register source is duplicated."));
            }

            var dataType = ParseDataType(Get(row, Fields.DataType), registerTypeParsed ? registerType : ModbusRegisterType.HoldingRegister, row.RowNumber, rowIssues);
            var sourceDataType = dataType;
            var unitId = ParseNonNegativeInt(Get(row, Fields.UnitId), options.DefaultUnitId);
            var scale = ParseDecimal(Get(row, Fields.Scale), 1m);
            var offset = ParseDecimal(Get(row, Fields.Offset), 0m);
            var byteOrder = ParseByteOrder(Get(row, Fields.ByteOrder));
            var wordOrder = ParseWordOrder(Get(row, Fields.WordOrder));
            var endpointRef = Coalesce(Get(row, Fields.EndpointRef), options.EndpointRef);

            issues.AddRange(rowIssues);
            var rowHasBlockingError = rowIssues.Any(issue => issue.Severity == ModbusPointTableImportIssueSeverity.Error);
            importedRows.Add(new ModbusPointTableImportedRow(
                row.RowNumber,
                semanticId,
                rowHasBlockingError ? "error" : rowIssues.Count > 0 ? "pending" : "ready",
                new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(row.Values, StringComparer.OrdinalIgnoreCase)),
                rowIssues));

            if (rowHasBlockingError)
            {
                continue;
            }

            var bindingId = $"modbus.{NormalizeIdentifierForId(semanticId)}";
            var assetDraft = CreateOrGetAssetDraft(assetDrafts, assetIdValue, assetPathValue, semanticId);
            var point = new SemanticPoint
            {
                SemanticId = semanticId,
                Name = NormalizeIdentifierForId(semanticId.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? semanticId),
                DisplayName = displayName,
                AssetId = assetDraft?.AssetId,
                Quantity = new Quantity
                {
                    QuantityKind = quantityKind,
                    Name = string.IsNullOrWhiteSpace(quantityKind) ? null : quantityKind,
                    Dimension = string.IsNullOrWhiteSpace(dimension) ? null : dimension
                },
                Unit = new Unit
                {
                    Code = unit,
                    DisplayName = string.IsNullOrWhiteSpace(unit) ? null : unit,
                    Symbol = string.IsNullOrWhiteSpace(unit) ? null : unit,
                    System = string.Equals(unit, "1", StringComparison.OrdinalIgnoreCase) ? UnitSystem.Ucum : UnitSystem.Custom,
                    QuantityKind = string.IsNullOrWhiteSpace(quantityKind) ? null : quantityKind
                },
                DataType = dataType,
                Access = access,
                Quality = new Quality
                {
                    Status = QualityStatus.Unknown,
                    Source = "not-provided",
                    Reason = "Imported Modbus point table draft."
                },
                Source = new ProtocolSource
                {
                    BindingId = bindingId,
                    Role = "primary"
                },
                Metadata = ToMetadata(rowIssues)
            };

            var binding = new ProtocolBinding
            {
                BindingId = bindingId,
                ProtocolKind = options.ProtocolKind,
                EndpointRef = endpointRef,
                Address = canonicalAddress,
                SourceDataType = sourceDataType,
                Decode = new ProtocolDecode
                {
                    ByteOrder = ToWireValue(byteOrder),
                    WordOrder = ToWireValue(wordOrder),
                    Scale = scale,
                    Offset = offset
                },
                Modbus = new ModbusBinding
                {
                    RegisterType = registerType,
                    Address = zeroBasedAddress,
                    FunctionCode = functionCode,
                    UnitId = unitId,
                    RegisterCount = registerCount,
                    ByteOrder = byteOrder,
                    WordOrder = wordOrder,
                    Scale = scale,
                    Offset = offset
                },
                Metadata = ToMetadata(rowIssues)
            };

            semanticPoints.Add(point);
            protocolBindings.Add(binding);
            assetDraft?.Points.Add(semanticId);
            pointDrafts.Add(new SemanticPointDraft(
                semanticId,
                displayName,
                string.IsNullOrWhiteSpace(quantityKind) ? "待补全" : quantityKind,
                string.IsNullOrWhiteSpace(unit) ? "待补全" : unit,
                dataType.ToString(),
                ToAccessText(access),
                assetDraft?.DisplayPath ?? "待补全",
                rowIssues.Count == 0 ? "ready" : "pending",
                bindingId,
                rowIssues.Count == 0 ? "ready" : "pending",
                rowIssues.Select(issue => issue.Message).ToArray()));
            bindingDrafts.Add(new ProtocolBindingDraft(
                bindingId,
                options.ProtocolKind == SemanticProtocolKind.ModbusRtu ? "Modbus RTU" : "Modbus TCP",
                $"{ToWireValue(registerType)} {canonicalAddress}, fc {functionCode}, unit {unitId}, scale {scale.ToString(CultureInfo.InvariantCulture)}",
                semanticId,
                "Imported from point table. Address, function code, byte order, word order, scale, and offset are preserved.",
                rowIssues.Count == 0 ? "ready" : "pending"));
        }

        var model = new SemanticModel
        {
            ModelId = options.ModelId,
            Name = options.ModelName,
            Description = "Draft Semantic Model imported from a Modbus point table in IoTCoWork.",
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
                ["source"] = JsonSerializer.SerializeToElement("iotcowork.modbus-point-table"),
                ["draftStatus"] = JsonSerializer.SerializeToElement(issues.Any(issue => issue.Severity == ModbusPointTableImportIssueSeverity.Error)
                    ? "error"
                    : issues.Count > 0 ? "pending" : "ready")
            }
        };

        var semanticDiagnostics = SemanticModelValidator.Validate(model);
        return new ModbusPointTableImportResult(
            model,
            pointDrafts,
            bindingDrafts,
            assetDrafts.Values.Select(asset => asset.ToNode()).ToList(),
            importedRows,
            issues,
            semanticDiagnostics);
    }

    private static ParsedPointTable ParseDelimitedText(string text, char delimiter)
    {
        var rawRows = ReadDelimitedRows(text, delimiter)
            .Where(row => row.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
            .ToList();
        if (rawRows.Count == 0)
        {
            return new ParsedPointTable([], new HashSet<string>(StringComparer.Ordinal));
        }

        var header = rawRows[0].Values;
        var headerMap = BuildHeaderMap(header);
        var rows = new List<PointTableRow>();
        foreach (var rawRow in rawRows.Skip(1))
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < header.Count; index++)
            {
                if (!headerMap.TryGetValue(index, out var canonicalName))
                {
                    continue;
                }

                values[canonicalName] = index < rawRow.Values.Count ? rawRow.Values[index].Trim() : string.Empty;
            }

            rows.Add(new PointTableRow(rawRow.RowNumber, values));
        }

        return new ParsedPointTable(rows, headerMap.Values.ToHashSet(StringComparer.Ordinal));
    }

    private static async Task<ParsedPointTable> ReadXlsxFirstSheetAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var workbook = await LoadXmlAsync(archive, "xl/workbook.xml", cancellationToken);
        var firstSheet = workbook
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "sheet")
            ?? throw new InvalidDataException("XLSX workbook does not contain a worksheet.");
        var relationshipId = firstSheet.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "id")?.Value
            ?? throw new InvalidDataException("XLSX worksheet relationship is missing.");

        var relationships = await LoadXmlAsync(archive, "xl/_rels/workbook.xml.rels", cancellationToken);
        var sheetTarget = relationships
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Relationship"
                && string.Equals(element.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal))
            ?.Attribute("Target")
            ?.Value
            ?? throw new InvalidDataException("XLSX worksheet target is missing.");

        var sheetPath = NormalizeXlsxPath("xl", sheetTarget);
        var sharedStrings = await ReadSharedStringsAsync(archive, cancellationToken);
        var sheet = await LoadXmlAsync(archive, sheetPath, cancellationToken);

        var rawRows = new List<RawPointTableRow>();
        foreach (var rowElement in sheet.Descendants().Where(element => element.Name.LocalName == "row"))
        {
            var rowNumber = TryParseInt(rowElement.Attribute("r")?.Value, 0);
            var cells = new SortedDictionary<int, string>();
            foreach (var cell in rowElement.Elements().Where(element => element.Name.LocalName == "c"))
            {
                var cellReference = cell.Attribute("r")?.Value;
                var columnIndex = string.IsNullOrWhiteSpace(cellReference) ? cells.Count : GetColumnIndex(cellReference);
                cells[columnIndex] = ReadCellValue(cell, sharedStrings);
            }

            if (cells.Count == 0)
            {
                continue;
            }

            var width = cells.Keys.Max() + 1;
            var values = Enumerable.Range(0, width)
                .Select(index => cells.TryGetValue(index, out var value) ? value : string.Empty)
                .ToList();
            rawRows.Add(new RawPointTableRow(rowNumber <= 0 ? rawRows.Count + 1 : rowNumber, values));
        }

        if (rawRows.Count == 0)
        {
            return new ParsedPointTable([], new HashSet<string>(StringComparer.Ordinal));
        }

        var header = rawRows[0].Values;
        var headerMap = BuildHeaderMap(header);
        var rows = new List<PointTableRow>();
        foreach (var rawRow in rawRows.Skip(1))
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < header.Count; index++)
            {
                if (!headerMap.TryGetValue(index, out var canonicalName))
                {
                    continue;
                }

                values[canonicalName] = index < rawRow.Values.Count ? rawRow.Values[index].Trim() : string.Empty;
            }

            rows.Add(new PointTableRow(rawRow.RowNumber, values));
        }

        return new ParsedPointTable(rows, headerMap.Values.ToHashSet(StringComparer.Ordinal));
    }

    private static async Task<XDocument> LoadXmlAsync(ZipArchive archive, string path, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(path.Replace('\\', '/'))
            ?? throw new InvalidDataException($"XLSX entry '{path}' is missing.");
        await using var entryStream = entry.Open();
        return await XDocument.LoadAsync(entryStream, LoadOptions.None, cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> ReadSharedStringsAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        await using var entryStream = entry.Open();
        var document = await XDocument.LoadAsync(entryStream, LoadOptions.None, cancellationToken);
        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "si")
            .Select(ReadSharedString)
            .ToArray();
    }

    private static string ReadSharedString(XElement item)
        => string.Concat(item.Descendants().Where(element => element.Name.LocalName == "t").Select(element => element.Value));

    private static string ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = cell.Attribute("t")?.Value;
        if (string.Equals(type, "inlineStr", StringComparison.Ordinal))
        {
            return string.Concat(cell.Descendants().Where(element => element.Name.LocalName == "t").Select(element => element.Value));
        }

        var value = cell.Elements().FirstOrDefault(element => element.Name.LocalName == "v")?.Value ?? string.Empty;
        if (string.Equals(type, "s", StringComparison.Ordinal)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedStringIndex)
            && sharedStringIndex >= 0
            && sharedStringIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedStringIndex];
        }

        return value;
    }

    private static string NormalizeXlsxPath(string basePath, string target)
    {
        var normalized = target.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            return normalized.TrimStart('/');
        }

        return $"{basePath.TrimEnd('/')}/{normalized}";
    }

    private static int GetColumnIndex(string cellReference)
    {
        var column = 0;
        foreach (var character in cellReference)
        {
            if (!char.IsLetter(character))
            {
                break;
            }

            column = column * 26 + char.ToUpperInvariant(character) - 'A' + 1;
        }

        return Math.Max(0, column - 1);
    }

    private static IReadOnlyDictionary<int, string> BuildHeaderMap(IReadOnlyList<string> header)
    {
        var result = new Dictionary<int, string>();
        for (var index = 0; index < header.Count; index++)
        {
            if (TryResolveColumn(header[index], out var canonicalName))
            {
                result[index] = canonicalName;
            }
        }

        return result;
    }

    private static IEnumerable<RawPointTableRow> ReadDelimitedRows(string text, char delimiter)
    {
        using var reader = new StringReader(text);
        string? line;
        var rowNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            rowNumber++;
            yield return new RawPointTableRow(rowNumber, ParseDelimitedLine(line, delimiter));
        }
    }

    private static List<string> ParseDelimitedLine(string line, char delimiter)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    builder.Append('"');
                    index++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (character == delimiter && !inQuotes)
            {
                values.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(character);
        }

        values.Add(builder.ToString());
        return values;
    }

    private static char DetectDelimiter(string text)
    {
        var firstLine = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        var tabs = firstLine.Count(character => character == '\t');
        var commas = firstLine.Count(character => character == ',');
        var semicolons = firstLine.Count(character => character == ';');
        if (tabs >= commas && tabs >= semicolons && tabs > 0)
        {
            return '\t';
        }

        return semicolons > commas ? ';' : ',';
    }

    private static string Get(PointTableRow row, string field)
        => row.Values.TryGetValue(field, out var value) ? value : string.Empty;

    private static string Coalesce(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static ModbusPointTableImportIssue Pending(int rowNumber, string field, string code, string message)
        => new(rowNumber, code, ModbusPointTableImportIssueSeverity.Warning, field, message);

    private static ModbusPointTableImportIssue Error(int rowNumber, string field, string code, string message)
        => new(rowNumber, code, ModbusPointTableImportIssueSeverity.Error, field, message);

    private static bool TryResolveColumn(string header, out string canonicalName)
        => ColumnAliases.TryGetValue(NormalizeColumnName(header), out canonicalName!);

    private static string NormalizeColumnName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static bool TryParseRegisterType(string value, out ModbusRegisterType registerType)
    {
        switch (NormalizeColumnName(value))
        {
            case "coil":
            case "coils":
            case "0x":
            case "线圈":
                registerType = ModbusRegisterType.Coil;
                return true;
            case "discreteinput":
            case "discreteinputs":
            case "discrete":
            case "inputstatus":
            case "1x":
            case "离散输入":
            case "开关量输入":
                registerType = ModbusRegisterType.DiscreteInput;
                return true;
            case "inputregister":
            case "inputregisters":
            case "3x":
            case "输入寄存器":
                registerType = ModbusRegisterType.InputRegister;
                return true;
            case "holdingregister":
            case "holdingregisters":
            case "register":
            case "registers":
            case "4x":
            case "保持寄存器":
            case "寄存器":
                registerType = ModbusRegisterType.HoldingRegister;
                return true;
            default:
                registerType = default;
                return false;
        }
    }

    private static bool TryParseAddress(
        ModbusRegisterType rowRegisterType,
        string value,
        out int zeroBasedAddress,
        out string canonicalAddress,
        out string error)
    {
        zeroBasedAddress = 0;
        canonicalAddress = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Modbus address is required.";
            return false;
        }

        var text = value.Trim();
        var addressRegisterType = rowRegisterType;
        var separatorIndex = text.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex >= 0)
        {
            var registerTypeText = text[..separatorIndex];
            if (!TryParseRegisterType(registerTypeText, out addressRegisterType))
            {
                error = $"Modbus address prefix '{registerTypeText}' is not supported.";
                return false;
            }

            if (addressRegisterType != rowRegisterType)
            {
                error = $"Modbus address prefix '{registerTypeText}' does not match registerType '{ToWireValue(rowRegisterType)}'.";
                return false;
            }

            text = text[(separatorIndex + 1)..].Trim();
        }

        text = text.Replace("_", string.Empty, StringComparison.Ordinal);
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var address) || address < 0)
        {
            error = $"Modbus address '{value}' is not a non-negative integer.";
            return false;
        }

        var displayBase = GetDisplayBase(addressRegisterType);
        var displayMax = displayBase + GetDisplayWidth(addressRegisterType) - 1;
        var usesDisplayNotation = address >= displayBase && address <= displayMax;
        if (usesDisplayNotation)
        {
            zeroBasedAddress = address - displayBase;
            canonicalAddress = $"{ToWireValue(addressRegisterType)}:{address.ToString(CultureInfo.InvariantCulture).PadLeft(5, '0')}";
            return true;
        }

        if (address <= 65535)
        {
            zeroBasedAddress = address;
            canonicalAddress = zeroBasedAddress.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        error = $"Modbus address '{value}' is outside the valid zero-based range 0..65535.";
        return false;
    }

    private static int GetDisplayBase(ModbusRegisterType registerType)
        => registerType switch
        {
            ModbusRegisterType.Coil => 1,
            ModbusRegisterType.DiscreteInput => 10001,
            ModbusRegisterType.InputRegister => 30001,
            ModbusRegisterType.HoldingRegister => 40001,
            _ => 0
        };

    private static int GetDisplayWidth(ModbusRegisterType registerType)
        => registerType == ModbusRegisterType.Coil ? 99999 : 9999;

    private static SemanticPointAccess ParseAccess(string value, ModbusRegisterType registerType, string functionCodeText)
    {
        var normalized = NormalizeColumnName(value);
        if (normalized is "write" or "w" or "写")
        {
            return SemanticPointAccess.Write;
        }

        if (normalized is "readwrite" or "rw" or "读写")
        {
            return SemanticPointAccess.ReadWrite;
        }

        if (normalized is "command" or "cmd" or "control" or "命令" or "控制")
        {
            return SemanticPointAccess.Command;
        }

        if (normalized is "config" or "configuration" or "配置")
        {
            return SemanticPointAccess.Config;
        }

        if (int.TryParse(functionCodeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var functionCode)
            && functionCode is 5 or 6 or 15 or 16
            && registerType is ModbusRegisterType.Coil or ModbusRegisterType.HoldingRegister)
        {
            return SemanticPointAccess.Write;
        }

        return SemanticPointAccess.Read;
    }

    private static int ParseFunctionCode(string value, ModbusRegisterType registerType, SemanticPointAccess access)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var functionCode))
        {
            return functionCode;
        }

        if (access is SemanticPointAccess.Write or SemanticPointAccess.Command or SemanticPointAccess.Config or SemanticPointAccess.ReadWrite)
        {
            return registerType == ModbusRegisterType.Coil ? 5 : 6;
        }

        return registerType switch
        {
            ModbusRegisterType.Coil => 1,
            ModbusRegisterType.DiscreteInput => 2,
            ModbusRegisterType.InputRegister => 4,
            _ => 3
        };
    }

    private static void ValidateFunctionAndCount(
        int rowNumber,
        ModbusRegisterType registerType,
        int functionCode,
        int registerCount,
        ICollection<ModbusPointTableImportIssue> issues)
    {
        var allowed = registerType switch
        {
            ModbusRegisterType.Coil => functionCode is 1 or 5 or 15,
            ModbusRegisterType.DiscreteInput => functionCode == 2,
            ModbusRegisterType.InputRegister => functionCode == 4,
            ModbusRegisterType.HoldingRegister => functionCode is 3 or 6 or 16,
            _ => false
        };
        if (!allowed)
        {
            issues.Add(Error(rowNumber, Fields.FunctionCode, ModbusPointTableImportIssueCodes.InvalidRegister, $"functionCode '{functionCode}' is not valid for registerType '{ToWireValue(registerType)}'."));
        }

        var maxCount = registerType is ModbusRegisterType.Coil or ModbusRegisterType.DiscreteInput ? 2000 : 125;
        if (registerCount < 1 || registerCount > maxCount)
        {
            issues.Add(Error(rowNumber, Fields.RegisterCount, ModbusPointTableImportIssueCodes.InvalidRegister, $"registerCount must be between 1 and {maxCount}."));
        }

        if (functionCode is 5 or 6 && registerCount != 1)
        {
            issues.Add(Error(rowNumber, Fields.RegisterCount, ModbusPointTableImportIssueCodes.InvalidRegister, $"functionCode '{functionCode}' writes exactly one Modbus address."));
        }
    }

    private static SemanticDataType ParseDataType(
        string value,
        ModbusRegisterType registerType,
        int rowNumber,
        ICollection<ModbusPointTableImportIssue> issues)
    {
        switch (NormalizeColumnName(value))
        {
            case "bool":
            case "boolean":
            case "bit":
            case "布尔":
            case "开关":
                return SemanticDataType.Boolean;
            case "int":
            case "int16":
            case "int32":
            case "uint16":
            case "uint32":
            case "integer":
            case "整数":
                return SemanticDataType.Int;
            case "float":
            case "single":
            case "double":
            case "real":
            case "浮点":
                return SemanticDataType.Float;
            case "decimal":
            case "number":
            case "数值":
                return SemanticDataType.Decimal;
            case "enum":
            case "枚举":
                return SemanticDataType.Enum;
            case "string":
            case "text":
            case "字符串":
                return SemanticDataType.String;
            case "":
                issues.Add(Pending(rowNumber, Fields.DataType, ModbusPointTableImportIssueCodes.DataTypePending, "dataType is missing and must be reviewed manually."));
                return registerType is ModbusRegisterType.Coil or ModbusRegisterType.DiscreteInput
                    ? SemanticDataType.Boolean
                    : SemanticDataType.String;
            default:
                issues.Add(Pending(rowNumber, Fields.DataType, ModbusPointTableImportIssueCodes.DataTypePending, $"dataType '{value}' is not recognized and must be reviewed manually."));
                return SemanticDataType.String;
        }
    }

    private static int ParsePositiveInt(string value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : fallback;

    private static int ParseNonNegativeInt(string value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0 ? parsed : fallback;

    private static int TryParseInt(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static decimal ParseDecimal(string value, decimal fallback)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static ModbusByteOrder ParseByteOrder(string value)
        => NormalizeColumnName(value) is "littleendian" or "le" or "little" ? ModbusByteOrder.LittleEndian : ModbusByteOrder.BigEndian;

    private static ModbusWordOrder ParseWordOrder(string value)
        => NormalizeColumnName(value) is "littleendian" or "le" or "little" ? ModbusWordOrder.LittleEndian : ModbusWordOrder.BigEndian;

    private static string ToWireValue(ModbusRegisterType registerType)
        => registerType switch
        {
            ModbusRegisterType.Coil => "coil",
            ModbusRegisterType.DiscreteInput => "discrete-input",
            ModbusRegisterType.InputRegister => "input-register",
            _ => "holding-register"
        };

    private static string ToWireValue(ModbusByteOrder order)
        => order == ModbusByteOrder.LittleEndian ? "littleEndian" : "bigEndian";

    private static string ToWireValue(ModbusWordOrder order)
        => order == ModbusWordOrder.LittleEndian ? "littleEndian" : "bigEndian";

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

    private static IReadOnlyList<string> NormalizeAssetPath(string value)
        => value
            .Trim()
            .Trim('/')
            .Split(['/', '\\', '>', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeIdentifierForId)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

    private static AssetDraft? CreateOrGetAssetDraft(
        IDictionary<string, AssetDraft> assets,
        string assetIdValue,
        string assetPathValue,
        string semanticId)
    {
        if (string.IsNullOrWhiteSpace(assetIdValue) && string.IsNullOrWhiteSpace(assetPathValue))
        {
            return null;
        }

        var path = NormalizeAssetPath(Coalesce(assetPathValue, assetIdValue));
        if (path.Count == 0)
        {
            return null;
        }

        var assetId = string.IsNullOrWhiteSpace(assetIdValue)
            ? $"asset.{string.Join(".", path)}"
            : assetIdValue.Trim();
        if (assets.TryGetValue(assetId, out var existing))
        {
            return existing;
        }

        var displayPath = "/" + string.Join("/", path);
        var asset = new AssetDraft(
            assetId,
            path[^1],
            string.IsNullOrWhiteSpace(assetPathValue) ? assetIdValue : assetPathValue,
            displayPath,
            path,
            []);
        assets[assetId] = asset;
        return asset;
    }

    private static Dictionary<string, JsonElement> ToMetadata(IReadOnlyList<ModbusPointTableImportIssue> rowIssues)
    {
        var metadata = new Dictionary<string, JsonElement>
        {
            ["draftStatus"] = JsonSerializer.SerializeToElement(rowIssues.Count == 0 ? "ready" : "pending")
        };
        if (rowIssues.Count > 0)
        {
            metadata["completionIssues"] = JsonSerializer.SerializeToElement(rowIssues.Select(issue => issue.Code).ToArray());
        }

        return metadata;
    }

    private static ModbusPointTableImportResult CreateEmptyResult(
        IReadOnlyList<ModbusPointTableImportIssue> issues,
        ModbusPointTableImportOptions? importOptions)
    {
        var options = importOptions ?? new ModbusPointTableImportOptions();
        var model = new SemanticModel
        {
            ModelId = options.ModelId,
            Name = options.ModelName,
            Description = "Empty Modbus point table import draft."
        };

        return new ModbusPointTableImportResult(
            model,
            [],
            [],
            [],
            [],
            issues,
            SemanticModelValidator.Validate(model));
    }

    private static readonly string[] RequiredColumnNames =
    [
        Fields.SemanticId,
        Fields.RegisterType,
        Fields.Address
    ];

    private static readonly string[] PendingColumnNames =
    [
        Fields.QuantityKind,
        Fields.Dimension,
        Fields.Unit,
        Fields.AssetPath
    ];

    private static readonly IReadOnlyDictionary<string, string> ColumnAliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["semanticid"] = Fields.SemanticId,
        ["semanticpointid"] = Fields.SemanticId,
        ["pointid"] = Fields.SemanticId,
        ["tag"] = Fields.SemanticId,
        ["tagname"] = Fields.SemanticId,
        ["语义id"] = Fields.SemanticId,
        ["点位id"] = Fields.SemanticId,
        ["点名"] = Fields.SemanticId,

        ["displayname"] = Fields.DisplayName,
        ["name"] = Fields.DisplayName,
        ["pointname"] = Fields.DisplayName,
        ["label"] = Fields.DisplayName,
        ["显示名"] = Fields.DisplayName,
        ["点位名"] = Fields.DisplayName,
        ["名称"] = Fields.DisplayName,

        ["quantitykind"] = Fields.QuantityKind,
        ["quantity"] = Fields.QuantityKind,
        ["kind"] = Fields.QuantityKind,
        ["physicalquantity"] = Fields.QuantityKind,
        ["物理量"] = Fields.QuantityKind,
        ["量类型"] = Fields.QuantityKind,
        ["语义类型"] = Fields.QuantityKind,

        ["dimension"] = Fields.Dimension,
        ["dimensionexpression"] = Fields.Dimension,
        ["量纲"] = Fields.Dimension,
        ["维度"] = Fields.Dimension,

        ["unit"] = Fields.Unit,
        ["unitcode"] = Fields.Unit,
        ["engineeringunit"] = Fields.Unit,
        ["单位"] = Fields.Unit,

        ["datatype"] = Fields.DataType,
        ["type"] = Fields.DataType,
        ["valuetype"] = Fields.DataType,
        ["数据类型"] = Fields.DataType,
        ["值类型"] = Fields.DataType,

        ["access"] = Fields.Access,
        ["rw"] = Fields.Access,
        ["访问"] = Fields.Access,
        ["读写"] = Fields.Access,

        ["assetid"] = Fields.AssetId,
        ["资产id"] = Fields.AssetId,
        ["assetpath"] = Fields.AssetPath,
        ["asset"] = Fields.AssetPath,
        ["assetowner"] = Fields.AssetPath,
        ["owner"] = Fields.AssetPath,
        ["资产路径"] = Fields.AssetPath,
        ["资产归属"] = Fields.AssetPath,

        ["registertype"] = Fields.RegisterType,
        ["area"] = Fields.RegisterType,
        ["areatype"] = Fields.RegisterType,
        ["registerarea"] = Fields.RegisterType,
        ["寄存器类型"] = Fields.RegisterType,
        ["寄存器区"] = Fields.RegisterType,
        ["功能区"] = Fields.RegisterType,

        ["address"] = Fields.Address,
        ["register"] = Fields.Address,
        ["registeraddress"] = Fields.Address,
        ["offsetaddress"] = Fields.Address,
        ["寄存器地址"] = Fields.Address,
        ["地址"] = Fields.Address,

        ["functioncode"] = Fields.FunctionCode,
        ["fc"] = Fields.FunctionCode,
        ["功能码"] = Fields.FunctionCode,

        ["unitid"] = Fields.UnitId,
        ["slaveid"] = Fields.UnitId,
        ["station"] = Fields.UnitId,
        ["stationnumber"] = Fields.UnitId,
        ["从站"] = Fields.UnitId,
        ["从站号"] = Fields.UnitId,
        ["站号"] = Fields.UnitId,

        ["registercount"] = Fields.RegisterCount,
        ["count"] = Fields.RegisterCount,
        ["length"] = Fields.RegisterCount,
        ["words"] = Fields.RegisterCount,
        ["寄存器数量"] = Fields.RegisterCount,
        ["长度"] = Fields.RegisterCount,

        ["scale"] = Fields.Scale,
        ["factor"] = Fields.Scale,
        ["倍率"] = Fields.Scale,
        ["比例"] = Fields.Scale,
        ["offset"] = Fields.Offset,
        ["偏移"] = Fields.Offset,

        ["byteorder"] = Fields.ByteOrder,
        ["字节序"] = Fields.ByteOrder,
        ["wordorder"] = Fields.WordOrder,
        ["字序"] = Fields.WordOrder,

        ["endpointref"] = Fields.EndpointRef,
        ["endpoint"] = Fields.EndpointRef,
        ["端点"] = Fields.EndpointRef
    };

    private static class Fields
    {
        public const string SemanticId = "semanticId";
        public const string DisplayName = "displayName";
        public const string QuantityKind = "quantityKind";
        public const string Dimension = "dimension";
        public const string Unit = "unit";
        public const string DataType = "dataType";
        public const string Access = "access";
        public const string AssetId = "assetId";
        public const string AssetPath = "assetPath";
        public const string RegisterType = "registerType";
        public const string Address = "address";
        public const string FunctionCode = "functionCode";
        public const string UnitId = "unitId";
        public const string RegisterCount = "registerCount";
        public const string Scale = "scale";
        public const string Offset = "offset";
        public const string ByteOrder = "byteOrder";
        public const string WordOrder = "wordOrder";
        public const string EndpointRef = "endpointRef";
    }

    private sealed record RawPointTableRow(int RowNumber, IReadOnlyList<string> Values);

    private sealed record PointTableRow(int RowNumber, IReadOnlyDictionary<string, string> Values);

    private sealed record ParsedPointTable(IReadOnlyList<PointTableRow> Rows, ISet<string> PresentCanonicalColumns);

    private sealed record AssetDraft(
        string AssetId,
        string Name,
        string DisplayName,
        string DisplayPath,
        IReadOnlyList<string> AssetPath,
        List<string> Points)
    {
        public Asset ToAsset()
            => new()
            {
                AssetId = AssetId,
                Name = Name,
                DisplayName = DisplayName,
                AssetType = SemanticAssetType.Custom,
                AssetPath = [.. AssetPath],
                Points = [.. Points]
            };

        public SemanticAssetNode ToNode()
            => new(0, AssetId, DisplayName, "custom", DisplayPath, $"{Points.Count} imported point(s).", false);
    }
}

public sealed record ModbusPointTableImportOptions
{
    public string ModelId { get; init; } = "semantic-model-modbus-import-draft";

    public string ModelName { get; init; } = "Modbus point table import draft";

    public SemanticProtocolKind ProtocolKind { get; init; } = SemanticProtocolKind.ModbusTcp;

    public string EndpointRef { get; init; } = "modbus-endpoint.draft";

    public int DefaultUnitId { get; init; } = 1;
}

public sealed record ModbusPointTableInputRow
{
    public int RowNumber { get; init; } = 1;

    public string? SemanticId { get; init; }

    public string? DisplayName { get; init; }

    public string? QuantityKind { get; init; }

    public string? Dimension { get; init; }

    public string? Unit { get; init; }

    public string? DataType { get; init; }

    public string? Access { get; init; }

    public string? AssetId { get; init; }

    public string? AssetPath { get; init; }

    public string? RegisterType { get; init; }

    public string? Address { get; init; }

    public string? FunctionCode { get; init; }

    public string? UnitId { get; init; }

    public string? RegisterCount { get; init; }

    public string? Scale { get; init; }

    public string? Offset { get; init; }

    public string? ByteOrder { get; init; }

    public string? WordOrder { get; init; }

    public string? EndpointRef { get; init; }
}

public sealed record ModbusPointTableImportResult(
    SemanticModel SemanticModel,
    IReadOnlyList<SemanticPointDraft> PointDrafts,
    IReadOnlyList<ProtocolBindingDraft> BindingDrafts,
    IReadOnlyList<SemanticAssetNode> AssetDrafts,
    IReadOnlyList<ModbusPointTableImportedRow> Rows,
    IReadOnlyList<ModbusPointTableImportIssue> Issues,
    IReadOnlyList<SemanticValidationDiagnostic> SemanticDiagnostics)
{
    public bool HasErrors => Issues.Any(issue => issue.Severity == ModbusPointTableImportIssueSeverity.Error);

    public int PendingCompletionCount => Issues.Count(issue =>
        issue.Severity == ModbusPointTableImportIssueSeverity.Warning
        && issue.Code != ModbusPointTableImportIssueCodes.CompletionColumnMissing);
}

public sealed record ModbusPointTableImportedRow(
    int RowNumber,
    string SemanticId,
    string Status,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<ModbusPointTableImportIssue> Issues);

public sealed record ModbusPointTableImportIssue(
    int RowNumber,
    string Code,
    ModbusPointTableImportIssueSeverity Severity,
    string Field,
    string Message,
    string? SemanticId = null);

public enum ModbusPointTableImportIssueSeverity
{
    Info,
    Warning,
    Error
}

public enum ModbusPointTableTextFormat
{
    Auto,
    Csv,
    Tsv
}

public static class ModbusPointTableImportIssueCodes
{
    public const string InputEmpty = "modbus_point_table.input.empty";
    public const string RequiredColumnMissing = "modbus_point_table.column.required";
    public const string CompletionColumnMissing = "modbus_point_table.column.completion_missing";
    public const string SemanticIdRequired = "modbus_point_table.semantic_id.required";
    public const string QuantityKindPending = "modbus_point_table.quantity_kind.pending";
    public const string DimensionPending = "modbus_point_table.dimension.pending";
    public const string UnitPending = "modbus_point_table.unit.pending";
    public const string AssetOwnerPending = "modbus_point_table.asset_owner.pending";
    public const string DataTypePending = "modbus_point_table.data_type.pending";
    public const string InvalidRegister = "modbus_point_table.register.invalid";
    public const string DuplicateSemanticId = "modbus_point_table.semantic_id.duplicate";
    public const string DuplicateBindingSource = "modbus_point_table.binding_source.duplicate";
}
