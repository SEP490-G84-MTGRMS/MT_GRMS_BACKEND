using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Expenses;
using MT_GRMS.Application.Expenses.Dtos;
using MT_GRMS.Application.Tax;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class ExpenseServiceTaxLockTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-test-01";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; } = Guid.NewGuid();
        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }
        public string CurrentRole { get; set; } = "STORE_OWNER";
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

        public Task<decimal> GetShiftExpenseSumAsync(Guid branchId, DateTime shiftDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(0m);

        public Task<decimal> GetDailyExpenseSumAsync(Guid branchId, DateTime date, CancellationToken cancellationToken = default) =>
            Task.FromResult(0m);

        public Task<int> GetRecentVouchersCountAsync(Guid branchId, Guid userId, DateTime sinceUtc, decimal minAmount, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<decimal> GetSameVendorDailySumAsync(Guid branchId, string beneficiaryName, string category, DateTime date, CancellationToken cancellationToken = default) =>
            Task.FromResult(0m);
    }

    private readonly FakeExpenseRepository _expenseRepo = new();
    private readonly Mock<ICashbookService> _cashbookServiceMock = new();
    private readonly FakeTenantContext _tenantContext = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly Mock<ITaxPeriodLockGuard> _taxLockGuardMock = new();

    private readonly ExpenseService _service;

    public ExpenseServiceTaxLockTests()
    {
        _service = new ExpenseService(
            _expenseRepo,
            _cashbookServiceMock.Object,
            _tenantContext,
            _unitOfWork,
            _taxLockGuardMock.Object
        );
    }

    [Fact]
    public async Task CreateExpenseAsync_WhenPeriodFinalized_ThrowsTaxPeriodFinalized()
    {
        // Arrange
        var finalizedDate = new DateTime(2026, 3, 15, 10, 0, 0, DateTimeKind.Utc);
        _taxLockGuardMock.Setup(g => g.ValidateTaxPeriodUnlockedAsync(
                finalizedDate,
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("TAX_PERIOD_FINALIZED", "Kỳ tính thuế Tháng 3/2026 đã được chốt sổ bất biến. Không thể tạo phiếu chi."));

        var request = new CreateExpenseRequest(
            BranchId: Guid.NewGuid(),
            BeneficiaryName: "Nhà xe Vận Tải",
            Category: "Vận chuyển",
            Amount: 300_000m,
            PaymentSourceType: FinanceAccountType.Till,
            PaymentSourceAccountId: Guid.NewGuid(),
            IsTaxDeductible: true,
            InvoiceNumber: null,
            SellerTaxCode: null,
            EvidenceAttachmentUrl: null,
            Note: "Cước giao hàng",
            ExpenseDate: finalizedDate
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CreateExpenseAsync(request));

        Assert.Equal("TAX_PERIOD_FINALIZED", ex.ErrorCode);
    }

    [Fact]
    public async Task ApproveExpenseAsync_WhenPeriodFinalized_ThrowsTaxPeriodFinalized()
    {
        // Arrange
        var finalizedDate = new DateTime(2026, 3, 10, 8, 0, 0, DateTimeKind.Utc);
        var voucher = new ExpenseVoucher
        {
            Id = Guid.NewGuid(),
            VoucherCode = "EXP-20260310-01",
            ExpenseDate = finalizedDate,
            Status = ExpenseVoucherStatus.PendingApproval,
            Amount = 1_000_000m
        };
        _expenseRepo.Items.Add(voucher);

        _taxLockGuardMock.Setup(g => g.ValidateTaxPeriodUnlockedAsync(
                finalizedDate,
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("TAX_PERIOD_FINALIZED", "Kỳ tính thuế Tháng 3/2026 đã được chốt sổ bất biến."));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ApproveExpenseAsync(new ApproveExpenseRequest(voucher.Id, "Duyệt chi")));

        Assert.Equal("TAX_PERIOD_FINALIZED", ex.ErrorCode);
    }

    [Fact]
    public async Task RejectExpenseAsync_WhenPeriodFinalized_ThrowsTaxPeriodFinalized()
    {
        // Arrange
        var finalizedDate = new DateTime(2026, 3, 10, 8, 0, 0, DateTimeKind.Utc);
        var voucher = new ExpenseVoucher
        {
            Id = Guid.NewGuid(),
            VoucherCode = "EXP-20260310-02",
            ExpenseDate = finalizedDate,
            Status = ExpenseVoucherStatus.PendingApproval,
            Amount = 1_000_000m
        };
        _expenseRepo.Items.Add(voucher);

        _taxLockGuardMock.Setup(g => g.ValidateTaxPeriodUnlockedAsync(
                finalizedDate,
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("TAX_PERIOD_FINALIZED", "Kỳ tính thuế Tháng 3/2026 đã được chốt sổ bất biến."));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.RejectExpenseAsync(new RejectExpenseRequest(voucher.Id, "Từ chối duyệt")));

        Assert.Equal("TAX_PERIOD_FINALIZED", ex.ErrorCode);
    }

    [Fact]
    public async Task CancelExpenseAsync_WhenPaidVoucherInFinalizedPeriod_ReversalBlockedWithTaxPeriodFinalized()
    {
        // Arrange: Phiếu đã chi tiền (Paid) rơi vào kỳ thuế đã chốt.
        // Quy tắc: Không cho phép tạo bút toán đảo (Reversal) trực tiếp vào kỳ đã chốt.
        var finalizedDate = new DateTime(2026, 2, 20, 14, 0, 0, DateTimeKind.Utc);
        var voucher = new ExpenseVoucher
        {
            Id = Guid.NewGuid(),
            VoucherCode = "EXP-20260220-01",
            ExpenseDate = finalizedDate,
            Status = ExpenseVoucherStatus.Paid,
            Amount = 500_000m,
            PaymentSourceAccountId = Guid.NewGuid(),
            PaymentSourceType = FinanceAccountType.Till
        };
        _expenseRepo.Items.Add(voucher);

        _taxLockGuardMock.Setup(g => g.ValidateTaxPeriodUnlockedAsync(
                finalizedDate,
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("TAX_PERIOD_FINALIZED", "Kỳ tính thuế Tháng 2/2026 đã được chốt sổ bất biến. Không cho phép tạo bút toán đảo vào kỳ đã chốt."));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CancelExpenseAsync(new CancelExpenseRequest(voucher.Id, "Hủy và hoàn tiền")));

        Assert.Equal("TAX_PERIOD_FINALIZED", ex.ErrorCode);
        // Không gọi cashbook hoàn tiền khi bị chặn
        _cashbookServiceMock.Verify(c => c.ReceiveCashAsync(
            It.IsAny<Guid>(), It.IsAny<FinanceAccountType>(), It.IsAny<decimal>(),
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisburseExpenseAsync_WhenPeriodFinalized_ThrowsTaxPeriodFinalized()
    {
        // Arrange
        var finalizedDate = new DateTime(2026, 3, 12, 9, 0, 0, DateTimeKind.Utc);
        var voucher = new ExpenseVoucher
        {
            Id = Guid.NewGuid(),
            VoucherCode = "EXP-20260312-01",
            ExpenseDate = finalizedDate,
            Status = ExpenseVoucherStatus.Approved,
            Amount = 400_000m
        };
        _expenseRepo.Items.Add(voucher);

        _taxLockGuardMock.Setup(g => g.ValidateTaxPeriodUnlockedAsync(
                finalizedDate,
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("TAX_PERIOD_FINALIZED", "Kỳ tính thuế Tháng 3/2026 đã được chốt sổ bất biến."));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.DisburseExpenseAsync(new DisburseExpenseRequest(
                voucher.Id,
                FinanceAccountType.Till,
                Guid.NewGuid(),
                "Chi tiền"
            )));

        Assert.Equal("TAX_PERIOD_FINALIZED", ex.ErrorCode);
    }

    [Fact]
    public async Task ResolveTriageAsync_WhenPeriodFinalized_ThrowsTaxPeriodFinalized()
    {
        // Arrange
        var finalizedDate = new DateTime(2026, 3, 5, 14, 0, 0, DateTimeKind.Utc);
        var voucher = new ExpenseVoucher
        {
            Id = Guid.NewGuid(),
            VoucherCode = "EXP-TRIAGE-01",
            ExpenseDate = finalizedDate,
            Status = ExpenseVoucherStatus.Unresolved,
            Nature = FinancialNature.Suspense,
            Amount = 150_000m
        };
        _expenseRepo.Items.Add(voucher);

        _taxLockGuardMock.Setup(g => g.ValidateTaxPeriodUnlockedAsync(
                finalizedDate,
                "xử lý mục tài chính trong Triage Inbox",
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("TAX_PERIOD_FINALIZED", "Kỳ tính thuế Tháng 3/2026 đã được chốt sổ bất biến."));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ResolveTriageAsync(voucher.Id, new ResolveTriageRequest(
                FinancialNature.OperatingExpense,
                true,
                "Chi phí hợp lý",
                "Giải trình hợp lệ"
            )));

        Assert.Equal("TAX_PERIOD_FINALIZED", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateExpenseAsync_BranchScope_BothBranchesBlockedWhenPeriodFinalized()
    {
        // Arrange: Cả 2 chi nhánh đều cố tạo phiếu chi trong kỳ tính thuế đã khóa
        var finalizedDate = new DateTime(2026, 2, 10, 10, 0, 0, DateTimeKind.Utc);
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();

        _taxLockGuardMock.Setup(g => g.ValidateTaxPeriodUnlockedAsync(
                finalizedDate,
                "tạo phiếu chi",
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("TAX_PERIOD_FINALIZED", "Kỳ tính thuế Tháng 2/2026 đã được chốt sổ bất biến."));

        var reqA = new CreateExpenseRequest(
            BranchId: branchA,
            BeneficiaryName: "Nhà cung cấp A",
            Category: "Vật tư",
            Amount: 200_000m,
            PaymentSourceType: FinanceAccountType.Till,
            PaymentSourceAccountId: Guid.NewGuid(),
            IsTaxDeductible: true,
            InvoiceNumber: null,
            SellerTaxCode: null,
            EvidenceAttachmentUrl: null,
            Note: "Chi phí chi nhánh A",
            ExpenseDate: finalizedDate
        );

        var reqB = new CreateExpenseRequest(
            BranchId: branchB,
            BeneficiaryName: "Nhà cung cấp B",
            Category: "Vật tư",
            Amount: 300_000m,
            PaymentSourceType: FinanceAccountType.Till,
            PaymentSourceAccountId: Guid.NewGuid(),
            IsTaxDeductible: true,
            InvoiceNumber: null,
            SellerTaxCode: null,
            EvidenceAttachmentUrl: null,
            Note: "Chi phí chi nhánh B",
            ExpenseDate: finalizedDate
        );

        // Act & Assert
        var exA = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateExpenseAsync(reqA));
        var exB = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateExpenseAsync(reqB));

        Assert.Equal("TAX_PERIOD_FINALIZED", exA.ErrorCode);
        Assert.Equal("TAX_PERIOD_FINALIZED", exB.ErrorCode);
    }
}
