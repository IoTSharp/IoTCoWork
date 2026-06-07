using System.IO.Compression;
using System.Globalization;
using System.Text;
using IoTCoWork.Workbench.Services;
using IoTSharp.Contracts.Semantic;

namespace IoTCoWork.Workbench.Core.Tests;

public sealed class ModbusPointTableImporterTests
{
    private readonly ModbusPointTableImporter _importer = new();

    [Fact]
    public void ValidCsvPointTable_CreatesSemanticPointsAndModbusBindings()
    {
        var result = _importer.ImportText(ModbusPointTableImporter.SampleCsv);

        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)));
        Assert.Equal(3, result.SemanticModel.SemanticPoints.Count);
        Assert.Equal(3, result.SemanticModel.ProtocolBindings.Count);
        Assert.Equal(2, result.SemanticModel.Assets.Count);

        var temperature = result.SemanticModel.SemanticPoints.Single(point => point.SemanticId == "compressor.unit01.outlet.temperature");
        Assert.Equal("temperature", temperature.Quantity.QuantityKind);
        Assert.Equal("temperature", temperature.Quantity.Dimension);
        Assert.Equal("Cel", temperature.Unit.Code);
        Assert.NotNull(temperature.AssetId);
        Assert.Equal(SemanticDataType.Float, temperature.DataType);

        var binding = result.SemanticModel.ProtocolBindings.Single(item => item.BindingId == temperature.Source.BindingId);
        Assert.Equal(SemanticProtocolKind.ModbusTcp, binding.ProtocolKind);
        Assert.Equal("holding-register:40001", binding.Address);
        Assert.NotNull(binding.Modbus);
        Assert.Equal(ModbusRegisterType.HoldingRegister, binding.Modbus.RegisterType);
        Assert.Equal(0, binding.Modbus.Address);
        Assert.Equal(3, binding.Modbus.FunctionCode);
        Assert.Equal(2, binding.Modbus.RegisterCount);
        Assert.Equal(0.1m, binding.Modbus.Scale);
        Assert.Equal(ModbusWordOrder.LittleEndian, binding.Modbus.WordOrder);
    }

    [Fact]
    public void MissingSemanticColumns_MarksDraftAsPendingWithoutGuessing()
    {
        const string csv = """
semanticId,displayName,dataType,access,registerType,address
compressor.unit01.outlet.temperature,Outlet temperature,float,read,holding-register,40001
""";

        var result = _importer.ImportText(csv);

        Assert.False(result.HasErrors);
        Assert.Equal(4, result.PendingCompletionCount);
        Assert.Contains(result.Issues, issue => issue.Code == ModbusPointTableImportIssueCodes.QuantityKindPending);
        Assert.Contains(result.Issues, issue => issue.Code == ModbusPointTableImportIssueCodes.DimensionPending);
        Assert.Contains(result.Issues, issue => issue.Code == ModbusPointTableImportIssueCodes.UnitPending);
        Assert.Contains(result.Issues, issue => issue.Code == ModbusPointTableImportIssueCodes.AssetOwnerPending);

        var point = Assert.Single(result.SemanticModel.SemanticPoints);
        Assert.Equal(string.Empty, point.Quantity.QuantityKind);
        Assert.Null(point.Quantity.Dimension);
        Assert.Equal(string.Empty, point.Unit.Code);
        Assert.Null(point.AssetId);

        var draft = Assert.Single(result.PointDrafts);
        Assert.Equal("pending", draft.Status);
        Assert.Contains(draft.CompletionIssues!, issue => issue.Contains("unit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InvalidRegister_IsRejectedAndDoesNotCreateDraftPoint()
    {
        const string csv = """
semanticId,displayName,quantityKind,dimension,unit,dataType,access,assetPath,registerType,address,functionCode
compressor.unit01.outlet.temperature,Outlet temperature,temperature,temperature,Cel,float,read,/plant/unit,holding-register,999999,3
compressor.unit01.bad.discrete,Bad discrete,state,dimensionless,1,boolean,read,/plant/unit,discrete-input,10001,3
""";

        var result = _importer.ImportText(csv);

        Assert.True(result.HasErrors);
        Assert.Empty(result.SemanticModel.SemanticPoints);
        Assert.Contains(result.Issues, issue => issue.Code == ModbusPointTableImportIssueCodes.InvalidRegister && issue.Field == "address");
        Assert.Contains(result.Issues, issue => issue.Code == ModbusPointTableImportIssueCodes.InvalidRegister && issue.Field == "functionCode");
    }

    [Fact]
    public void DuplicatePointAndRegisterSources_AreReported()
    {
        const string csv = """
semanticId,displayName,quantityKind,dimension,unit,dataType,access,assetPath,registerType,address,functionCode
compressor.unit01.outlet.temperature,Outlet temperature,temperature,temperature,Cel,float,read,/plant/unit,holding-register,40001,3
compressor.unit01.outlet.temperature,Outlet temperature duplicate,temperature,temperature,Cel,float,read,/plant/unit,holding-register,40002,3
compressor.unit01.outlet.pressure,Outlet pressure,pressure,pressure,bar,float,read,/plant/unit,holding-register,40001,3
""";

        var result = _importer.ImportText(csv);

        Assert.True(result.HasErrors);
        Assert.Single(result.SemanticModel.SemanticPoints);
        Assert.Contains(result.Issues, issue => issue.Code == ModbusPointTableImportIssueCodes.DuplicateSemanticId);
        Assert.Contains(result.Issues, issue => issue.Code == ModbusPointTableImportIssueCodes.DuplicateBindingSource);
    }

    [Fact]
    public async Task ValidXlsxPointTable_UsesFirstWorksheet()
    {
        await using var stream = CreateMinimalXlsx(
            [
                ["semanticId", "displayName", "quantityKind", "dimension", "unit", "dataType", "access", "assetPath", "registerType", "address", "functionCode"],
                ["compressor.unit01.outlet.temperature", "Outlet temperature", "temperature", "temperature", "Cel", "float", "read", "/plant/unit", "holding-register", "40001", "3"]
            ]);

        var result = await _importer.ImportXlsxAsync(stream);

        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)));
        var point = Assert.Single(result.SemanticModel.SemanticPoints);
        Assert.Equal("compressor.unit01.outlet.temperature", point.SemanticId);
        var binding = Assert.Single(result.SemanticModel.ProtocolBindings);
        Assert.Equal("holding-register:40001", binding.Address);
    }

    private static MemoryStream CreateMinimalXlsx(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", """
<?xml version="1.0" encoding="UTF-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
</Types>
""");
            AddEntry(archive, "_rels/.rels", """
<?xml version="1.0" encoding="UTF-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
""");
            AddEntry(archive, "xl/workbook.xml", """
<?xml version="1.0" encoding="UTF-8"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets>
    <sheet name="Points" sheetId="1" r:id="rId1"/>
  </sheets>
</workbook>
""");
            AddEntry(archive, "xl/_rels/workbook.xml.rels", """
<?xml version="1.0" encoding="UTF-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
</Relationships>
""");
            AddEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(rows));
        }

        stream.Position = 0;
        return stream;
    }

    private static string BuildWorksheetXml(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        builder.AppendLine("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        builder.AppendLine("<sheetData>");
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowIndex + 1}\">");
            for (var columnIndex = 0; columnIndex < rows[rowIndex].Count; columnIndex++)
            {
                var cellReference = $"{ColumnName(columnIndex)}{rowIndex + 1}";
                builder.Append(CultureInfo.InvariantCulture, $"<c r=\"{cellReference}\" t=\"inlineStr\"><is><t>{EscapeXml(rows[rowIndex][columnIndex])}</t></is></c>");
            }

            builder.AppendLine("</row>");
        }

        builder.AppendLine("</sheetData>");
        builder.AppendLine("</worksheet>");
        return builder.ToString();
    }

    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string ColumnName(int zeroBasedColumn)
    {
        var column = zeroBasedColumn + 1;
        var builder = new StringBuilder();
        while (column > 0)
        {
            column--;
            builder.Insert(0, (char)('A' + column % 26));
            column /= 26;
        }

        return builder.ToString();
    }

    private static string EscapeXml(string value)
        => value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
