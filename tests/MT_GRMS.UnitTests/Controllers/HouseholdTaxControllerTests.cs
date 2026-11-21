using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Tax;
using MT_GRMS.Application.Tax.Dtos;
using MT_GRMS.Domain.Enums;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class HouseholdTaxControllerTests
{
    private readonly Mock<IHouseholdTaxService> _mockTaxService;
    private readonly HouseholdTaxController _controller;

    public HouseholdTaxControllerTests()
    {
        _mockTaxService = new Mock<IHouseholdTaxService>();
        _controller = new HouseholdTaxController(_mockTaxService.Object);
    }

    [Fact]
    public async Task GetSummary_ShouldReturnOk_WithSummary()
    {
        var summary = new HouseholdTaxSummaryDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            GrossRevenueInPeriod = 100_000_000m,
            TotalTaxAmount = 1_500_000m
        };

        _mockTaxService
            .Setup(s => s.GetTaxSummaryAsync(It.IsAny<HouseholdTaxFilterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        var actionResult = await _controller.GetSummary(
            2026, TaxPeriodType.Quarter, 1, null, TaxCalculationMethod.RevenuePercentage, null, null, false, null, null, null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<HouseholdTaxSummaryDto>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(100_000_000m, apiResponse.Data?.GrossRevenueInPeriod);
    }

    [Fact]
    public async Task GetBookDetails_ShouldReturnOk_WithBooks()
    {
        var list = new List<AccountingBookEntryDto>();
        _mockTaxService
            .Setup(s => s.GetBookDetailsAsync(2026, TaxPeriodType.Quarter, 1, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var actionResult = await _controller.GetBookDetails(2026, TaxPeriodType.Quarter, 1, null, null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<List<AccountingBookEntryDto>>>(okResult.Value);
        Assert.True(apiResponse.Success);
    }

    [Fact]
    public async Task CreateSnapshot_ShouldReturnUnauthorized_WhenClaimsMissing()
    {
        var httpContext = new DefaultHttpContext();
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1
        };

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "usr-01"),
            new(ClaimTypes.Name, "Owner Name")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        var snapshot = new HouseholdTaxSnapshotDto
        {
            Id = Guid.NewGuid(),
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            TaxableRevenueInPeriod = 50_000_000m,
            TotalTaxAmount = 750_000m
        };

        _mockTaxService
            .Setup(s => s.FinalizeTaxSnapshotAsync(request, "Owner Name", "usr-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var actionResult = await _controller.CreateSnapshot(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<HouseholdTaxSnapshotDto>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(snapshot.Id, apiResponse.Data?.Id);
    }

    [Fact]
    public async Task GetSnapshots_ShouldReturnOk_WithList()
    {
        var snapshots = new List<HouseholdTaxSnapshotDto>();
        _mockTaxService
            .Setup(s => s.GetSnapshotsAsync(2026, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var actionResult = await _controller.GetSnapshots(2026, null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<List<HouseholdTaxSnapshotDto>>>(okResult.Value);
        Assert.True(apiResponse.Success);
    }

    [Fact]
    public async Task GetSnapshotById_ShouldReturnNotFound_WhenNull()
    {
        var id = Guid.NewGuid();
        _mockTaxService
            .Setup(s => s.GetSnapshotByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HouseholdTaxSnapshotDto?)null);

        var actionResult = await _controller.GetSnapshotById(id, CancellationToken.None);

        var notFoundResult = Assert.IsType<NotFoundObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<HouseholdTaxSnapshotDto>>(notFoundResult.Value);
        Assert.False(apiResponse.Success);
    }

    [Fact]
    public async Task GetSnapshotById_ShouldReturnOk_WhenFound()
    {
        var id = Guid.NewGuid();
        var snapshot = new HouseholdTaxSnapshotDto { Id = id, Year = 2026 };
        _mockTaxService
            .Setup(s => s.GetSnapshotByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var actionResult = await _controller.GetSnapshotById(id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<HouseholdTaxSnapshotDto>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(id, apiResponse.Data?.Id);
    }

    [Fact]
    public async Task VerifyIntegrity_ShouldReturnOk_ForValidAndInvalid()
    {
        var id = Guid.NewGuid();
        _mockTaxService
            .Setup(s => s.VerifySnapshotIntegrityAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var actionResult = await _controller.VerifyIntegrity(id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Contains("toàn vẹn", apiResponse.Message);

        _mockTaxService
            .Setup(s => s.VerifySnapshotIntegrityAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var actionResult2 = await _controller.VerifyIntegrity(id, CancellationToken.None);
        var okResult2 = Assert.IsType<OkObjectResult>(actionResult2.Result);
        var apiResponse2 = Assert.IsType<ApiResponse<object>>(okResult2.Value);
        Assert.Contains("Cảnh báo", apiResponse2.Message);
    }

    [Fact]
    public async Task ExportExcel_ShouldReturnFileResult()
    {
        var mockExport = new Mock<ITaxExportService>();
        var fakeBytes = new byte[] { 1, 2, 3, 4 };

        mockExport
            .Setup(e => e.ExportTaxSummaryToExcelAsync(It.IsAny<HouseholdTaxFilterDto>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeBytes);

        var actionResult = await _controller.ExportExcel(
            2026, TaxPeriodType.Quarter, 1, null, null, null, null, null, null, null, mockExport.Object, CancellationToken.None);

        var fileResult = Assert.IsType<FileContentResult>(actionResult);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileResult.ContentType);
        Assert.Contains("BaoCaoThue_2026_Q1_Full152.xlsx", fileResult.FileDownloadName);
        Assert.Equal(fakeBytes, fileResult.FileContents);
    }
}
