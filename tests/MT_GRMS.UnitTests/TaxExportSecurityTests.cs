using ClosedXML.Excel;
using Xunit;

namespace MT_GRMS.UnitTests;

public class TaxExportSecurityTests
{
    [Fact]
    public void ExportCell_WithFormulaPayload_ShouldStoreAsTextWithoutFormulaExecution()
    {
        // Arrange
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("SecurityTest");
        var cell = ws.Cell(1, 1);
        var formulaPayload = "=1+1";

        // Act - Simulate SetTextCell from TaxExportService
        cell.Style.NumberFormat.Format = "@";
        cell.Style.IncludeQuotePrefix = true;
        cell.Value = formulaPayload;

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        ms.Position = 0;

        // Reload workbook from exported stream to inspect OpenXML deserialization
        using var loadedWorkbook = new XLWorkbook(ms);
        var loadedCell = loadedWorkbook.Worksheet("SecurityTest").Cell(1, 1);

        // Assert: The cell MUST be pure Text, never a calculated formula <f>
        Assert.False(loadedCell.HasFormula, "Cell must not contain an executable formula (<f>)");
        Assert.Equal(XLDataType.Text, loadedCell.DataType);
        Assert.Equal("=1+1", loadedCell.GetString());
        Assert.True(loadedCell.Style.IncludeQuotePrefix, "IncludeQuotePrefix must be enabled (quotePrefix=\"1\") to prevent Excel execution upon user editing");
    }

    [Theory]
    [InlineData("=SUM(A1:A10)")]
    [InlineData("-5%")]
    [InlineData("+84901234567")]
    [InlineData("@AdminUser")]
    [InlineData("=cmd|'/C calc'!A0")]
    public void ExportCell_WithPotentiallyDangerousOrSpecialPrefixes_ShouldPreserveLiteralString(string rawValue)
    {
        // Arrange
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("PayloadTest");
        var cell = ws.Cell(2, 2);

        // Act
        cell.Style.NumberFormat.Format = "@";
        cell.Style.IncludeQuotePrefix = true;
        cell.Value = rawValue;

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        ms.Position = 0;

        using var loadedWorkbook = new XLWorkbook(ms);
        var loadedCell = loadedWorkbook.Worksheet("PayloadTest").Cell(2, 2);

        // Assert
        Assert.False(loadedCell.HasFormula);
        Assert.Equal(XLDataType.Text, loadedCell.DataType);
        Assert.Equal(rawValue, loadedCell.GetString());
    }
}

