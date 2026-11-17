using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Cashbook.Dtos;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Enums;
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

    private class DummyCashbookService : ICashbookService
    {
        public Task<ShiftResponse> OpenShiftAsync(OpenShiftRequest request, CancellationToken ct = default)
            => Task.FromResult(new ShiftResponse(Guid.NewGuid(), request.TillId, Guid.NewGuid(), request.CountedFloat, 0, 0, request.CountedFloat, null, null, request.CountedFloat, 0, "OPEN", DateTime.UtcNow, null));

        public Task<ShiftResponse> CloseShiftZReportAsync(CloseShiftRequest request, CancellationToken ct = default)
            => Task.FromResult(new ShiftResponse(request.ShiftId, Guid.NewGuid(), Guid.NewGuid(), 0, 0, 0, 0, request.ActualCashCounted, 0, request.RetainedFloatAmount, 0, "CLOSED", DateTime.UtcNow, DateTime.UtcNow));

        public Task<bool> ProcessCashDropAsync(CashDropRequest request, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<CashInTransitResponse> CreateDepositToBankAsync(DepositToBankRequest request, CancellationToken ct = default)
            => Task.FromResult(new CashInTransitResponse(Guid.NewGuid(), "CIT", request.SourceAccountId, request.TargetBankAccountId, request.Amount, null, 0, request.CarrierUserId, "INITIATED", DateTime.UtcNow));

        public Task<CashInTransitResponse> ConfirmBankDepositAsync(ConfirmBankDepositRequest request, CancellationToken ct = default)
            => Task.FromResult(new CashInTransitResponse(request.TransitId, "CIT", Guid.NewGuid(), Guid.NewGuid(), request.ActualReceivedAmount, request.ActualReceivedAmount, 0, Guid.NewGuid(), "CONFIRMED", DateTime.UtcNow));

        public Task<CashbookEntryResponse> DisburseCashAsync(Guid accountId, FinanceAccountType accountType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct = default)
            => Task.FromResult(new CashbookEntryResponse(Guid.NewGuid(), "DISB", accountId, "Till", null, "Payment", amount, 0, sourceType, sourceId, referenceCode, "", description, DateTime.UtcNow));

        public Task<CashbookEntryResponse> ReceiveCashAsync(Guid accountId, FinanceAccountType accountType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct = default)
            => Task.FromResult(new CashbookEntryResponse(Guid.NewGuid(), "REC", accountId, "Till", null, "Receipt", amount, amount, sourceType, sourceId, referenceCode, "", description, DateTime.UtcNow));

        public Task<List<CashbookEntryResponse>> GetEntriesAsync(Guid? accountId, Guid? branchId, DateTime? fromDate, DateTime? toDate, int page = 1, int pageSize = 50, CashbookEntryType? entryType = null, string? sourceType = null, string? keyword = null, CancellationToken ct = default)
            => Task.FromResult(new List<CashbookEntryResponse>());

        public Task<FinanceAccount> GetOrCreateTillAsync(Guid branchId, string tillName, CancellationToken ct = default)
            => Task.FromResult(new FinanceAccount { Id = Guid.NewGuid(), BranchId = branchId, AccountName = tillName });

        public Task<FinanceAccount> GetOrCreateSafeAsync(Guid branchId, CancellationToken ct = default)
            => Task.FromResult(new FinanceAccount { Id = Guid.NewGuid(), BranchId = branchId, AccountName = "Safe" });
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

    [Theory]
    [InlineData("BANK", true)]
    [InlineData("BANK_TRANSFER", true)]
    [InlineData("VIETQR", true)]
    [InlineData("QR", true)]
    [InlineData("CASH", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public async Task CashbookService_ResolveDisbursementAccountAsync_HandlesAllBranches(string? method, bool expectBank)
    {
        ICashbookService service = new DummyCashbookService();
        var branchId = Guid.NewGuid();
        var acc = await service.ResolveDisbursementAccountAsync(branchId, method!);
        Assert.NotNull(acc);
        if (expectBank)
        {
            Assert.Equal(FinanceAccountType.Bank, acc.AccountType);
        }
        else
        {
            Assert.Equal("Safe", acc.AccountName);
        }
    }

    [Fact]
    public async Task CashbookService_OtherDefaultMethods_ExecuteSuccessfully()
    {
        ICashbookService service = new DummyCashbookService();
        var branchId = Guid.NewGuid();

        var bankCustom = await service.GetOrCreateBankAccountAsync(branchId, "Vietcombank", "1234567890", "Nguyen Van A");
        Assert.Equal("Vietcombank", bankCustom.BankName);
        Assert.Equal("1234567890", bankCustom.AccountNumber);

        var bankDefault = await service.GetOrCreateBankAccountAsync();
        Assert.NotEmpty(bankDefault.BankName!);

        var accounts = await service.GetAccountsAsync(branchId);
        Assert.Empty(accounts);

        var summary = await service.GetSummaryAsync(branchId);
        Assert.Equal(0, summary.TotalOpeningBalance);

        var receipt = await service.CreateReceiptAsync(new CreateReceiptRequest(Guid.NewGuid(), 100000m, "THU", "Mo ta", "REF-01"));
        Assert.Equal("CB-REC", receipt.EntryCode);

        var payment = await service.CreatePaymentAsync(new CreatePaymentRequest(Guid.NewGuid(), 50000m, "CHI", "Chi", "REF-02"));
        Assert.Equal("CB-PAY", payment.EntryCode);

        var transfer = await service.TransferFundsAsync(new TransferFundRequest(Guid.NewGuid(), Guid.NewGuid(), 20000m, "Chuyen"));
        Assert.Equal("CB-TRF-OUT", transfer.Payment.EntryCode);
        Assert.Equal("CB-TRF-IN", transfer.Receipt.EntryCode);

        var reconcile = await service.ReconcileCashAccountAsync(new ReconcileAccountRequest(Guid.NewGuid(), 100000m, "Giai trinh"));
        Assert.Null(reconcile);

        var voidTx = await service.VoidTransactionAsync(Guid.NewGuid(), new VoidEntryRequest("Sai sot"));
        Assert.Equal("CB-VOID", voidTx.EntryCode);

        var verified = await service.VerifyChainIntegrityAsync(Guid.NewGuid());
        Assert.True(verified);
    }
}
