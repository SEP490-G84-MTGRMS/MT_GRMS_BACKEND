using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Application.Promotions.Validators;
using Xunit;

namespace MT_GRMS.UnitTests;

public class PromotionInputValidationTests
{
    [Theory]
    [InlineData("", 1000, null, false)]
    [InlineData("CODE", -1, null, false)]
    [InlineData("CODE", 1000, "not-a-guid", false)]
    [InlineData("CODE", 0, "11111111-1111-1111-1111-111111111111", true)]
    public async Task ValidateVoucherRequest_CoversRequiredAmountAndCustomerId(
        string code, decimal cartTotal, string? customerId, bool expectedValid)
    {
        var validator = new ValidateVoucherRequestValidator();
        var result = await validator.ValidateAsync(new ValidateVoucherRequest(code, cartTotal, customerId));
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData("BAD CODE", "SALE")]
    [InlineData("BATCH", "BAD PREFIX")]
    public async Task VoucherBatchCreate_RejectsUnsafeCodeCharacters(string batchCode, string prefix)
    {
        var request = new VoucherBatchCreateRequest(batchCode, "Đợt voucher", "PERCENT", 10, 0, 100000, 10,
            DateTime.UtcNow, DateTime.UtcNow.AddDays(1), prefix);
        var result = await new VoucherBatchCreateRequestValidator().ValidateAsync(request);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task PromotionCreate_RejectsUnsafePromotionCode()
    {
        var request = new PromotionCreateRequest("BAD CODE", "Khuyến mại", "ORDER_DISCOUNT", 10, 0, null, null,
            DateTime.UtcNow, DateTime.UtcNow.AddDays(1));
        var result = await new PromotionCreateRequestValidator().ValidateAsync(request);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task PromotionCreate_RejectsPastStartDate()
    {
        var request = new PromotionCreateRequest("VALID-CODE", "Khuyến mại", "ORDER_DISCOUNT", 10, 0, null, null,
            DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(1));
        var result = await new PromotionCreateRequestValidator().ValidateAsync(request);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(request.StartDate));
    }

    [Fact]
    public async Task VoucherBatchCreate_RejectsPastStartDate()
    {
        var request = new VoucherBatchCreateRequest("BATCH", "Đợt voucher", "PERCENT", 10, 0, 100000, 10,
            DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(1), "SALE");
        var result = await new VoucherBatchCreateRequestValidator().ValidateAsync(request);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(request.StartDate));
    }
}
