using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Domain.Entities.Sales;
using Xunit;

namespace MT_GRMS.UnitTests;

public class DefaultInterfaceMethodsTests
{
    private class DummyEmailService : IEmailService
    {
        public string? LastToEmail { get; private set; }
        public string? LastOtp { get; private set; }

        public Task SendPasswordResetOtpAsync(string toEmail, string fullName, string otpCode, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> SendLoginOtpAsync(string toEmail, string fullName, string otpCode, CancellationToken cancellationToken = default)
        {
            LastToEmail = toEmail;
            LastOtp = otpCode;
            return Task.FromResult(true);
        }

        public Task<bool> SendRegistrationOtpAsync(string toEmail, string fullName, string storeName, string otpCode, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private class DummyPromotionRepository : IPromotionRepository
    {
        public Task AddAsync(Promotion entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Promotion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Promotion?>(null);
        public Task<IReadOnlyList<Promotion>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Promotion>>(new List<Promotion>());
        public Task<IReadOnlyList<Promotion>> FindAsync(Expression<Func<Promotion, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Promotion>>(new List<Promotion>());
        public void Update(Promotion entity) { }
        public void Delete(Promotion entity) { }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task<Promotion?> GetByIdWithConditionsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Promotion?>(null);
        public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<(IReadOnlyList<Promotion> Items, int TotalCount)> GetPagedListAsync(PromotionFilterQuery query, CancellationToken cancellationToken = default) => Task.FromResult(((IReadOnlyList<Promotion>)new List<Promotion>(), 0));
        public Task<IReadOnlyList<Promotion>> GetActivePromotionsAsync(DateTime atUtc, string? branchId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Promotion>>(new List<Promotion>());
        public Task ReplaceConditionsAsync(Guid promotionId, IEnumerable<PromotionCondition> conditions, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<(IReadOnlyList<Promotion> Batches, int TotalCount)> GetPagedVoucherBatchesAsync(int pageIndex, int pageSize, string? searchTerm = null, string? status = null, CancellationToken cancellationToken = default) => Task.FromResult(((IReadOnlyList<Promotion>)new List<Promotion>(), 0));
        public Task AddVoucherCodesAsync(IEnumerable<VoucherCode> codes, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> VoucherCodeExistsAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<VoucherCode?> GetVoucherCodeAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult<VoucherCode?>(null);
        public Task<(IReadOnlyList<VoucherCode> Codes, int TotalCount)> GetPagedVoucherCodesAsync(Guid promotionId, int pageIndex, int pageSize, string? status = null, CancellationToken cancellationToken = default) => Task.FromResult(((IReadOnlyList<VoucherCode>)new List<VoucherCode>(), 0));
        public Task<bool> RedeemVoucherCodeAtomicAsync(string code, Guid orderId, string orderCode, Guid? customerId, string? customerName, string? customerPhone, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<LoyaltySetting?> GetLoyaltySettingAsync(string tenantId, CancellationToken cancellationToken = default) => Task.FromResult<LoyaltySetting?>(null);
        public Task SaveLoyaltySettingAsync(LoyaltySetting setting, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task EmailService_DefaultTwoFactorMethod_CallsLoginOtp()
    {
        IEmailService service = new DummyEmailService();
        var result = await service.SendTwoFactorSettingsOtpAsync("user@test.com", "Test User", "123456");
        Assert.True(result);
    }

    [Fact]
    public async Task PromotionRepository_DefaultInterfaceMethods_ExecuteCorrectly()
    {
        IPromotionRepository repo = new DummyPromotionRepository();
        await repo.RecordPromotionUsageAsync(Guid.NewGuid(), 50000m, "VOUCHER10", 10000m, "tenant-1");
        await repo.RestoreOrderPromotionsOnCancelAsync(Guid.NewGuid(), 50000m, "VOUCHER10", 10000m, "tenant-1");
        var prices = await repo.GetProductSellingPricesAsync(new[] { Guid.NewGuid(), Guid.NewGuid() });
        Assert.NotNull(prices);
        Assert.Empty(prices);
    }

}
