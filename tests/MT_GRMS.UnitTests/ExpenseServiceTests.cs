using System.Linq.Expressions;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Cashbook.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Expenses;
using MT_GRMS.Application.Expenses.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class ExpenseServiceTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-test-01";
        public Guid CurrentUserId { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public Guid? CurrentBranchId { get; set; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }
        public string CurrentRole { get; set; } = "ADMIN";
        public void SetRole(string? role) => CurrentRole = role?.Trim() ?? string.Empty;
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCount { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            return Task.FromResult(1);
        }
    }

    private class FakeExpenseRepository : IExpenseRepository
    {
        public List<ExpenseVoucher> Items { get; } = new();
        public decimal ShiftExpenseSum { get; set; }
        public decimal DailyExpenseSum { get; set; }
        public int RecentVouchersCount { get; set; }
        public decimal SameVendorDailySum { get; set; }

        public Task<ExpenseVoucher?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(x => x.Id == id));

        public Task<IReadOnlyList<ExpenseVoucher>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult((IReadOnlyList<ExpenseVoucher>)Items.ToList());

        public Task<IReadOnlyList<ExpenseVoucher>> FindAsync(Expression<Func<ExpenseVoucher, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult((IReadOnlyList<ExpenseVoucher>)Items.AsQueryable().Where(predicate).ToList());

        public Task AddAsync(ExpenseVoucher entity, CancellationToken cancellationToken = default)
        {
            if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
            Items.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(ExpenseVoucher entity) { }
        public void Delete(ExpenseVoucher entity) => Items.Remove(entity);

        public Task<decimal> GetShiftExpenseSumAsync(Guid branchId, DateTime shiftStartUtc, CancellationToken ct = default) =>
            Task.FromResult(ShiftExpenseSum);

        public Task<decimal> GetDailyExpenseSumAsync(Guid branchId, DateTime dateUtc, CancellationToken ct = default) =>
            Task.FromResult(DailyExpenseSum);

        public Task<int> GetRecentVouchersCountAsync(Guid branchId, Guid createdByUserId, DateTime sinceUtc, decimal minAmount, CancellationToken ct = default) =>
            Task.FromResult(RecentVouchersCount);

        public Task<decimal> GetSameVendorDailySumAsync(Guid branchId, string beneficiaryName, string category, DateTime dateUtc, CancellationToken ct = default) =>
            Task.FromResult(SameVendorDailySum);
    }

    private class FakeCashbookService : ICashbookService
    {
        public bool ShouldThrowInsufficientBalance { get; set; }
        public decimal DisbursedAmount { get; private set; }

        public Task<CashbookEntryResponse> DisburseCashAsync(Guid accountId, FinanceAccountType accountType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct = default)
        {
            if (ShouldThrowInsufficientBalance)
            {
                throw new InsufficientStockException("Tài khoản không đủ số dư để chi.");
            }

            DisbursedAmount += amount;
            return Task.FromResult(new CashbookEntryResponse(Guid.NewGuid(), "CB-01", accountId, accountType.ToString(), null, "Payment", amount, 100_000m, sourceType, sourceId, referenceCode, "HASH-01", description, DateTime.UtcNow));
        }

        public Task<ShiftResponse> OpenShiftAsync(OpenShiftRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ShiftResponse> CloseShiftZReportAsync(CloseShiftRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ProcessCashDropAsync(CashDropRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CashInTransitResponse> CreateDepositToBankAsync(DepositToBankRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CashInTransitResponse> ConfirmBankDepositAsync(ConfirmBankDepositRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CashbookEntryResponse> ReceiveCashAsync(Guid accountId, FinanceAccountType accountType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<CashbookEntryResponse>> GetEntriesAsync(Guid? accountId, Guid? branchId, DateTime? fromDate, DateTime? toDate, int page = 1, int pageSize = 50, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<BranchCashTill> GetOrCreateTillAsync(Guid branchId, string tillName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<BranchSafe> GetOrCreateSafeAsync(Guid branchId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private readonly FakeExpenseRepository _expenseRepo = new();
    private readonly FakeCashbookService _cashbookService = new();
    private readonly FakeTenantContext _tenantContext = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly ExpenseService _service;

    public ExpenseServiceTests()
    {
        _service = new ExpenseService(
            _expenseRepo,
            _cashbookService,
            _tenantContext,
            _unitOfWork
        );
    }

    [Fact]
    public async Task CreateExpenseAsync_WhenAmountUnderT1Threshold_ShouldAutoApprove()
    {
        var branchId = Guid.NewGuid();
        var request = new CreateExpenseRequest(
            branchId,
            "Tiệm tạp hóa Cô Ba",
            "Văn phòng phẩm",
            450_000m,
            FinanceAccountType.Till,
            Guid.NewGuid(),
            true,
            "HD001",
            "0101234567",
            null,
            "Mua hóa đơn in nhiệt"
        );

        var response = await _service.CreateExpenseAsync(request);

        Assert.NotNull(response);
        Assert.Equal(ExpenseVoucherStatus.Approved.ToString(), response.Status);
        Assert.False(response.IsFlaggedAsSuspiciousSplit);
        Assert.NotNull(response.ApprovedAtUtc);
    }

    [Fact]
    public async Task CreateExpenseAsync_WhenAmountOverT1Threshold_ShouldBePendingApproval()
    {
        var branchId = Guid.NewGuid();
        var request = new CreateExpenseRequest(
            branchId,
            "Công ty Sửa chữa Điện",
            "Bảo trì thiết bị",
            500_001m,
            FinanceAccountType.Till,
            Guid.NewGuid(),
            true,
            "HD002",
            "0109876543",
            null,
            "Sửa tủ mát trưng bày"
        );

        var response = await _service.CreateExpenseAsync(request);

        Assert.Equal(ExpenseVoucherStatus.PendingApproval.ToString(), response.Status);
        Assert.Null(response.ApprovedAtUtc);
    }

    [Fact]
    public async Task CreateExpenseAsync_WhenPaymentSourceIsBank_ShouldBePendingApprovalRegardlessOfAmount()
    {
        var branchId = Guid.NewGuid();
        var request = new CreateExpenseRequest(
            branchId,
            "VNPT Internet",
            "Tiện ích viễn thông",
            200_000m,
            FinanceAccountType.Bank,
            Guid.NewGuid(),
            true,
            "INV-NET",
            "0100109106",
            null,
            "Cước mạng tháng này"
        );

        var response = await _service.CreateExpenseAsync(request);

        Assert.Equal(ExpenseVoucherStatus.PendingApproval.ToString(), response.Status);
    }

    [Fact]
    public async Task CreateExpenseAsync_WhenSplitAcrossSameVendorExceedsT1_ShouldRequireApproval()
    {
        var branchId = Guid.NewGuid();
        _expenseRepo.SameVendorDailySum = 300_000m;

        var request = new CreateExpenseRequest(
            branchId,
            "Đại lý Nước đá",
            "Vật tư vận hành",
            250_000m,
            FinanceAccountType.Till,
            Guid.NewGuid(),
            false,
            null,
            null,
            null,
            "Mua thêm đá viên lần 2"
        );

        var response = await _service.CreateExpenseAsync(request);

        Assert.Equal(ExpenseVoucherStatus.PendingApproval.ToString(), response.Status);
        Assert.Contains("vượt ngưỡng tự duyệt", response.SuspiciousReason);
    }

    [Fact]
    public async Task CreateExpenseAsync_WhenFrequencyPatternDetected_ShouldFlagSuspiciousSplit()
    {
        var branchId = Guid.NewGuid();
        _expenseRepo.RecentVouchersCount = 2;

        var request = new CreateExpenseRequest(
            branchId,
            "Cửa hàng Đồ gỗ",
            "Trang trí cửa hàng",
            350_000m,
            FinanceAccountType.Till,
            Guid.NewGuid(),
            false,
            null,
            null,
            null,
            "Mua kệ gỗ đợt 3"
        );

        var response = await _service.CreateExpenseAsync(request);

        Assert.True(response.IsFlaggedAsSuspiciousSplit);
        Assert.Equal(ExpenseVoucherStatus.PendingApproval.ToString(), response.Status);
        Assert.Contains("mẫu hình tạo nhiều phiếu chi", response.SuspiciousReason);
    }

    [Fact]
    public async Task CreateExpenseAsync_WhenDailyCumulativeLimitExceeded_ShouldRequireApproval()
    {
        var branchId = Guid.NewGuid();
        _expenseRepo.DailyExpenseSum = 19_800_000m;

        var request = new CreateExpenseRequest(
            branchId,
            "Đơn vị vệ sinh",
            "Dịch vụ vệ sinh",
            300_000m,
            FinanceAccountType.Till,
            Guid.NewGuid(),
            false,
            null,
            null,
            null,
            "Dọn dẹp kho"
        );

        var response = await _service.CreateExpenseAsync(request);

        Assert.Equal(ExpenseVoucherStatus.PendingApproval.ToString(), response.Status);
    }

    [Fact]
    public async Task ApproveExpenseAsync_WhenPendingApproval_ShouldChangeStatusToApproved()
    {
        var voucher = new ExpenseVoucher
        {
            Id = Guid.NewGuid(),
            Amount = 1_000_000m,
            Status = ExpenseVoucherStatus.PendingApproval
        };
        _expenseRepo.Items.Add(voucher);

        var request = new ApproveExpenseRequest(voucher.Id, "Đồng ý chi mua đồ bảo hộ");
        var response = await _service.ApproveExpenseAsync(request);

        Assert.Equal(ExpenseVoucherStatus.Approved.ToString(), response.Status);
        Assert.NotNull(response.ApprovedAtUtc);
        Assert.NotNull(response.ApprovedByUserId);
    }

    [Fact]
    public async Task ApproveExpenseAsync_WhenAlreadyApproved_ShouldThrowBusinessException()
    {
        var voucher = new ExpenseVoucher
        {
            Id = Guid.NewGuid(),
            Amount = 1_000_000m,
            Status = ExpenseVoucherStatus.Approved
        };
        _expenseRepo.Items.Add(voucher);

        var request = new ApproveExpenseRequest(voucher.Id, "Duyệt lại");
        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.ApproveExpenseAsync(request));
        Assert.Equal("INVALID_STATUS", ex.ErrorCode);
    }

    [Fact]
    public async Task DisburseExpenseAsync_WhenInsufficientCash_ShouldMarkPaymentFailed()
    {
        var voucher = new ExpenseVoucher
        {
            Id = Guid.NewGuid(),
            Amount = 2_000_000m,
            Status = ExpenseVoucherStatus.Approved
        };
        _expenseRepo.Items.Add(voucher);

        _cashbookService.ShouldThrowInsufficientBalance = true;

        var request = new DisburseExpenseRequest(voucher.Id, FinanceAccountType.Till, Guid.NewGuid(), null);
        await Assert.ThrowsAsync<InsufficientStockException>(() => _service.DisburseExpenseAsync(request));

        Assert.Equal(ExpenseVoucherStatus.PaymentFailed, voucher.Status);
    }

    [Fact]
    public async Task DisburseExpenseAsync_WhenSufficientCash_ShouldChangeStatusToPaidAndDeductCash()
    {
        var voucher = new ExpenseVoucher
        {
            Id = Guid.NewGuid(),
            VoucherCode = "EXP-999",
            Category = "Tiếp khách",
            BeneficiaryName = "Quán cơm",
            Amount = 400_000m,
            Status = ExpenseVoucherStatus.Approved
        };
        _expenseRepo.Items.Add(voucher);

        _cashbookService.ShouldThrowInsufficientBalance = false;

        var request = new DisburseExpenseRequest(voucher.Id, FinanceAccountType.Till, Guid.NewGuid(), null);
        var response = await _service.DisburseExpenseAsync(request);

        Assert.Equal(ExpenseVoucherStatus.Paid.ToString(), response.Status);
        Assert.NotNull(response.PaidAtUtc);
        Assert.Equal(400_000m, _cashbookService.DisbursedAmount);
    }

    [Fact]
    public async Task GetExpenseByIdAndList_ShouldWorkCorrectly()
    {
        var branchId = Guid.NewGuid();
        var voucher = new ExpenseVoucher
        {
            Id = Guid.NewGuid(),
            VoucherCode = "EXP-777",
            BranchId = branchId,
            Amount = 100_000m,
            Status = ExpenseVoucherStatus.Approved,
            CreatedAt = DateTime.UtcNow
        };
        _expenseRepo.Items.Add(voucher);

        var byId = await _service.GetExpenseByIdAsync(voucher.Id);
        Assert.NotNull(byId);
        Assert.Equal("EXP-777", byId.VoucherCode);

        var list = await _service.GetExpensesAsync(branchId, null, null, 1, 10);
        Assert.Single(list);
    }

    [Fact]
    public async Task GetExpenseSummaryAsync_ShouldCalculateKPIsAccurately()
    {
        var branchId = Guid.NewGuid();
        _expenseRepo.Items.Add(new ExpenseVoucher
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            Amount = 1_000_000m,
            Status = ExpenseVoucherStatus.Paid,
            IsTaxDeductible = true,
            CreatedAt = DateTime.UtcNow
        });
        _expenseRepo.Items.Add(new ExpenseVoucher
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            Amount = 600_000m,
            Status = ExpenseVoucherStatus.PendingApproval,
            IsTaxDeductible = false,
            IsFlaggedAsSuspiciousSplit = true,
            CreatedAt = DateTime.UtcNow
        });

        var summary = await _service.GetExpenseSummaryAsync(branchId, null, null);

        Assert.Equal(1_600_000m, summary.TotalAmount);
        Assert.Equal(2, summary.TotalCount);
        Assert.Equal(1_000_000m, summary.PaidAmount);
        Assert.Equal(1, summary.PaidCount);
        Assert.Equal(600_000m, summary.PendingApprovalAmount);
        Assert.Equal(1, summary.PendingApprovalCount);
        Assert.Equal(600_000m, summary.SuspiciousAmount);
        Assert.Equal(1, summary.SuspiciousCount);
        Assert.Equal(1_000_000m, summary.TaxDeductibleAmount);
        Assert.Equal(1, summary.TaxDeductibleCount);
        Assert.Equal(600_000m, summary.NonTaxDeductibleAmount);
        Assert.Equal(1, summary.NonTaxDeductibleCount);
    }

    [Fact]
    public async Task ApproveExpenseAsync_WhenFlaggedAsSuspiciousAndNoteIsEmpty_ShouldThrowBusinessException()
    {
        var voucher = new ExpenseVoucher
        {
            Id = Guid.NewGuid(),
            Amount = 450_000m,
            Status = ExpenseVoucherStatus.PendingApproval,
            IsFlaggedAsSuspiciousSplit = true,
            SuspiciousReason = "Cảnh báo xé nhỏ chi phí cùng vendor trong ngày"
        };
        _expenseRepo.Items.Add(voucher);

        // Duyệt nhưng để trống ghi chú -> Ném lỗi NOTE_REQUIRED_FOR_SUSPICIOUS_EXPENSE
        var request = new ApproveExpenseRequest(voucher.Id, "");
        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.ApproveExpenseAsync(request));

        Assert.Equal("NOTE_REQUIRED_FOR_SUSPICIOUS_EXPENSE", ex.ErrorCode);
    }

    [Fact]
    public async Task ApproveExpenseAsync_WhenFlaggedAsSuspiciousAndNoteProvided_ShouldApproveSuccessfully()
    {
        var voucher = new ExpenseVoucher
        {
            Id = Guid.NewGuid(),
            Amount = 450_000m,
            Status = ExpenseVoucherStatus.PendingApproval,
            IsFlaggedAsSuspiciousSplit = true,
            SuspiciousReason = "Cảnh báo xé nhỏ chi phí cùng vendor trong ngày"
        };
        _expenseRepo.Items.Add(voucher);

        // Chủ hộ giải trình rõ lý do -> Phê duyệt thành công
        var request = new ApproveExpenseRequest(voucher.Id, "Đã kiểm tra hóa đơn đỏ đính kèm, mua gấp do hết vật tư");
        var response = await _service.ApproveExpenseAsync(request);

        Assert.Equal(ExpenseVoucherStatus.Approved.ToString(), response.Status);
        Assert.Equal("Đã kiểm tra hóa đơn đỏ đính kèm, mua gấp do hết vật tư", response.Note);
    }

    [Fact]
    public async Task ApproveExpenseAsync_WhenUserHasUnauthorizedRole_ShouldThrowUnauthorizedAccessException()
    {
        _tenantContext.SetRole("Cashier");
        var voucher = new ExpenseVoucher
        {
            Id = Guid.NewGuid(),
            Amount = 100_000m,
            Status = ExpenseVoucherStatus.PendingApproval
        };
        _expenseRepo.Items.Add(voucher);

        var request = new ApproveExpenseRequest(voucher.Id, "Duyệt chi");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.ApproveExpenseAsync(request));
    }
}
