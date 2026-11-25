using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Cashbook.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Expenses;
using MT_GRMS.Application.Expenses.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class CashbookControllerTests
{
    private readonly Mock<ICashbookService> _mockCashbookService;
    private readonly Mock<IExpenseService> _mockExpenseService;
    private readonly CashbookController _controller;

    public CashbookControllerTests()
    {
        _mockCashbookService = new Mock<ICashbookService>();
        _mockExpenseService = new Mock<IExpenseService>();
        _controller = new CashbookController(_mockCashbookService.Object, _mockExpenseService.Object);
    }

    [Fact]
    public async Task OpenShift_ShouldReturnOk_WithShiftResponse()
    {
        var request = new OpenShiftRequest(Guid.NewGuid(), 1_000_000m, "Dau ca");
        var shiftResponse = new ShiftResponse(
            Guid.NewGuid(),
            request.TillId,
            Guid.NewGuid(),
            request.CountedFloat,
            0m,
            0m,
            1_000_000m,
            null,
            null,
            0m,
            0m,
            "OPEN",
            DateTime.UtcNow,
            null);

        _mockCashbookService
            .Setup(s => s.OpenShiftAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(shiftResponse);

        var actionResult = await _controller.OpenShift(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ShiftResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal("OPEN", apiResponse.Data?.Status);
    }

    [Fact]
    public async Task CloseShift_ShouldReturnOk_WithClosedShift()
    {
        var request = new CloseShiftRequest(Guid.NewGuid(), 1_500_000m, 500_000m, "Chot ca");
        var shiftResponse = new ShiftResponse(
            request.ShiftId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            1_000_000m,
            500_000m,
            0m,
            1_500_000m,
            request.ActualCashCounted,
            0m,
            request.RetainedFloatAmount,
            1_000_000m,
            "CLOSED",
            DateTime.UtcNow.AddHours(-8),
            DateTime.UtcNow);

        _mockCashbookService
            .Setup(s => s.CloseShiftZReportAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(shiftResponse);

        var actionResult = await _controller.CloseShift(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ShiftResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal("CLOSED", apiResponse.Data?.Status);
    }

    [Fact]
    public async Task CashDrop_ShouldReturnOk()
    {
        var request = new CashDropRequest(Guid.NewGuid(), 2_000_000m, Guid.NewGuid(), "Drop bot tien");
        _mockCashbookService
            .Setup(s => s.ProcessCashDropAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var actionResult = await _controller.CashDrop(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<bool>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.True(apiResponse.Data);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecordTaxPayment_ReturnsVoucherAndCashbookEntry(bool hasSnapshot)
    {
        Guid? snapshotId = hasSnapshot ? Guid.NewGuid() : null;
        var request = new RecordTaxPaymentRequest(Guid.NewGuid(), 5_000_000m, "Kho bạc", "TAX-01", null, snapshotId, null);
        var voucherId = Guid.NewGuid();
        var entry = new CashbookEntryResponse(Guid.NewGuid(), "CB-TAX", request.SourceAccountId, "Bank", null,
            "Payment", request.Amount, 1_000_000m, "TAX_PAYMENT", voucherId, "TAX-01", "HASH", "Nộp thuế", DateTime.UtcNow);
        var receipt = new TaxPaymentResponse(voucherId, snapshotId, entry);
        _mockCashbookService
            .Setup(s => s.RecordTaxPaymentAsync(request, CancellationToken.None))
            .ReturnsAsync(receipt);

        var actionResult = await _controller.RecordTaxPayment(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<ApiResponse<TaxPaymentResponse>>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal(receipt, response.Data);
    }

    [Fact]
    public async Task GetEntries_ShouldReturnOk_WithEntries()
    {
        var entries = new List<CashbookEntryResponse>
        {
            new(
                Guid.NewGuid(),
                "CB-001",
                Guid.NewGuid(),
                "Till",
                Guid.NewGuid(),
                "Receipt",
                100_000m,
                100_000m,
                "ORDER",
                Guid.NewGuid(),
                "ORD-001",
                "HASH123",
                "Ban hang",
                DateTime.UtcNow)
        };

        _mockCashbookService
            .Setup(s => s.GetEntriesPageAsync(null, null, null, null, 1, 50, null, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<CashbookEntryResponse> { Items = entries, TotalItems = 51 });

        var actionResult = await _controller.GetEntries(null, null, null, null, null, null, null, 1, 50, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<CashbookEntriesResponse>(okResult.Value);
        Assert.Equal(51, apiResponse.TotalItems);
        Assert.True(apiResponse.Success);
        Assert.Single(apiResponse.Data!);
    }

    [Fact]
    public async Task GetOrCreateTill_ShouldReturnOk()
    {
        var branchId = Guid.NewGuid();
        var till = new FinanceAccount
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            AccountName = "Két Thu Ngân #1",
            AccountType = FinanceAccountType.Till
        };

        _mockCashbookService
            .Setup(s => s.GetOrCreateTillAsync(branchId, "Két Thu Ngân #1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(till);

        var actionResult = await _controller.GetOrCreateTill(branchId, "Két Thu Ngân #1", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<Guid>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(till.Id, apiResponse.Data);
    }

    [Fact]
    public async Task GetAccounts_ShouldReturnOk()
    {
        var accounts = new List<CashAccountResponse>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "Branch 1", "Till 1", "Till", null, null, 1_000_000m, 1_000_000m, true, "VND", false)
        };

        _mockCashbookService
            .Setup(s => s.GetAccountsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accounts);

        var actionResult = await _controller.GetAccounts(null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<List<CashAccountResponse>>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Single(apiResponse.Data!);
    }

    [Fact]
    public async Task GetSummary_ShouldReturnOk()
    {
        var summary = new CashbookSummaryResponse(10_000_000m, 5_000_000m, 2_000_000m, 13_000_000m, 10_000_000m, 2_000_000m, 1_000_000m);
        _mockCashbookService
            .Setup(s => s.GetSummaryAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        var actionResult = await _controller.GetSummary(null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<CashbookSummaryResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(13_000_000m, apiResponse.Data?.TotalClosingBalance);
    }

    [Fact]
    public async Task CreateReceipt_ShouldReturnOk()
    {
        var request = new CreateReceiptRequest(Guid.NewGuid(), 200_000m, "MANUAL", "Thu tien khach", "REF-1");
        var response = new CashbookEntryResponse(
            Guid.NewGuid(),
            "CB-002",
            request.AccountId,
            "Till",
            null,
            "Receipt",
            200_000m,
            1_200_000m,
            "MANUAL",
            null,
            "REF-1",
            "HASH",
            request.Description ?? "",
            DateTime.UtcNow);

        _mockCashbookService
            .Setup(s => s.CreateReceiptAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var actionResult = await _controller.CreateReceipt(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<CashbookEntryResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal("CB-002", apiResponse.Data?.EntryCode);
    }

    [Fact]
    public async Task CreatePayment_ShouldReturnOk()
    {
        var request = new CreatePaymentRequest(Guid.NewGuid(), 50_000m, "EXPENSE", "Mua nuoc", "EXP-1");
        var response = new CashbookEntryResponse(
            Guid.NewGuid(),
            "CB-003",
            request.AccountId,
            "Till",
            null,
            "Payment",
            50_000m,
            950_000m,
            "EXPENSE",
            null,
            "EXP-1",
            "HASH",
            request.Description ?? "",
            DateTime.UtcNow);

        _mockCashbookService
            .Setup(s => s.CreatePaymentAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var actionResult = await _controller.CreatePayment(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<CashbookEntryResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal("CB-003", apiResponse.Data?.EntryCode);
    }

    [Fact]
    public async Task TransferFunds_ShouldReturnOk()
    {
        var request = new TransferFundRequest(Guid.NewGuid(), Guid.NewGuid(), 1_000_000m, "Chuyen tien");
        var payment = new CashbookEntryResponse(Guid.NewGuid(), "CB-OUT", request.FromAccountId, "Till", null, "Payment", 1_000_000m, 0m, "TRANSFER", null, "REF", "HASH", "Note", DateTime.UtcNow);
        var receipt = new CashbookEntryResponse(Guid.NewGuid(), "CB-IN", request.ToAccountId, "Safe", null, "Receipt", 1_000_000m, 1_000_000m, "TRANSFER", null, "REF", "HASH", "Note", DateTime.UtcNow);
        var response = new TransferFundResponse(payment, receipt);

        _mockCashbookService
            .Setup(s => s.TransferFundsAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var actionResult = await _controller.TransferFunds(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<TransferFundResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal("CB-OUT", apiResponse.Data?.Payment.EntryCode);
    }

    [Fact]
    public async Task VoidTransaction_ShouldReturnOk()
    {
        var entryId = Guid.NewGuid();
        var request = new VoidEntryRequest("Ghi nham");
        var response = new CashbookEntryResponse(
            Guid.NewGuid(),
            "CB-VOID",
            Guid.NewGuid(),
            "Till",
            null,
            "Payment",
            100_000m,
            900_000m,
            "VOID",
            entryId,
            entryId.ToString(),
            "HASH",
            "Huy giao dich",
            DateTime.UtcNow);

        _mockCashbookService
            .Setup(s => s.VoidTransactionAsync(entryId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var actionResult = await _controller.VoidTransaction(entryId, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<CashbookEntryResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal("CB-VOID", apiResponse.Data?.EntryCode);
    }

    [Fact]
    public async Task GetPendingTriage_ShouldReturnOk()
    {
        var vouchers = new List<ExpenseVoucherResponse>();
        _mockExpenseService
            .Setup(s => s.GetPendingTriageAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vouchers);

        var actionResult = await _controller.GetPendingTriage(null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<List<ExpenseVoucherResponse>>>(okResult.Value);
        Assert.True(apiResponse.Success);
    }

    [Fact]
    public async Task ResolveTriage_ShouldReturnOk()
    {
        var triageId = Guid.NewGuid();
        var request = new ResolveTriageRequest(FinancialNature.OperatingExpense, true, "Other", "Resolved");
        var voucher = new ExpenseVoucherResponse(
            Id: triageId,
            VoucherCode: "EXP-TRG",
            BranchId: Guid.NewGuid(),
            BeneficiaryName: "Beneficiary",
            Category: "Other",
            Amount: 100_000m,
            Status: "RESOLVED",
            PaymentSourceType: "Till",
            PaymentSourceAccountId: null,
            IsTaxDeductible: true,
            InvoiceNumber: null,
            SellerTaxCode: null,
            EvidenceAttachmentUrl: null,
            IsFlaggedAsSuspiciousSplit: false,
            SuspiciousReason: null,
            ApprovedByUserId: null,
            ApprovedAtUtc: null,
            PaidAtUtc: null,
            ExpenseDate: DateTime.UtcNow,
            Note: "Da xu ly");

        _mockExpenseService
            .Setup(s => s.ResolveTriageAsync(triageId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(voucher);

        var actionResult = await _controller.ResolveTriage(triageId, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ExpenseVoucherResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal("EXP-TRG", apiResponse.Data?.VoucherCode);
    }

    [Fact]
    public async Task VerifyChainIntegrity_ShouldReturnOk_WhenValidAndWhenInvalid()
    {
        var accountId = Guid.NewGuid();
        _mockCashbookService
            .Setup(s => s.VerifyChainIntegrityAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var actionResult = await _controller.VerifyChainIntegrity(accountId, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(okResult.Value);
        Assert.True(apiResponse.Success);

        // Test invalid chain
        _mockCashbookService
            .Setup(s => s.VerifyChainIntegrityAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var actionResult2 = await _controller.VerifyChainIntegrity(accountId, CancellationToken.None);
        var okResult2 = Assert.IsType<OkObjectResult>(actionResult2.Result);
        var apiResponse2 = Assert.IsType<ApiResponse<object>>(okResult2.Value);
        Assert.Contains("CẢNH BÁO", apiResponse2.Message);
    }
}
