using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Expenses;
using MT_GRMS.Application.Expenses.Dtos;
using MT_GRMS.Domain.Enums;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class ExpensesControllerTests
{
    private readonly Mock<IExpenseService> _mockExpenseService;
    private readonly ExpensesController _controller;

    public ExpensesControllerTests()
    {
        _mockExpenseService = new Mock<IExpenseService>();
        _controller = new ExpensesController(_mockExpenseService.Object);
    }

    private static ExpenseVoucherResponse CreateTestVoucher(
        Guid id,
        string voucherCode = "EXP-001",
        string status = "PENDING",
        decimal amount = 500_000m)
    {
        return new ExpenseVoucherResponse(
            Id: id,
            VoucherCode: voucherCode,
            BranchId: Guid.NewGuid(),
            BeneficiaryName: "Cong ty Dien luc",
            Category: "Utilities",
            Amount: amount,
            Status: status,
            PaymentSourceType: "Cash",
            PaymentSourceAccountId: Guid.NewGuid(),
            IsTaxDeductible: true,
            InvoiceNumber: "HD-01",
            SellerTaxCode: "0100109106",
            EvidenceAttachmentUrl: null,
            IsFlaggedAsSuspiciousSplit: false,
            SuspiciousReason: null,
            ApprovedByUserId: null,
            ApprovedAtUtc: null,
            PaidAtUtc: null,
            ExpenseDate: DateTime.UtcNow,
            Note: "Tien dien");
    }

    [Fact]
    public async Task CreateExpense_ShouldReturnOk_WithCreatedVoucher()
    {
        var request = new CreateExpenseRequest(
            Guid.NewGuid(), "Cong ty Dien luc", "Utilities", 500_000m,
            FinanceAccountType.Till, Guid.NewGuid(), true, "HD-01", "0100109106", null, "Tien dien");
        var responseDto = CreateTestVoucher(Guid.NewGuid(), "EXP-001", "PENDING", 500_000m);

        _mockExpenseService
            .Setup(s => s.CreateExpenseAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseDto);

        var actionResult = await _controller.CreateExpense(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ExpenseVoucherResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal("EXP-001", apiResponse.Data?.VoucherCode);
    }

    [Fact]
    public async Task ApproveExpense_ShouldReturnOk_WithApprovedVoucher()
    {
        var expenseId = Guid.NewGuid();
        var request = new ApproveExpenseRequest(expenseId, "Approved by manager");
        var responseDto = CreateTestVoucher(expenseId, "EXP-001", "APPROVED", 10_000_000m);

        _mockExpenseService
            .Setup(s => s.ApproveExpenseAsync(It.Is<ApproveExpenseRequest>(r => r.ExpenseId == expenseId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseDto);

        var actionResult = await _controller.ApproveExpense(expenseId, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ExpenseVoucherResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal("APPROVED", apiResponse.Data?.Status);
    }

    [Fact]
    public async Task RejectExpense_ShouldReturnOk_WithRejectedVoucher()
    {
        var expenseId = Guid.NewGuid();
        var request = new RejectExpenseRequest(expenseId, "Sai hóa đơn");
        var responseDto = CreateTestVoucher(expenseId, "EXP-001", "REJECTED", 5_000_000m);

        _mockExpenseService
            .Setup(s => s.RejectExpenseAsync(It.Is<RejectExpenseRequest>(r => r.ExpenseId == expenseId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseDto);

        var actionResult = await _controller.RejectExpense(expenseId, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ExpenseVoucherResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal("REJECTED", apiResponse.Data?.Status);
    }

    [Fact]
    public async Task CancelExpense_ShouldReturnOk_WithCancelledVoucher()
    {
        var expenseId = Guid.NewGuid();
        var request = new CancelExpenseRequest(expenseId, "Hủy vì không dùng");
        var responseDto = CreateTestVoucher(expenseId, "EXP-001", "CANCELLED", 1_000_000m);

        _mockExpenseService
            .Setup(s => s.CancelExpenseAsync(It.Is<CancelExpenseRequest>(r => r.ExpenseId == expenseId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseDto);

        var actionResult = await _controller.CancelExpense(expenseId, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ExpenseVoucherResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal("CANCELLED", apiResponse.Data?.Status);
    }

    [Fact]
    public async Task DisburseExpense_ShouldReturnOk_WithDisbursedVoucher()
    {
        var expenseId = Guid.NewGuid();
        var request = new DisburseExpenseRequest(expenseId, FinanceAccountType.Till, Guid.NewGuid(), "Disbursed from till");
        var responseDto = CreateTestVoucher(expenseId, "EXP-001", "DISBURSED", 300_000m);

        _mockExpenseService
            .Setup(s => s.DisburseExpenseAsync(It.Is<DisburseExpenseRequest>(r => r.ExpenseId == expenseId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseDto);

        var actionResult = await _controller.DisburseExpense(expenseId, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ExpenseVoucherResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal("DISBURSED", apiResponse.Data?.Status);
    }

    [Fact]
    public async Task GetExpenseSummary_ShouldReturnOk_WithSummary()
    {
        var branchId = Guid.NewGuid();
        var fromDate = DateTime.UtcNow.AddDays(-7);
        var toDate = DateTime.UtcNow;
        var summary = new ExpenseSummaryResponse(
            TotalAmount: 5_000_000m,
            TotalCount: 5,
            PaidAmount: 2_000_000m,
            PaidCount: 2,
            PendingApprovalAmount: 1_000_000m,
            PendingApprovalCount: 1,
            SuspiciousAmount: 0m,
            SuspiciousCount: 0,
            TaxDeductibleAmount: 2_000_000m,
            TaxDeductibleCount: 2,
            NonTaxDeductibleAmount: 0m,
            NonTaxDeductibleCount: 0);

        _mockExpenseService
            .Setup(s => s.GetExpenseSummaryAsync(branchId, fromDate, toDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        var actionResult = await _controller.GetExpenseSummary(branchId, fromDate, toDate, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ExpenseSummaryResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(5_000_000m, apiResponse.Data?.TotalAmount);
    }

    [Fact]
    public async Task GetExpenseById_ShouldReturnOk_WithVoucher()
    {
        var expenseId = Guid.NewGuid();
        var responseDto = CreateTestVoucher(expenseId, "EXP-001", "PENDING", 150_000m);

        _mockExpenseService
            .Setup(s => s.GetExpenseByIdAsync(expenseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseDto);

        var actionResult = await _controller.GetExpenseById(expenseId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ExpenseVoucherResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(expenseId, apiResponse.Data?.Id);
    }

    [Fact]
    public async Task GetExpenses_ShouldReturnOk_WithList()
    {
        var branchId = Guid.NewGuid();
        var list = new List<ExpenseVoucherResponse>
        {
            CreateTestVoucher(Guid.NewGuid(), "EXP-001", "DISBURSED", 100_000m)
        };

        _mockExpenseService
            .Setup(s => s.GetExpensesAsync(branchId, null, null, 1, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var actionResult = await _controller.GetExpenses(branchId, null, null, 1, 50, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<List<ExpenseVoucherResponse>>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Single(apiResponse.Data!);
    }
}
