using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Expenses.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Expenses;

public class ExpenseService : IExpenseService
{
    private readonly IExpenseRepository _expenseRepo;
    private readonly ICashbookService _cashbookService;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    // Định mức thẩm quyền theo BR-17-06 / TT 152/2025/TT-BTC
    private const decimal T1_AutoApprovalThreshold = 500_000m; // Ngưỡng tự duyệt chi dưới 500k
    private const decimal C2_DailyCumulativeLimit = 20_000_000m; // Hạn mức ngày 20 triệu

    public ExpenseService(
        IExpenseRepository expenseRepo,
        ICashbookService cashbookService,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _expenseRepo = expenseRepo;
        _cashbookService = cashbookService;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<ExpenseVoucherResponse> CreateExpenseAsync(CreateExpenseRequest request, CancellationToken ct = default)
    {
        if (request.Amount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền chi phí phải lớn hơn 0.");
        }

        var voucher = new ExpenseVoucher
        {
            VoucherCode = "EXP-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
            BranchId = request.BranchId,
            BeneficiaryName = request.BeneficiaryName,
            Category = request.Category,
            Amount = request.Amount,
            PaymentSourceType = request.PaymentSourceType,
            PaymentSourceAccountId = request.PaymentSourceAccountId,
            IsTaxDeductible = request.IsTaxDeductible,
            InvoiceNumber = request.InvoiceNumber,
            SellerTaxCode = request.SellerTaxCode,
            EvidenceAttachmentUrl = request.EvidenceAttachmentUrl,
            ExpenseDate = DateTime.UtcNow,
            Note = request.Note,
            Status = ExpenseVoucherStatus.Draft
        };

        // Thuật toán Anti-Splitting Engine (REQ-EXP-02)
        var isSuspicious = false;
        string? suspiciousReason = null;
        var requiresApproval = false;

        if (request.BranchId.HasValue)
        {
            var branchId = request.BranchId.Value;

            // 1. Gộp theo đối tượng/hạng mục trong ngày
            var sameVendorSum = await _expenseRepo.GetSameVendorDailySumAsync(branchId, request.BeneficiaryName, request.Category, DateTime.UtcNow, ct);
            if (sameVendorSum + request.Amount > T1_AutoApprovalThreshold)
            {
                requiresApproval = true;
                suspiciousReason = $"Tổng chi cùng đối tượng '{request.BeneficiaryName}' trong ngày vượt ngưỡng tự duyệt ({sameVendorSum + request.Amount:N0}đ > {T1_AutoApprovalThreshold:N0}đ).";
            }

            // 2. Mẫu hình tần suất: >= 3 phiếu trong 60 phút có giá trị >= 60% T1 (300.000đ)
            var currentUserId = _tenantContext.CurrentUserId;
            var recentCount = await _expenseRepo.GetRecentVouchersCountAsync(branchId, currentUserId, DateTime.UtcNow.AddMinutes(-60), T1_AutoApprovalThreshold * 0.6m, ct);
            if (recentCount >= 2 && request.Amount >= T1_AutoApprovalThreshold * 0.6m)
            {
                isSuspicious = true;
                requiresApproval = true;
                suspiciousReason = "Phát hiện mẫu hình tạo nhiều phiếu chi giá trị lớn trong 60 phút (nghi vấn chia nhỏ hóa đơn né duyệt).";
            }

            // 3. Hạn mức lũy kế ngày C2
            var dailySum = await _expenseRepo.GetDailyExpenseSumAsync(branchId, DateTime.UtcNow, ct);
            if (dailySum + request.Amount > C2_DailyCumulativeLimit)
            {
                requiresApproval = true;
            }
        }

        voucher.IsFlaggedAsSuspiciousSplit = isSuspicious;
        voucher.SuspiciousReason = suspiciousReason;

        // Quyết định trạng thái phê duyệt ban đầu
        if (request.Amount > T1_AutoApprovalThreshold || requiresApproval || request.PaymentSourceType == FinanceAccountType.Bank)
        {
            voucher.Status = ExpenseVoucherStatus.PendingApproval;
        }
        else
        {
            voucher.Status = ExpenseVoucherStatus.Approved;
            voucher.ApprovedAtUtc = DateTime.UtcNow;
        }

        await _expenseRepo.AddAsync(voucher, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return MapVoucherResponse(voucher);
    }

    public async Task<ExpenseVoucherResponse> ApproveExpenseAsync(ApproveExpenseRequest request, CancellationToken ct = default)
    {
        var role = _tenantContext.CurrentRole;
        if (!string.IsNullOrWhiteSpace(role) &&
            !role.Equals("Owner", StringComparison.OrdinalIgnoreCase) &&
            !role.Equals("Store_Owner", StringComparison.OrdinalIgnoreCase) &&
            !role.Equals("Admin", StringComparison.OrdinalIgnoreCase) &&
            !role.Equals("Manager", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Chỉ Chủ hộ kinh doanh hoặc Quản lý cửa hàng mới có thẩm quyền phê duyệt phiếu chi.");
        }

        var voucher = await _expenseRepo.GetByIdAsync(request.ExpenseId, ct)
            ?? throw new NotFoundException("Không tìm thấy phiếu chi.");

        if (voucher.Status != ExpenseVoucherStatus.PendingApproval)
        {
            throw new BusinessException("INVALID_STATUS", $"Phiếu chi đang ở trạng thái '{voucher.Status}', không thể phê duyệt.");
        }

        // Kiểm toán tiệm 1 chủ solo: Nếu phiếu bị cảnh báo xé nhỏ hạn mức thì bắt buộc phải nhập lý do/ghi chú giải trình
        if (voucher.IsFlaggedAsSuspiciousSplit && string.IsNullOrWhiteSpace(request.Note))
        {
            throw new BusinessException("NOTE_REQUIRED_FOR_SUSPICIOUS_EXPENSE", "Phiếu chi bị gắn cờ nghi vấn xé nhỏ hạn mức. Bắt buộc nhập ghi chú giải trình khi phê duyệt.");
        }

        voucher.Status = ExpenseVoucherStatus.Approved;
        voucher.ApprovedByUserId = _tenantContext.CurrentUserId != Guid.Empty ? _tenantContext.CurrentUserId : Guid.NewGuid();
        voucher.ApprovedAtUtc = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(request.Note)) voucher.Note = request.Note;

        await _unitOfWork.SaveChangesAsync(ct);
        return MapVoucherResponse(voucher);
    }

    public async Task<ExpenseVoucherResponse> DisburseExpenseAsync(DisburseExpenseRequest request, CancellationToken ct = default)
    {
        var voucher = await _expenseRepo.GetByIdAsync(request.ExpenseId, ct)
            ?? throw new NotFoundException("Không tìm thấy phiếu chi.");

        if (voucher.Status != ExpenseVoucherStatus.Approved && voucher.Status != ExpenseVoucherStatus.PaymentFailed)
        {
            throw new BusinessException("INVALID_STATUS", $"Phiếu chi chưa được phê duyệt hoặc đã được chi trả (Trạng thái hiện tại: {voucher.Status}).");
        }

        voucher.PaymentSourceType = request.PaymentSourceType;
        voucher.PaymentSourceAccountId = request.PaymentSourceAccountId;

        try
        {
            await _cashbookService.DisburseCashAsync(
                request.PaymentSourceAccountId,
                request.PaymentSourceType,
                voucher.Amount,
                "EXPENSE",
                voucher.Id,
                voucher.VoucherCode,
                $"Chi phí: {voucher.Category} - {voucher.BeneficiaryName}",
                ct
            );

            voucher.Status = ExpenseVoucherStatus.Paid;
            voucher.PaidAtUtc = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (InsufficientStockException)
        {
            voucher.Status = ExpenseVoucherStatus.PaymentFailed;
            await _unitOfWork.SaveChangesAsync(ct);
            throw;
        }

        return MapVoucherResponse(voucher);
    }

    public async Task<ExpenseVoucherResponse> GetExpenseByIdAsync(Guid expenseId, CancellationToken ct = default)
    {
        var voucher = await _expenseRepo.GetByIdAsync(expenseId, ct)
            ?? throw new NotFoundException("Không tìm thấy phiếu chi.");

        return MapVoucherResponse(voucher);
    }

    public async Task<List<ExpenseVoucherResponse>> GetExpensesAsync(Guid? branchId, DateTime? fromDate, DateTime? toDate, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var vouchers = await _expenseRepo.FindAsync(e =>
            (!branchId.HasValue || e.BranchId == branchId.Value) &&
            (!fromDate.HasValue || e.CreatedAt >= fromDate.Value) &&
            (!toDate.HasValue || e.CreatedAt <= toDate.Value), ct);

        return vouchers
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapVoucherResponse)
            .ToList();
    }

    public async Task<ExpenseSummaryResponse> GetExpenseSummaryAsync(Guid? branchId, DateTime? fromDate, DateTime? toDate, CancellationToken ct = default)
    {
        var vouchers = await _expenseRepo.FindAsync(e =>
            (!branchId.HasValue || e.BranchId == branchId.Value) &&
            (!fromDate.HasValue || e.CreatedAt >= fromDate.Value) &&
            (!toDate.HasValue || e.CreatedAt <= toDate.Value), ct);

        var totalAmount = vouchers.Sum(v => v.Amount);
        var totalCount = vouchers.Count;

        var paidVouchers = vouchers.Where(v => v.Status == ExpenseVoucherStatus.Paid).ToList();
        var pendingVouchers = vouchers.Where(v => v.Status == ExpenseVoucherStatus.PendingApproval).ToList();
        var suspiciousVouchers = vouchers.Where(v => v.IsFlaggedAsSuspiciousSplit).ToList();
        var taxDeductibleVouchers = vouchers.Where(v => v.IsTaxDeductible).ToList();
        var nonTaxDeductibleVouchers = vouchers.Where(v => !v.IsTaxDeductible).ToList();

        return new ExpenseSummaryResponse(
            TotalAmount: totalAmount,
            TotalCount: totalCount,
            PaidAmount: paidVouchers.Sum(v => v.Amount),
            PaidCount: paidVouchers.Count,
            PendingApprovalAmount: pendingVouchers.Sum(v => v.Amount),
            PendingApprovalCount: pendingVouchers.Count,
            SuspiciousAmount: suspiciousVouchers.Sum(v => v.Amount),
            SuspiciousCount: suspiciousVouchers.Count,
            TaxDeductibleAmount: taxDeductibleVouchers.Sum(v => v.Amount),
            TaxDeductibleCount: taxDeductibleVouchers.Count,
            NonTaxDeductibleAmount: nonTaxDeductibleVouchers.Sum(v => v.Amount),
            NonTaxDeductibleCount: nonTaxDeductibleVouchers.Count
        );
    }

    private static ExpenseVoucherResponse MapVoucherResponse(ExpenseVoucher v) =>
        new(v.Id, v.VoucherCode, v.BranchId, v.BeneficiaryName, v.Category, v.Amount, v.Status.ToString(), v.PaymentSourceType.ToString(), v.PaymentSourceAccountId, v.IsTaxDeductible, v.InvoiceNumber, v.SellerTaxCode, v.EvidenceAttachmentUrl, v.IsFlaggedAsSuspiciousSplit, v.SuspiciousReason, v.ApprovedByUserId, v.ApprovedAtUtc, v.PaidAtUtc, v.ExpenseDate, v.Note);
}
