using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Application.Suppliers.Dtos;
using MT_GRMS.Domain.Enums;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class SuppliersControllerTests
{
    private readonly Mock<ISupplierService> _mockSupplierService;
    private readonly SuppliersController _controller;

    public SuppliersControllerTests()
    {
        _mockSupplierService = new Mock<ISupplierService>();
        _controller = new SuppliersController(_mockSupplierService.Object);
    }

    [Fact]
    public async Task GetSuppliers_ReturnsOkWithPagedResult()
    {
        var filter = new SupplierQueryFilter();
        var paged = new PagedResult<SupplierSummaryDto>
        {
            Items = new List<SupplierSummaryDto>
            {
                new() { Id = Guid.NewGuid(), Code = "SUP-01", Name = "Supplier 1" }
            },
            TotalItems = 1,
            Page = 1,
            PageSize = 20
        };
        _mockSupplierService.Setup(s => s.GetSuppliersAsync(filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetSuppliers(filter, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PagedResult<SupplierSummaryDto>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!.Items);
        Assert.Equal("SUP-01", resp.Data.Items[0].Code);
    }

    [Fact]
    public async Task GetSearchSuggestions_ReturnsOkWithSuggestions()
    {
        var expected = new SupplierSearchSuggestionsDto();
        _mockSupplierService.Setup(s => s.GetSearchSuggestionsAsync("test", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _controller.GetSearchSuggestions("test", 10, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierSearchSuggestionsDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Same(expected, resp.Data);
    }

    [Fact]
    public async Task GetById_WhenSupplierExists_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var detail = new SupplierDetailDto { Id = id, Code = "SUP-01", Name = "Supplier 1" };
        _mockSupplierService.Setup(s => s.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await _controller.GetById(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierDetailDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(id, resp.Data!.Id);
    }

    [Fact]
    public async Task GetById_WhenSupplierNotFound_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _mockSupplierService.Setup(s => s.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupplierDetailDto?)null);

        var result = await _controller.GetById(id, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<object>>(notFound.Value);
        Assert.False(resp.Success);
        Assert.Equal("NOT_FOUND", resp.ErrorCode);
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtAction()
    {
        var command = new CreateSupplierCommand { Name = "New Supplier", Code = "SUP-NEW" };
        var created = new SupplierDetailDto { Id = Guid.NewGuid(), Name = "New Supplier", Code = "SUP-NEW" };
        _mockSupplierService.Setup(s => s.CreateSupplierAsync(command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        var result = await _controller.Create(command, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(SuppliersController.GetById), createdResult.ActionName);
        Assert.Equal(created.Id, createdResult.RouteValues!["id"]);
    }

    [Fact]
    public async Task Update_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var command = new UpdateSupplierCommand { Name = "Updated Supplier" };
        var updated = new SupplierDetailDto { Id = id, Name = "Updated Supplier" };
        _mockSupplierService.Setup(s => s.UpdateSupplierAsync(id, command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        var result = await _controller.Update(id, command, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierDetailDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("Updated Supplier", resp.Data!.Name);
    }

    [Fact]
    public async Task UpdateBasic_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var command = new UpdateBasicSupplierCommand { Name = "Updated Basic" };
        var updated = new SupplierDetailDto { Id = id, Name = "Updated Basic" };
        _mockSupplierService.Setup(s => s.UpdateBasicSupplierAsync(id, command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        var result = await _controller.UpdateBasic(id, command, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierDetailDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("Updated Basic", resp.Data!.Name);
    }

    [Fact]
    public async Task UpdateBank_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var command = new UpdateSupplierBankCommand { BankAccountNumber = "123456789" };
        var updated = new SupplierDetailDto { Id = id, BankAccountNumber = "123456789" };
        _mockSupplierService.Setup(s => s.UpdateBankAsync(id, command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        var result = await _controller.UpdateBank(id, command, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierDetailDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("123456789", resp.Data!.BankAccountNumber);
    }

    [Fact]
    public async Task UpdateVat_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var command = new UpdateSupplierVatCommand { CompanyName = "VAT Company" };
        var updated = new SupplierDetailDto { Id = id, CompanyName = "VAT Company" };
        _mockSupplierService.Setup(s => s.UpdateVatAsync(id, command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        var result = await _controller.UpdateVat(id, command, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierDetailDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("VAT Company", resp.Data!.CompanyName);
    }

    [Fact]
    public async Task UpdateStatus_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var request = new UpdateSupplierStatusRequest { Status = SupplierStatus.Active };
        var updated = new SupplierDetailDto { Id = id, Status = SupplierStatus.Active };
        _mockSupplierService.Setup(s => s.UpdateStatusAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        var result = await _controller.UpdateStatus(id, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierDetailDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(SupplierStatus.Active, resp.Data!.Status);
    }

    [Fact]
    public async Task Delete_ReturnsOk()
    {
        var id = Guid.NewGuid();
        _mockSupplierService.Setup(s => s.DeleteSupplierAsync(id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.Delete(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<bool>>(ok.Value);
        Assert.True(resp.Success);
        Assert.True(resp.Data);
    }

    [Fact]
    public async Task GetDeliveryConfig_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var config = new SupplierDeliveryConfigDto { SupplierId = id, MinOrderQuantity = 2 };
        _mockSupplierService.Setup(s => s.GetDeliveryConfigAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var result = await _controller.GetDeliveryConfig(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierDeliveryConfigDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(2, resp.Data!.MinOrderQuantity);
    }

    [Fact]
    public async Task UpdateDeliveryConfig_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var command = new UpdateSupplierDeliveryConfigCommand { MinOrderQuantity = 3 };
        var config = new SupplierDeliveryConfigDto { SupplierId = id, MinOrderQuantity = 3 };
        _mockSupplierService.Setup(s => s.UpdateDeliveryConfigAsync(id, command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var result = await _controller.UpdateDeliveryConfig(id, command, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierDeliveryConfigDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(3, resp.Data!.MinOrderQuantity);
    }

    [Fact]
    public async Task GetReturnPolicy_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var policy = new SupplierReturnPolicyDto { SupplierId = id };
        _mockSupplierService.Setup(s => s.GetReturnPolicyAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        var result = await _controller.GetReturnPolicy(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierReturnPolicyDto>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task UpsertReturnPolicy_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var command = new UpdateSupplierReturnPolicyCommand();
        var policy = new SupplierReturnPolicyDto { SupplierId = id };
        _mockSupplierService.Setup(s => s.UpsertReturnPolicyAsync(id, command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        var result = await _controller.UpsertReturnPolicy(id, command, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierReturnPolicyDto>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task GetApSummary_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var summary = new SupplierApSummaryDto { SupplierId = id, CurrentDebt = 1000m };
        _mockSupplierService.Setup(s => s.GetApSummaryAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        var result = await _controller.GetApSummary(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierApSummaryDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(1000m, resp.Data!.CurrentDebt);
    }

    [Fact]
    public async Task GetTransactions_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var paged = new PagedResult<SupplierTransactionDto>
        {
            Items = new List<SupplierTransactionDto>(),
            TotalItems = 0,
            Page = 1,
            PageSize = 20
        };
        _mockSupplierService.Setup(s => s.GetTransactionsAsync(id, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetTransactions(id, 1, 20, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PagedResult<SupplierTransactionDto>>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task GetAssignedBranches_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        IReadOnlyList<SupplierBranchAssignmentDto> list = new List<SupplierBranchAssignmentDto>
        {
            new() { BranchId = branchId }
        };
        _mockSupplierService.Setup(s => s.GetAssignedBranchesAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var result = await _controller.GetAssignedBranches(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<SupplierBranchAssignmentDto>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!);
    }

    [Fact]
    public async Task ReplaceAssignedBranches_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var command = new ReplaceSupplierBranchesCommand();
        IReadOnlyList<SupplierBranchAssignmentDto> list = new List<SupplierBranchAssignmentDto>();
        _mockSupplierService.Setup(s => s.ReplaceAssignedBranchesAsync(id, command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var result = await _controller.ReplaceAssignedBranches(id, command, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<SupplierBranchAssignmentDto>>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task GetBranchAssignments_ReturnsOk()
    {
        var filter = new SupplierBranchAssignmentQueryFilter();
        var paged = new PagedResult<SupplierBranchAssignmentDetailDto>
        {
            Items = new List<SupplierBranchAssignmentDetailDto>(),
            TotalItems = 0,
            Page = 1,
            PageSize = 20
        };
        _mockSupplierService.Setup(s => s.GetBranchAssignmentsAsync(filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetBranchAssignments(filter, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PagedResult<SupplierBranchAssignmentDetailDto>>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task AssignSupplierToBranches_ReturnsOk()
    {
        var command = new AssignSupplierToBranchesCommand();
        IReadOnlyList<SupplierBranchAssignmentDetailDto> list = new List<SupplierBranchAssignmentDetailDto>();
        _mockSupplierService.Setup(s => s.AssignSupplierToBranchesAsync(command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var result = await _controller.AssignSupplierToBranches(command, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<SupplierBranchAssignmentDetailDto>>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task AssignSuppliersToBranch_ReturnsOk()
    {
        var command = new AssignSuppliersToBranchCommand();
        IReadOnlyList<SupplierBranchAssignmentDetailDto> list = new List<SupplierBranchAssignmentDetailDto>();
        _mockSupplierService.Setup(s => s.AssignSuppliersToBranchAsync(command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var result = await _controller.AssignSuppliersToBranch(command, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<SupplierBranchAssignmentDetailDto>>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task AssignSuppliersToBranchAlias_SetsBranchIdAndReturnsOk()
    {
        var branchId = Guid.NewGuid();
        var command = new AssignSuppliersToBranchCommand();
        IReadOnlyList<SupplierBranchAssignmentDetailDto> list = new List<SupplierBranchAssignmentDetailDto>();
        _mockSupplierService.Setup(s => s.AssignSuppliersToBranchAsync(command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var result = await _controller.AssignSuppliersToBranchAlias(branchId, command, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(branchId, command.BranchId);
    }

    [Fact]
    public async Task UnassignSupplierBranch_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        _mockSupplierService.Setup(s => s.UnassignSupplierBranchAsync(id, branchId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.UnassignSupplierBranch(id, branchId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<object>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Theory]
    [InlineData(SupplierStatus.Inactive, "Inactive", "Đã tạm ngưng phân bổ nhà cung cấp với cửa hàng.")]
    [InlineData(SupplierStatus.Active, "Active", "Đã kích hoạt phân bổ nhà cung cấp với cửa hàng.")]
    public async Task UpdateBranchAssignmentStatus_ReturnsExpectedMessage(SupplierStatus statusEnum, string statusString, string expectedMsg)
    {
        var id = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var request = new UpdateSupplierBranchAssignmentStatusRequest { Status = statusEnum };
        var detail = new SupplierBranchAssignmentDetailDto { Status = statusString };
        _mockSupplierService.Setup(s => s.UpdateBranchAssignmentStatusAsync(id, branchId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await _controller.UpdateBranchAssignmentStatus(id, branchId, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierBranchAssignmentDetailDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(expectedMsg, resp.Message);
    }

    [Fact]
    public async Task UpdateBranchAssignmentDeliveryDays_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var request = new UpdateSupplierBranchAssignmentDeliveryDaysRequest();
        var detail = new SupplierBranchAssignmentDetailDto();
        _mockSupplierService.Setup(s => s.UpdateBranchAssignmentDeliveryDaysAsync(id, branchId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await _controller.UpdateBranchAssignmentDeliveryDays(id, branchId, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierBranchAssignmentDetailDto>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task GetAPLedger_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var paged = new PagedResult<SupplierAPLedgerDto>
        {
            Items = new List<SupplierAPLedgerDto>(),
            TotalItems = 0,
            Page = 1,
            PageSize = 20
        };
        _mockSupplierService.Setup(s => s.GetAPLedgerAsync(id, null, null, 1, 20, It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(paged);

        var result = await _controller.GetAPLedger(id, null, null, null, 1, 20, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PagedResult<SupplierAPLedgerDto>>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task RecordAPTransaction_SetsSupplierIdAndReturnsCreatedAtAction()
    {
        var id = Guid.NewGuid();
        var command = new RecordAPTransactionCommand { Amount = 500m };
        var ledgerDto = new SupplierAPLedgerDto { Id = Guid.NewGuid(), SupplierId = id, Amount = 500m };
        _mockSupplierService.Setup(s => s.RecordAPTransactionAsync(command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ledgerDto);

        var result = await _controller.RecordAPTransaction(id, command, CancellationToken.None);

        Assert.Equal(id, command.SupplierId);
        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(SuppliersController.GetAPLedger), created.ActionName);
        Assert.Equal(id, created.RouteValues!["id"]);
    }

    [Fact]
    public async Task LinkCustomer_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var request = new LinkCustomerRequest { CustomerId = Guid.NewGuid() };
        var detail = new SupplierDetailDto { Id = id };
        _mockSupplierService.Setup(s => s.LinkCustomerAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await _controller.LinkCustomer(id, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierDetailDto>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetAllSupplierProducts_ReturnsOk(bool hasSupplierId)
    {
        var filter = new SupplierProductQueryFilter();
        var supplierId = hasSupplierId ? Guid.NewGuid() : (Guid?)null;
        var expectedId = supplierId ?? Guid.Empty;
        var paged = new PagedResult<SupplierProductDto>
        {
            Items = new List<SupplierProductDto>(),
            TotalItems = 0,
            Page = 1,
            PageSize = 20
        };
        _mockSupplierService.Setup(s => s.GetSupplierProductsAsync(expectedId, filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetAllSupplierProducts(supplierId, filter, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PagedResult<SupplierProductDto>>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task GetSupplierProducts_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var filter = new SupplierProductQueryFilter();
        var paged = new PagedResult<SupplierProductDto>
        {
            Items = new List<SupplierProductDto>(),
            TotalItems = 0,
            Page = 1,
            PageSize = 20
        };
        _mockSupplierService.Setup(s => s.GetSupplierProductsAsync(id, filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetSupplierProducts(id, filter, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PagedResult<SupplierProductDto>>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task AssignProduct_ReturnsCreatedAtAction()
    {
        var id = Guid.NewGuid();
        var command = new AssignSupplierProductCommand();
        var dto = new SupplierProductDto { SupplierId = id, ProductId = Guid.NewGuid() };
        _mockSupplierService.Setup(s => s.AssignSupplierProductAsync(id, command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.AssignProduct(id, command, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(SuppliersController.GetSupplierProducts), created.ActionName);
        Assert.Equal(id, created.RouteValues!["id"]);
    }

    [Fact]
    public async Task BatchAssignProducts_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var command = new BatchAssignSupplierProductsCommand();
        var resultDto = new BatchAssignSupplierProductsResultDto { AssignedCount = 5, SyncedBranchesCount = 2 };
        _mockSupplierService.Setup(s => s.BatchAssignSupplierProductsAsync(id, command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultDto);

        var result = await _controller.BatchAssignProducts(id, command, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<BatchAssignSupplierProductsResultDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(5, resp.Data!.AssignedCount);
    }

    [Fact]
    public async Task UpdateSupplierProduct_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var prodId = Guid.NewGuid();
        var command = new UpdateSupplierProductCommand();
        var dto = new SupplierProductDto { SupplierId = id, ProductId = prodId };
        _mockSupplierService.Setup(s => s.UpdateSupplierProductAsync(id, prodId, command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.UpdateSupplierProduct(id, prodId, command, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierProductDto>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task ToggleProductStatus_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var prodId = Guid.NewGuid();
        var request = new ToggleSupplierProductStatusRequest { Status = SupplierProductStatus.Active };
        var dto = new SupplierProductDto { SupplierId = id, ProductId = prodId, Status = "Active" };
        _mockSupplierService.Setup(s => s.ToggleSupplierProductStatusAsync(id, prodId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.ToggleProductStatus(id, prodId, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierProductDto>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task SetPrimarySupplier_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var prodId = Guid.NewGuid();
        var request = new SetPrimarySupplierRequest { IsPrimarySupplier = true };
        var dto = new SupplierProductDto { SupplierId = id, ProductId = prodId, IsPrimarySupplier = true };
        _mockSupplierService.Setup(s => s.SetPrimarySupplierAsync(id, prodId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.SetPrimarySupplier(id, prodId, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierProductDto>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task ExportSupplierProducts_ReturnsFileResult()
    {
        var id = Guid.NewGuid();
        var mockExcel = new Mock<ISupplierProductExcelService>();
        var fileDto = new SupplierProductExcelFileDto
        {
            Content = new byte[] { 1, 2, 3 },
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileName = "test.xlsx"
        };
        mockExcel.Setup(x => x.ExportAsync(id, It.IsAny<SupplierProductQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileDto);

        var result = await _controller.ExportSupplierProducts(
            id,
            new SupplierProductQueryFilter(),
            mockExcel.Object,
            CancellationToken.None);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal(fileDto.ContentType, fileResult.ContentType);
        Assert.Equal(fileDto.FileName, fileResult.FileDownloadName);
    }

    [Fact]
    public async Task ImportSupplierProducts_NullOrEmptyFile_ReturnsBadRequest()
    {
        var id = Guid.NewGuid();
        var mockExcel = new Mock<ISupplierProductExcelService>();

        var emptyFilter = new SupplierProductQueryFilter();

        // Null file
        var result1 = await _controller.ImportSupplierProducts(id, null, emptyFilter, mockExcel.Object, CancellationToken.None);
        var badReq1 = Assert.IsType<BadRequestObjectResult>(result1);
        var resp1 = Assert.IsType<ApiResponse<object>>(badReq1.Value);
        Assert.Equal("INVALID_FILE", resp1.ErrorCode);

        // Empty file
        var mockFile = new Mock<IFormFile>();
        mockFile.SetupGet(f => f.Length).Returns(0);
        var result2 = await _controller.ImportSupplierProducts(id, mockFile.Object, emptyFilter, mockExcel.Object, CancellationToken.None);
        var badReq2 = Assert.IsType<BadRequestObjectResult>(result2);
        var resp2 = Assert.IsType<ApiResponse<object>>(badReq2.Value);
        Assert.Equal("INVALID_FILE", resp2.ErrorCode);
    }

    [Theory]
    [InlineData(0, "Import thành công 10/10 dòng.")]
    [InlineData(2, "Import xong: 8 thành công, 2 lỗi / 10 dòng.")]
    public async Task ImportSupplierProducts_ValidFile_ReturnsOkWithAppropriateMessage(int failedCount, string expectedMsg)
    {
        var id = Guid.NewGuid();
        var mockExcel = new Mock<ISupplierProductExcelService>();
        var mockFile = new Mock<IFormFile>();
        var content = Encoding.UTF8.GetBytes("fake excel data");
        var ms = new MemoryStream(content);
        mockFile.Setup(f => f.OpenReadStream()).Returns(ms);
        mockFile.SetupGet(f => f.Length).Returns(content.Length);
        mockFile.SetupGet(f => f.FileName).Returns("test.xlsx");

        var importResult = new SupplierProductExcelImportResultDto
        {
            Summary = new SupplierProductExcelImportSummaryDto
            {
                TotalRows = 10,
                SuccessCount = 10 - failedCount,
                FailedCount = failedCount
            }
        };
        mockExcel.Setup(x => x.ImportAsync(
                id,
                It.IsAny<Stream>(),
                "test.xlsx",
                It.IsAny<SupplierProductQueryFilter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(importResult);

        var result = await _controller.ImportSupplierProducts(
            id,
            mockFile.Object,
            new SupplierProductQueryFilter(),
            mockExcel.Object,
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierProductExcelImportResultDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(expectedMsg, resp.Message);
    }
}
