using System.Text.Json;
using ClosedXML.Excel;
using Moq;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Repositories;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Admin.Validators;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Services;
using Xunit;

namespace MT_GRMS.UnitTests.Admin;

public class AdminInputBoundariesTests
{
    private static readonly CreateMasterProductRequest Product = new("SKU-1", "Trà", "A", "B", "g", null);
    private static readonly CreatePlanRequest Plan = new("A", "Gói", null, 0, 0, null, null);
    private static readonly UpdatePlatformSettingRequest Settings = new(3, 7, 0, 0, "ACB", "Ngân hàng",
        "123456", "Chủ TK", "1900 6522", "owner@example.com", "Zalo OA", false, null, null);

    [Theory]
    [InlineData("Barcode", 1, 50)]
    [InlineData("Name", 2, 250)]
    [InlineData("Brand", 1, 100)]
    [InlineData("CategoryName", 1, 100)]
    [InlineData("BaseUnit", 1, 30)]
    public void ProductText_EnforcesRequiredMinAndColumnWidth(string field, int min, int max)
    {
        CreateMasterProductRequest With(string? value) => field switch
        {
            "Barcode" => Product with { Barcode = value! },
            "Name" => Product with { Name = value! },
            "Brand" => Product with { Brand = value! },
            "CategoryName" => Product with { CategoryName = value! },
            _ => Product with { BaseUnit = value! }
        };
        foreach (var invalid in new[] { null, "   ", new string('x', min - 1), new string('x', max + 1) })
            Assert.Throws<BusinessException>(() => MasterProductValidator.ValidateCreate(With(invalid)));
        MasterProductValidator.ValidateCreate(With(new string('x', min)));
        MasterProductValidator.ValidateCreate(With(new string('x', max)));
    }

    [Fact]
    public void Product_OptionalFieldsAndNumericBoundaries()
    {
        var limit = Product with { Description = new string('x', 1000), TaxCategory = new string('T', 50),
            ImageUrl = "https://x.test/" + new string('x', 985), SuggestedRetailPrice = 9999999999999999.99m,
            Units = [new(new string('x', 50), 99999999999999.9999m, new string('0', 50))] };
        MasterProductValidator.ValidateCreate(Product);
        MasterProductValidator.ValidateCreate(limit);
        foreach (var invalid in new[] { limit with { Description = limit.Description + "x" },
            limit with { ImageUrl = limit.ImageUrl + "x" }, limit with { ImageUrl = "file:///image" },
            limit with { SuggestedRetailPrice = 10000000000000000m }, limit with { SuggestedRetailPrice = 0.001m },
            limit with { SuggestedRetailPrice = -1m }, limit with { VatRate = 11 }, limit with { TaxCategory = new string('t', 51) },
            limit with { Units = [new(" ", 2, null)] }, limit with { Units = [new("kg", 1m, null)] },
            limit with { Units = [new("kg", 1.00001m, null)] }, limit with { Units = [new("kg", 2, new string('0', 51))] },
            limit with { Units = [null!] } })
            Assert.Throws<BusinessException>(() => MasterProductValidator.ValidateCreate(invalid));
    }

    [Theory]
    [InlineData(6, "abc")]
    [InlineData(7, "abc")]
    [InlineData(7, "12")]
    [InlineData(9, "Thùng:24;Lốc:0")]
    [InlineData(9, "Thùng:x")]
    public async Task Excel_InvalidUpsert_PreservesOriginalAndImportsNextRow(int column, string invalid)
    {
        var repo = new Mock<IMasterProductRepository>();
        var original = new MasterProduct { Barcode = "A", Name = "Original", Brand = "X", CategoryName = "Y", BaseUnit = "g" };
        repo.Setup(r => r.GetByBarcodeWithUnitsAsync("A", default)).ReturnsAsync(original);
        var service = new MasterCatalogExcelService(repo.Object, Mock.Of<IUnitOfWork>());
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Import");
        sheet.Cell(1, 1).Value = "Barcode";
        sheet.Cell(2, 1).Value = "A";
        sheet.Cell(2, 2).Value = "Changed";
        sheet.Cell(2, column).Value = invalid;
        sheet.Cell(3, 1).Value = "B";
        sheet.Cell(3, 2).Value = "Valid";
        sheet.Cell(3, 3).Value = "X";
        sheet.Cell(3, 4).Value = "Y";
        sheet.Cell(3, 5).Value = "g";
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        stream.Position = 0;
        var result = await service.ImportExcelAsync(stream);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(2, result.Rows.Single(x => !x.IsSuccess).RowNumber);
        Assert.Equal("Original", original.Name);
        Assert.Empty(original.Units);
        repo.Verify(r => r.Update(original), Times.Never);
    }

