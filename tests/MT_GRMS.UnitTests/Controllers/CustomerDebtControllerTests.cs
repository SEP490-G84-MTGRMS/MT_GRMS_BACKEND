using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Debt;
using MT_GRMS.Application.Debt.Dtos;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.UnitTests.Controllers;

public class CustomerDebtControllerTests
{
    private readonly Mock<ICustomerDebtService> _mockDebtService = new(MockBehavior.Strict);
    private readonly CustomerDebtController _controller;

    public CustomerDebtControllerTests()
    {
        _controller = new CustomerDebtController(_mockDebtService.Object);
    }

    [Theory]
    [InlineData(FinanceAccountType.Till, 100_000)]
    [InlineData(FinanceAccountType.Bank, 0)]
    public async Task RepayDebt_ReturnsLedgerEntryAndRemainingBalance(FinanceAccountType accountType, int remainingDebt)
    {
        using var cancellation = new CancellationTokenSource();
        var request = new RepayDebtRequest(Guid.NewGuid(), 200_000m, accountType, Guid.NewGuid(), "REC-01", null);
        var cashbookEntryId = Guid.NewGuid();
        var receipt = new RepaymentResultResponse(cashbookEntryId, "DE-01", request.Amount,
            request.Amount + remainingDebt, remainingDebt, cashbookEntryId);
        _mockDebtService.Setup(service => service.RepayDebtAsync(request, cancellation.Token)).ReturnsAsync(receipt);

        var action = await _controller.RepayDebt(request, cancellation.Token);

        var response = Assert.IsType<ApiResponse<RepaymentResultResponse>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.True(response.Success);
        Assert.Equal(receipt, response.Data);
    }

    [Theory]
    [InlineData("INVALID_AMOUNT")]
    [InlineData("REPAYMENT_EXCEEDS_DEBT")]
    public async Task RepayDebt_PropagatesBusinessFailure(string errorCode)
    {
        var request = new RepayDebtRequest(Guid.NewGuid(), 200_000m, FinanceAccountType.Till, Guid.NewGuid(), null, null);
        var failure = new BusinessException(errorCode, "Thu nợ không hợp lệ.");
        _mockDebtService.Setup(service => service.RepayDebtAsync(request, CancellationToken.None)).ThrowsAsync(failure);

        var exception = await Assert.ThrowsAsync<BusinessException>(() => _controller.RepayDebt(request, CancellationToken.None));

        Assert.Equal(errorCode, exception.ErrorCode);
    }

    [Fact]
    public async Task GetProfile_ReturnsBalanceAndActivityTimestamps()
    {
        var customerId = Guid.NewGuid();
        var lastRepayment = new DateTime(2026, 11, 20, 3, 0, 0, DateTimeKind.Utc);
        var profile = new CustomerDebtProfileResponse(customerId, 150_000m, lastRepayment.AddDays(-1), lastRepayment);
        _mockDebtService.Setup(service => service.GetProfileAsync(customerId, CancellationToken.None)).ReturnsAsync(profile);

        var action = await _controller.GetProfile(customerId, CancellationToken.None);

        var response = Assert.IsType<ApiResponse<CustomerDebtProfileResponse>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.True(response.Success);
        Assert.Equal(profile, response.Data);
    }

    [Fact]
    public async Task GetLedger_ForwardsRequestedPageAndReturnsReceiptDetails()
    {
        var customerId = Guid.NewGuid();
        var cashbookEntryId = Guid.NewGuid();
        var receipt = new CustomerDebtLedgerItemResponse(cashbookEntryId, "DE-01", customerId, "DebtRepayment",
            1_000m, null, null, "REC-01", "Thu nợ", DateTime.UtcNow, Guid.NewGuid(), "Till", cashbookEntryId);
        _mockDebtService.Setup(service => service.GetLedgerAsync(customerId, 2, 10, CancellationToken.None))
            .ReturnsAsync([receipt]);

        var action = await _controller.GetLedger(customerId, 2, 10);

        var response = Assert.IsType<ApiResponse<List<CustomerDebtLedgerItemResponse>>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.True(response.Success);
        Assert.Equal(receipt, Assert.Single(response.Data!));
    }

    [Fact]
    public async Task GetLedger_UsesDefaultPageAndReturnsEmptyList()
    {
        var customerId = Guid.NewGuid();
        _mockDebtService.Setup(service => service.GetLedgerAsync(customerId, 1, 50, CancellationToken.None))
            .ReturnsAsync([]);

        var action = await _controller.GetLedger(customerId);

        var response = Assert.IsType<ApiResponse<List<CustomerDebtLedgerItemResponse>>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.True(response.Success);
        Assert.Empty(response.Data!);
    }
}
