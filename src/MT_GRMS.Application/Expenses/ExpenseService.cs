using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Interfaces;
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
    private readonly Tax.ITaxPeriodLockGuard? _taxPeriodLockGuard;
    private readonly ICloudinaryService? _cloudinaryService;

    // Định mức thẩm quyền theo BR-17-06 / TT 152/2025/TT-BTC
    private const decimal T1_AutoApprovalThreshold = 500_000m; // Ngưỡng tự duyệt chi dưới 500k
    private const decimal C1_ShiftCapLimit = 500_000m; // Hạn mức trần ca chi vặt
    private const decimal C2_DailyCumulativeLimit = 20_000_000m; // Hạn mức ngày 20 triệu

    public ExpenseService(
        IExpenseRepository expenseRepo,
        ICashbookService cashbookService,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork,
        Tax.ITaxPeriodLockGuard? taxPeriodLockGuard = null,
        ICloudinaryService? cloudinaryService = null)
    {
        _expenseRepo = expenseRepo;
        _cashbookService = cashbookService;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
        _taxPeriodLockGuard = taxPeriodLockGuard;
        _cloudinaryService = cloudinaryService;
    }

    public async Task<ExpenseVoucherResponse> CreateExpenseAsync(CreateExpenseRequest request, CancellationToken ct = default)
    {
        RequireOwner();
        if (request.Amount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền chi phí phải lớn hơn 0.");
        }

        EnsureNatureInScope(request.Nature);

        if (request.Nature == FinancialNature.OwnerDraw || request.Nature == FinancialNature.TaxPayment)
        {
            ValidateFinancialManagementRole("thực hiện rút vốn hoặc nộp thuế");
        }

        var expenseDate = request.ExpenseDate ?? DateTime.UtcNow;

        if (_taxPeriodLockGuard != null)
        {
            await _taxPeriodLockGuard.ValidateTaxPeriodUnlockedAsync(expenseDate, "tạo phiếu chi", request.BranchId, ct);
        }
        var voucherCode = "EXP-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var resolvedEvidenceUrl = await CloudinaryDataUrlHelper.ResolveImageUrlAsync(
            _cloudinaryService,
            request.EvidenceAttachmentUrl,
            "mtgrms/expenses",
            voucherCode,
            ct);

        var voucher = new ExpenseVoucher
        {
            VoucherCode = voucherCode,
            BranchId = request.BranchId,
            BeneficiaryName = request.BeneficiaryName,
            Category = request.Category,
            Amount = request.Amount,
            PaymentSourceType = request.PaymentSourceType,
            PaymentSourceAccountId = request.PaymentSourceAccountId,
            IsTaxDeductible = request.IsTaxDeductible,
            InvoiceNumber = request.InvoiceNumber,
            SellerTaxCode = request.SellerTaxCode,
            EvidenceAttachmentUrl = resolvedEvidenceUrl,
            ExpenseDate = expenseDate,
            Note = request.Note,
            Nature = request.Nature,
            ReferenceCode = request.ReferenceCode,
            Status = ExpenseVoucherStatus.Draft
        };

        // Thuật toán Anti-Splitting Engine (REQ-EXP-02) & Trần ca C1
        var isSuspicious = false;
        string? suspiciousReason = null;
        var requiresApproval = false;
        var isOverShiftCap = false;

        if (request.BranchId.HasValue)
        {
            var branchId = request.BranchId.Value;

            // 0. Kiểm tra trần ca chi vặt C1 (500.000đ)
            if (request.Nature == FinancialNature.OperatingExpense && request.Amount <= T1_AutoApprovalThreshold)
            {
                var shiftSum = await _expenseRepo.GetShiftExpenseSumAsync(branchId, DateTime.UtcNow.Date, ct);
                if (shiftSum + request.Amount > C1_ShiftCapLimit)
                {
                    isOverShiftCap = true;
                    isSuspicious = true;
                    suspiciousReason = $"Tổng chi trong ca ({shiftSum + request.Amount:N0}đ) vượt trần ca cho phép ({C1_ShiftCapLimit:N0}đ). Đã chuyển vào Triage Inbox chờ chủ duyệt.";
                }
            }

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
        if (request.Nature == FinancialNature.Suspense || isOverShiftCap)
        {
            voucher.Status = ExpenseVoucherStatus.Unresolved;
            if (request.Nature == FinancialNature.Suspense && string.IsNullOrWhiteSpace(voucher.SuspiciousReason))
            {
                voucher.SuspiciousReason = "Phiếu mục không rõ - chờ chủ phân loại.";
            }
        }
        else if (request.Amount > T1_AutoApprovalThreshold || requiresApproval || request.PaymentSourceType == FinanceAccountType.Bank)
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
        RequireOwner();
        ValidateFinancialManagementRole("phê duyệt phiếu chi");

        var voucher = await _expenseRepo.GetByIdAsync(request.ExpenseId, ct)
            ?? throw new NotFoundException("Không tìm thấy phiếu chi.");

        if (_taxPeriodLockGuard != null)
        {
            await _taxPeriodLockGuard.ValidateTaxPeriodUnlockedAsync(voucher.ExpenseDate, "phê duyệt phiếu chi", voucher.BranchId, ct);
        }

        // Idempotent: Nếu đã duyệt hoặc đã thanh toán thành công thì không báo lỗi 422
        if (voucher.Status == ExpenseVoucherStatus.Approved || voucher.Status == ExpenseVoucherStatus.Paid)
        {
            return MapVoucherResponse(voucher);
        }

        if (voucher.Status != ExpenseVoucherStatus.PendingApproval &&
            voucher.Status != ExpenseVoucherStatus.Draft &&
            voucher.Status != ExpenseVoucherStatus.PaymentFailed &&
            voucher.Status != ExpenseVoucherStatus.Unresolved)
        {
            throw new BusinessException("INVALID_STATUS", $"Phiếu chi đang ở trạng thái '{voucher.Status}', không thể phê duyệt.");
        }

        // Kiểm toán tiệm 1 chủ solo: Nếu phiếu bị cảnh báo xé nhỏ hạn mức thì bắt buộc phải nhập lý do/ghi chú giải trình
        if (voucher.IsFlaggedAsSuspiciousSplit && string.IsNullOrWhiteSpace(request.Note) && string.IsNullOrWhiteSpace(voucher.Note))
        {
            throw new BusinessException("NOTE_REQUIRED_FOR_SUSPICIOUS_EXPENSE", "Phiếu chi bị gắn cờ nghi vấn xé nhỏ hạn mức. Bắt buộc nhập ghi chú giải trình khi phê duyệt.");
        }

        voucher.Status = ExpenseVoucherStatus.Approved;
        voucher.ApprovedByUserId = _tenantContext.CurrentUserId != Guid.Empty ? _tenantContext.CurrentUserId : Guid.NewGuid();
        voucher.ApprovedAtUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            voucher.Note = string.IsNullOrWhiteSpace(voucher.Note)
                ? request.Note.Trim()
                : $"{voucher.Note} | {request.Note.Trim()}";
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return MapVoucherResponse(voucher);
    }

    public async Task<ExpenseVoucherResponse> RejectExpenseAsync(RejectExpenseRequest request, CancellationToken ct = default)
    {
        RequireOwner();
        ValidateFinancialManagementRole("từ chối phê duyệt phiếu chi");

        var voucher = await _expenseRepo.GetByIdAsync(request.ExpenseId, ct)
            ?? throw new NotFoundException("Không tìm thấy phiếu chi.");

        if (_taxPeriodLockGuard != null)
        {
            await _taxPeriodLockGuard.ValidateTaxPeriodUnlockedAsync(voucher.ExpenseDate, "từ chối phê duyệt phiếu chi", voucher.BranchId, ct);
        }

        if (voucher.Status != ExpenseVoucherStatus.PendingApproval &&
            voucher.Status != ExpenseVoucherStatus.Draft &&
            voucher.Status != ExpenseVoucherStatus.Unresolved)
        {
            throw new BusinessException("INVALID_STATUS", $"Phiếu chi đang ở trạng thái '{voucher.Status}', không thể từ chối duyệt.");
        }

        voucher.Status = ExpenseVoucherStatus.Cancelled;
        var reasonText = string.IsNullOrWhiteSpace(request.Reason) ? "Không có lý do cụ thể" : request.Reason.Trim();
        voucher.Note = string.IsNullOrWhiteSpace(voucher.Note)
            ? $"[TỪ CHỐI DUYỆT]: {reasonText}"
            : $"{voucher.Note} | [TỪ CHỐI DUYỆT]: {reasonText}";

        await _unitOfWork.SaveChangesAsync(ct);
        return MapVoucherResponse(voucher);
    }

    public async Task<ExpenseVoucherResponse> CancelExpenseAsync(CancelExpenseRequest request, CancellationToken ct = default)
    {
        RequireOwner();

        var voucher = await _expenseRepo.GetByIdAsync(request.ExpenseId, ct)
            ?? throw new NotFoundException("Không tìm thấy phiếu chi.");

        if (_taxPeriodLockGuard != null)
        {
            await _taxPeriodLockGuard.ValidateTaxPeriodUnlockedAsync(voucher.ExpenseDate, "hủy phiếu chi", voucher.BranchId, ct);
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new BusinessException("REASON_REQUIRED", "Bắt buộc phải nhập lý do khi hủy phiếu chi.");
        }

        if (voucher.Status == ExpenseVoucherStatus.Paid)
        {
            // Bút toán đảo (Reversal Entry): Hoàn tiền lại vào tài khoản nguồn
            if (!voucher.PaymentSourceAccountId.HasValue)
            {
                throw new BusinessException("MISSING_PAYMENT_SOURCE", "Phiếu chi thiếu thông tin tài khoản nguồn để thực hiện bút toán đảo hoàn tiền.");
            }

            var reversalRefCode = "REV-" + voucher.VoucherCode;
            var reversalDesc = $"Bút toán đảo: Hoàn tiền phiếu chi {voucher.VoucherCode} đã thanh toán do bị hủy - Lý do: {request.Reason.Trim()}";

            await _cashbookService.ReceiveCashAsync(
                voucher.PaymentSourceAccountId.Value,
                voucher.PaymentSourceType,
                voucher.Amount,
                "EXPENSE_REVERSAL",
                voucher.Id,
                reversalRefCode,
                reversalDesc,
                ct
            );

            voucher.Status = ExpenseVoucherStatus.Reversed;
            voucher.Note = string.IsNullOrWhiteSpace(voucher.Note)
                ? $"[ĐÃ ĐẢO BÚT TOÁN HOÀN TIỀN]: {request.Reason.Trim()}"
                : $"{voucher.Note} | [ĐÃ ĐẢO BÚT TOÁN HOÀN TIỀN]: {request.Reason.Trim()}";
        }
        else if (voucher.Status == ExpenseVoucherStatus.Draft ||
                 voucher.Status == ExpenseVoucherStatus.PendingApproval ||
                 voucher.Status == ExpenseVoucherStatus.Approved ||
                 voucher.Status == ExpenseVoucherStatus.PaymentFailed ||
                 voucher.Status == ExpenseVoucherStatus.Unresolved)
        {
            voucher.Status = ExpenseVoucherStatus.Cancelled;
            voucher.Note = string.IsNullOrWhiteSpace(voucher.Note)
                ? $"[ĐÃ HỦY]: {request.Reason.Trim()}"
                : $"{voucher.Note} | [ĐÃ HỦY]: {request.Reason.Trim()}";
        }
        else
        {
            throw new BusinessException("INVALID_STATUS", $"Phiếu chi đang ở trạng thái '{voucher.Status}', không thể hủy.");
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return MapVoucherResponse(voucher);
    }

    public async Task<ExpenseVoucherResponse> DisburseExpenseAsync(DisburseExpenseRequest request, CancellationToken ct = default)
    {
        RequireOwner();
        ValidateFinancialManagementRole("xuất quỹ chi trả phiếu chi");

        var voucher = await _expenseRepo.GetByIdAsync(request.ExpenseId, ct)
            ?? throw new NotFoundException("Không tìm thấy phiếu chi.");

        if (_taxPeriodLockGuard != null)
        {
            await _taxPeriodLockGuard.ValidateTaxPeriodUnlockedAsync(voucher.ExpenseDate, "chi tiền cho phiếu chi", voucher.BranchId, ct);
        }

        // Idempotent: Nếu đã xuất quỹ thành công từ trước thì trả về luôn không thực hiện trừ quỹ kép
        if (voucher.Status == ExpenseVoucherStatus.Paid)
        {
            return MapVoucherResponse(voucher);
        }

        // Tự động phê duyệt cho Chủ cửa hàng / Quản lý nếu phiếu đang ở Draft, PendingApproval hoặc Unresolved (1-click payment flow)
        if (voucher.Status == ExpenseVoucherStatus.Draft ||
            voucher.Status == ExpenseVoucherStatus.PendingApproval ||
            voucher.Status == ExpenseVoucherStatus.Unresolved)
        {
            if (voucher.IsFlaggedAsSuspiciousSplit && string.IsNullOrWhiteSpace(request.Note) && string.IsNullOrWhiteSpace(voucher.Note))
            {
                throw new BusinessException("NOTE_REQUIRED_FOR_SUSPICIOUS_EXPENSE", "Phiếu chi bị gắn cờ nghi vấn xé nhỏ hạn mức. Bắt buộc nhập ghi chú giải trình khi phê duyệt và xuất quỹ.");
            }

            voucher.Status = ExpenseVoucherStatus.Approved;
            voucher.ApprovedByUserId = _tenantContext.CurrentUserId != Guid.Empty ? _tenantContext.CurrentUserId : Guid.NewGuid();
            voucher.ApprovedAtUtc = DateTime.UtcNow;
        }
        else if (voucher.Status != ExpenseVoucherStatus.Approved && voucher.Status != ExpenseVoucherStatus.PaymentFailed)
        {
            throw new BusinessException("INVALID_STATUS", $"Phiếu chi đang ở trạng thái '{voucher.Status}', không thể xuất quỹ chi trả.");
        }

        // Cập nhật/bổ sung ghi chú giải trình hoặc thanh toán nếu có (áp dụng cho mọi trường hợp xuất quỹ)
        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            voucher.Note = string.IsNullOrWhiteSpace(voucher.Note)
                ? request.Note.Trim()
                : $"{voucher.Note} | {request.Note.Trim()}";
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
        RequireOwner();
        var voucher = await _expenseRepo.GetByIdAsync(expenseId, ct)
            ?? throw new NotFoundException("Không tìm thấy phiếu chi.");

        return MapVoucherResponse(voucher);
    }

    public async Task<List<ExpenseVoucherResponse>> GetExpensesAsync(Guid? branchId, DateTime? fromDate, DateTime? toDate, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        RequireOwner();
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
        RequireOwner();
        var vouchers = await _expenseRepo.FindAsync(e =>
            (!branchId.HasValue || e.BranchId == branchId.Value) &&
            (!fromDate.HasValue || e.CreatedAt >= fromDate.Value) &&
            (!toDate.HasValue || e.CreatedAt <= toDate.Value), ct);

        var activeVouchers = vouchers
            .Where(v => v.Status != ExpenseVoucherStatus.Cancelled && v.Status != ExpenseVoucherStatus.Reversed)
            .ToList();

        var totalAmount = activeVouchers.Sum(v => v.Amount);
        var totalCount = activeVouchers.Count;

        var paidVouchers = activeVouchers.Where(v => v.Status == ExpenseVoucherStatus.Paid).ToList();
        var pendingVouchers = activeVouchers.Where(v => v.Status != ExpenseVoucherStatus.Paid).ToList();
        var suspiciousVouchers = activeVouchers.Where(v => v.IsFlaggedAsSuspiciousSplit).ToList();
        var taxDeductibleVouchers = activeVouchers.Where(v => v.IsTaxDeductible).ToList();
        var nonTaxDeductibleVouchers = activeVouchers.Where(v => !v.IsTaxDeductible).ToList();

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

    public async Task<List<ExpenseVoucherResponse>> GetPendingTriageAsync(Guid? branchId, CancellationToken ct = default)
    {
        RequireOwner();
        ValidateFinancialManagementRole("xem danh sách Triage Inbox");

        var vouchers = await _expenseRepo.FindAsync(e =>
            (!branchId.HasValue || e.BranchId == branchId.Value) &&
            (e.Status == ExpenseVoucherStatus.Unresolved || e.Nature == FinancialNature.Suspense),
            ct
        );

        return vouchers
            .OrderByDescending(e => e.CreatedAt)
            .Select(MapVoucherResponse)
            .ToList();
    }

    public async Task<ExpenseVoucherResponse> ResolveTriageAsync(Guid voucherId, ResolveTriageRequest request, CancellationToken ct = default)
    {
        RequireOwner();
        ValidateFinancialManagementRole("xử lý mục tài chính trong Triage Inbox");
        EnsureNatureInScope(request.Nature);

        var voucher = await _expenseRepo.GetByIdAsync(voucherId, ct)
            ?? throw new NotFoundException("Không tìm thấy phiếu chi/mục cần xử lý trong Triage Inbox.");

        if (_taxPeriodLockGuard != null)
        {
            await _taxPeriodLockGuard.ValidateTaxPeriodUnlockedAsync(voucher.ExpenseDate, "xử lý mục tài chính trong Triage Inbox", voucher.BranchId, ct);
        }

        if (voucher.Status != ExpenseVoucherStatus.Unresolved && voucher.Nature != FinancialNature.Suspense)
        {
            throw new BusinessException("INVALID_STATUS", "Phiếu này không ở trạng thái Chờ xử lý (Unresolved).");
        }

        voucher.Nature = request.Nature;
        voucher.IsTaxDeductible = request.IsTaxDeductible;
        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            voucher.Category = request.Category.Trim();
        }

        voucher.Status = ExpenseVoucherStatus.Approved;
        voucher.ApprovedByUserId = _tenantContext.CurrentUserId != Guid.Empty ? _tenantContext.CurrentUserId : Guid.NewGuid();
        voucher.ApprovedAtUtc = DateTime.UtcNow;
        voucher.Note = string.IsNullOrWhiteSpace(voucher.Note)
            ? $"[TRIAGE ĐÃ XỬ LÝ]: {request.ResolutionNote.Trim()}"
            : $"{voucher.Note} | [TRIAGE ĐÃ XỬ LÝ]: {request.ResolutionNote.Trim()}";

        await _unitOfWork.SaveChangesAsync(ct);
        return MapVoucherResponse(voucher);
    }

    /// <summary>
    /// Rejects financial natures that are out of scope (bottle/container deposit and its refund).
    /// </summary>
    private static void EnsureNatureInScope(FinancialNature nature)
    {
        if (nature == FinancialNature.Deposit || nature == FinancialNature.RefundDeposit)
        {
            throw new BusinessException("FEATURE_OUT_OF_SCOPE", "Nghiệp vụ thu/hoàn cọc vỏ bình/két nằm ngoài phạm vi hệ thống.");
        }
    }

    private void ValidateFinancialManagementRole(string actionDescription = "thực hiện thao tác tài chính này") => RequireOwner();

    private void RequireOwner() => TenantRoleGuard.RequireStoreOwner(_tenantContext, "chi phí và phiếu chi");

    private static ExpenseVoucherResponse MapVoucherResponse(ExpenseVoucher v) =>
        new(v.Id, v.VoucherCode, v.BranchId, v.BeneficiaryName, v.Category, v.Amount, v.Status.ToString(), v.PaymentSourceType.ToString(), v.PaymentSourceAccountId, v.IsTaxDeductible, v.InvoiceNumber, v.SellerTaxCode, v.EvidenceAttachmentUrl, v.IsFlaggedAsSuspiciousSplit, v.SuspiciousReason, v.ApprovedByUserId, v.ApprovedAtUtc, v.PaidAtUtc, v.ExpenseDate, v.Note, v.Nature, v.ReferenceCode, v.RefundedAmount, v.IsSettled);
}