    [Fact]
    public async Task Excel_RejectsUnreadableWorkbook()
    {
        var service = new MasterCatalogExcelService(Mock.Of<IMasterProductRepository>(), Mock.Of<IUnitOfWork>());
        using var stream = new MemoryStream("not a spreadsheet"u8.ToArray());
        var error = await Assert.ThrowsAsync<BusinessException>(() => service.ImportExcelAsync(stream));
        Assert.Equal("INVALID_FILE_TYPE", error.ErrorCode);
    }

    [Fact]
    public async Task InvalidUpdate_DoesNotClearExistingUnits()
    {
        var product = new MasterProduct { Name = "Old" };
        product.Units.Add(new MasterProductUnit { UnitName = "Box", ConversionRate = 6 });
        var repo = new Mock<IMasterProductRepository>();
        repo.Setup(r => r.GetByIdWithUnitsAsync(product.Id, default)).ReturnsAsync(product);
        var service = new MasterCatalogService(repo.Object, Mock.Of<IUnitOfWork>());
        await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(product.Id,
            new("New", "A", "B", "g", null, 8, "T", null, null, true, [new("Box", 0, null)])));
        Assert.Equal("Old", product.Name);
        Assert.Single(product.Units);
    }

    [Fact]
    public void Plan_CreateAndUpdate_EnforceTheSameBounds()
    {
        var create = new CreatePlanRequestValidator();
        var update = new UpdatePlanRequestValidator();
        Assert.False(create.Validate(Plan with { Code = new string('A', 51) }).IsValid);
        foreach (var (input, valid) in new[] {
            (Plan with { Code = new string('A', 50), Name = new string('N', 100), MonthlyPrice = 9999999999999999.99m }, true),
            (Plan with { Name = " A " }, false), (Plan with { Name = new string('a', 101) }, false),
            (Plan with { YearlyPrice = 0.001m }, false), (Plan with { MonthlyPrice = 10000000000000000m }, false),
            (Plan with { Quotas = new(BackupFrequency: "Other") }, false) })
        {
            Assert.Equal(valid, create.Validate(input).IsValid);
            Assert.Equal(valid, update.Validate(new UpdatePlanRequest(input.Name, null, input.MonthlyPrice, input.YearlyPrice, input.Quotas, null)).IsValid);
        }
    }

    [Fact]
    public void Json_MissingPriceOrStatusIsRejected_ExplicitZeroAndFalseAllowed()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreatePlanRequest>("{}"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdatePlanStatusRequest>("{}"));
        Assert.False(JsonSerializer.Deserialize<UpdatePlanStatusRequest>("""{"IsActive":false}""")!.IsActive);
        Assert.Equal(0m, JsonSerializer.Deserialize<CreatePlanRequest>(JsonSerializer.Serialize(Plan))!.MonthlyPrice);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void TenantActions_EnforceReasonBounds(int length, bool valid)
    {
        var reason = new string('a', length);
        Assert.Equal(valid, new LockTenantRequestValidator().Validate(new LockTenantRequest("HARD", reason)).IsValid);
        Assert.Equal(valid, new ExtendGraceRequestValidator().Validate(new ExtendGraceRequest(7, reason)).IsValid);
    }

    [Fact]
    public void CreateTenant_ValidatesOwnerCredentialsAndOptionalFieldLimits()
    {
        var validator = new CreateTenantRequestValidator();
        var tenant = new CreateTenantRequest("21", "shop-21", "Nhật An", "owner@example.vn", "0912345678", "Aa12345!");
        Assert.True(validator.Validate(tenant).IsValid);
        foreach (var input in new[] { tenant with { Name = "X" }, tenant with { OwnerName = "An1" },
            tenant with { OwnerEmail = "a@b..c" }, tenant with { OwnerPhone = "091234567８" },
            tenant with { Password = "Aa12345 " }, tenant with { Subdomain = "-shop" },
            tenant with { Address = new string('a', 256) }, tenant with { City = new string('a', 101) },
            tenant with { TaxCode = new string('1', 51) } })
            Assert.False(validator.Validate(input).IsValid);
    }

    [Fact]
    public void PlatformSettings_EnforceBoundsAndFormats()
    {
        var validator = new UpdatePlatformSettingValidator();
        Assert.True(validator.Validate(Settings with { MaintenanceMessage = new string('a', 500) }).IsValid);
        foreach (var invalid in new[] { Settings with { MaintenanceMessage = new string('a', 501) },
            Settings with { SupportHotline = "ABCDEFGH" }, Settings with { MasterAccountName = " A " },
            Settings with { SupportEmail = "a@b..c" }, Settings with { BranchAddonPriceMonthly = 1.001m },
            Settings with { MasterAccountNumber = "12 34" }, Settings with { MasterBankCode = null! } })
            Assert.False(validator.Validate(invalid).IsValid);
    }
}
