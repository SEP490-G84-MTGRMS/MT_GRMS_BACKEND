using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.Expenses.Dtos;
using MT_GRMS.Application.Notifications;
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
    private readonly NotificationOutbox? _notificationOutbox;

    // Định mức thẩm quyền theo BR-17-06 / TT 152/2025/TT-BTC
    private const decimal T1_AutoApprovalThreshold = 500_000m; // Ngưỡng tự duyệt chi dưới 500k
    private const decimal C1_ShiftCapLimit = 500_000m; // Hạn mức trần ca chi vặt
    private const decimal C2_DailyCumulativeLimit = 20_000_000m; // Hạn mức ngày 20 triệu
    private static readonly string[] TaxBlacklistKeywords =
    [
        "phạt",
        "vi phạm",
        "cá nhân",
        "từ thiện",
        "phong bì",
        "không hóa đơn",
        "rút vốn",
        "tiếp khách không chứng từ",
    ];

    private static bool IsTaxBlacklisted(string? category, string? beneficiaryName, string? note)
    {
        var combined = $"{category} {beneficiaryName} {note}".ToLowerInvariant();
        return TaxBlacklistKeywords.Any(kw => combined.Contains(kw, StringComparison.Ordinal));
    }

    public ExpenseService(
        IExpenseRepository expenseRepo,
        ICashbookService cashbookService,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork,
        Tax.ITaxPeriodLockGuard? taxPeriodLockGuard = null,
        ICloudinaryService? cloudinaryService = null,
        NotificationOutbox? notificationOutbox = null)
    {
        _expenseRepo = expenseRepo;
        _cashbookService = cashbookService;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
        _taxPeriodLockGuard = taxPeriodLockGuard;
        _cloudinaryService = cloudinaryService;
        _notificationOutbox = notificationOutbox;
    }

    public async Task<ExpenseVoucherResponse> CreateExpenseAsync(CreateExpenseRequest request, CancellationToken ct = default)
    {
        RequireOwner();
        if (request.Amount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền chi phí phải lớn hơn 0.");
        }

        if (string.IsNullOrWhiteSpace(request.BeneficiaryName) || request.BeneficiaryName.Trim().Length > 200)
        {
            throw new BusinessException("INVALID_BENEFICIARY", "Tên người thụ hưởng/nhà cung cấp không được để trống và tối đa 200 ký tự.");
        }

        if (string.IsNullOrWhiteSpace(request.Category) || request.Category.Trim().Length > 100)
        {
            throw new BusinessException("INVALID_CATEGORY", "Danh mục chi phí không được để trống và tối đa 100 ký tự.");
        }

        if (!Enum.IsDefined(typeof(FinanceAccountType), request.PaymentSourceType))
        {
            throw new BusinessException("INVALID_PAYMENT_SOURCE", "Loại nguồn chi thanh toán không hợp lệ.");
        }

        if (!string.IsNullOrWhiteSpace(request.SellerTaxCode) && request.SellerTaxCode.Trim().Length > 50)
        {
            throw new BusinessException("INVALID_TAX_CODE", "Mã số thuế bên bán không vượt quá 50 ký tự.");
        }

        if (request.BranchId.HasValue)
        {
            var exists = await _expenseRepo.BranchExistsAsync(request.BranchId.Value, ct);
            if (!exists)
            {
                throw new NotFoundException($"Không tìm thấy chi nhánh với mã {request.BranchId.Value}.");
            }
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
        var voucherCode = $"EXP-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Random.Shared.Next(100, 999)}";
        var resolvedEvidenceUrl = await CloudinaryDataUrlHelper.ResolveImageUrlAsync(
            _cloudinaryService,
            request.EvidenceAttachmentUrl,
            "mtgrms/expenses",
            voucherCode,
            ct);

        var effectiveTaxDeductible = request.IsTaxDeductible
            && request.Nature == FinancialNature.OperatingExpense
            && !IsTaxBlacklisted(request.Category, request.BeneficiaryName, request.Note);

        var voucher = new ExpenseVoucher
        {
            VoucherCode = voucherCode,
            BranchId = request.BranchId,
            BeneficiaryName = request.BeneficiaryName,
            Category = request.Category,
            Amount = request.Amount,
            PaymentSourceType = request.PaymentSourceType,
            PaymentSourceAccountId = request.PaymentSourceAccountId,
            IsTaxDeductible = effectiveTaxDeductible,
            InvoiceNumber = request.InvoiceNumber,
            SellerTaxCode = request.SellerTaxCode,
            EvidenceAttachmentUrl = resolvedEvidenceUrl,
            ExpenseDate = expenseDate,
            DueDate = request.DueDate,
            Note = request.Note,
            Nature = request.Nature,
            ReferenceCode = request.ReferenceCode,
            Status = ExpenseVoucherStatus.Draft
        };

        // Thuật toán Anti-Splitting Engine (REQ-EXP-02) & Trần ca C1
        var isSuspicious = false;
        var suspiciousReasons = new List<string>();
        var requiresApproval = false;
        var isOverShiftCap = false;

        if (request.BranchId.HasValue)
        {
            var branchId = request.BranchId.Value;
            var vnDayStartUtc = DateTime.UtcNow.AddHours(7).Date.AddHours(-7);

            // 0. Kiểm tra trần ca chi vặt C1 (500.000đ)
            if (request.Nature == FinancialNature.OperatingExpense && request.Amount <= T1_AutoApprovalThreshold)
            {
                var shiftSum = await _expenseRepo.GetShiftExpenseSumAsync(branchId, vnDayStartUtc, ct);
                if (shiftSum + request.Amount > C1_ShiftCapLimit)
                {
                    isOverShiftCap = true;
                    isSuspicious = true;
                    suspiciousReasons.Add($"Tổng chi trong ca ({shiftSum + request.Amount:N0}đ) vượt trần ca cho phép ({C1_ShiftCapLimit:N0}đ). Đã chuyển vào Triage Inbox chờ chủ duyệt.");
                }
            }

            // 1. Gộp theo đối tượng/hạng mục trong ngày
            var sameVendorSum = await _expenseRepo.GetSameVendorDailySumAsync(branchId, request.BeneficiaryName, request.Category, DateTime.UtcNow, ct);
            if (sameVendorSum + request.Amount > T1_AutoApprovalThreshold)
            {
                isSuspicious = true;
                requiresApproval = true;
                suspiciousReasons.Add($"Tổng chi cùng đối tượng '{request.BeneficiaryName}' trong ngày vượt ngưỡng tự duyệt ({sameVendorSum + request.Amount:N0}đ > {T1_AutoApprovalThreshold:N0}đ).");
            }

            // 2. Mẫu hình tần suất: >= 3 phiếu trong 60 phút có giá trị >= 60% T1 (300.000đ)
            var currentUserId = _tenantContext.CurrentUserId;
            var recentCount = await _expenseRepo.GetRecentVouchersCountAsync(branchId, currentUserId, DateTime.UtcNow.AddMinutes(-60), T1_AutoApprovalThreshold * 0.6m, ct);
            if (recentCount >= 2 && request.Amount >= T1_AutoApprovalThreshold * 0.6m)
            {
                isSuspicious = true;
                requiresApproval = true;
                suspiciousReasons.Add("Phát hiện mẫu hình tạo nhiều phiếu chi giá trị lớn trong 60 phút (nghi vấn chia nhỏ hóa đơn né duyệt).");
            }

            // 3. Hạn mức lũy kế ngày C2
            var dailySum = await _expenseRepo.GetDailyExpenseSumAsync(branchId, DateTime.UtcNow, ct);
            if (dailySum + request.Amount > C2_DailyCumulativeLimit)
            {
                requiresApproval = true;
            }
        }

        voucher.IsFlaggedAsSuspiciousSplit = isSuspicious;
        voucher.SuspiciousReason = suspiciousReasons.Count > 0 ? string.Join(" | ", suspiciousReasons) : null;

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
        if (voucher.Status == ExpenseVoucherStatus.PendingApproval)
            await QueueNotificationAsync(voucher, BusinessNotificationTypes.ExpenseApprovalRequired, "Phiếu chi cần duyệt", ct);
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

        // Kiểm toán tiệm 1 chủ solo: Nếu phiếu bị cảnh báo xé nhỏ hạn mức thì bắt buộc phải nhập lý do/ghi chú giải trình khi duyệt
        if (voucher.IsFlaggedAsSuspiciousSplit && string.IsNullOrWhiteSpace(request.Note))
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

        return await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var voucher = await _expenseRepo.GetByIdAsync(request.ExpenseId, token)
                ?? throw new NotFoundException("Không tìm thấy phiếu chi.");

            if (_taxPeriodLockGuard != null)
            {
                await _taxPeriodLockGuard.ValidateTaxPeriodUnlockedAsync(voucher.ExpenseDate, "hủy phiếu chi", voucher.BranchId, token);
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
                    token
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

            await _unitOfWork.SaveChangesAsync(token);
            return MapVoucherResponse(voucher);
        }, ct);
    }

    public async Task<ExpenseVoucherResponse> DisburseExpenseAsync(DisburseExpenseRequest request, CancellationToken ct = default)
    {
        RequireOwner();
        ValidateFinancialManagementRole("xuất quỹ chi trả phiếu chi");

        var result = await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var voucher = await _expenseRepo.GetByIdAsync(request.ExpenseId, token)
                ?? throw new NotFoundException("Không tìm thấy phiếu chi.");

            if (_taxPeriodLockGuard != null)
            {
                await _taxPeriodLockGuard.ValidateTaxPeriodUnlockedAsync(voucher.ExpenseDate, "chi tiền cho phiếu chi", voucher.BranchId, token);
            }

            // Idempotent: Nếu đã xuất quỹ thành công từ trước thì trả về luôn không thực hiện trừ quỹ kép
            if (voucher.Status == ExpenseVoucherStatus.Paid)
            {
                return (Voucher: MapVoucherResponse(voucher), Failure: (InsufficientStockException?)null);
            }

            // Tự động phê duyệt cho Chủ cửa hàng / Quản lý nếu phiếu đang ở Draft, PendingApproval hoặc Unresolved (1-click payment flow)
            if (voucher.Status == ExpenseVoucherStatus.Draft ||
                voucher.Status == ExpenseVoucherStatus.PendingApproval ||
                voucher.Status == ExpenseVoucherStatus.Unresolved)
            {
                if (voucher.IsFlaggedAsSuspiciousSplit && string.IsNullOrWhiteSpace(request.Note))
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
                    token
                );

                voucher.Status = ExpenseVoucherStatus.Paid;
                voucher.PaidAtUtc = DateTime.UtcNow;
            }
            catch (InsufficientStockException failure)
            {
                if (voucher.Status != ExpenseVoucherStatus.PaymentFailed)
                    await QueueNotificationAsync(voucher, BusinessNotificationTypes.ExpensePaymentFailed, "Phiếu chi chưa xuất được quỹ", token);
                voucher.Status = ExpenseVoucherStatus.PaymentFailed;
                await _unitOfWork.SaveChangesAsync(token);
                return (Voucher: MapVoucherResponse(voucher), Failure: failure);
            }

            await _unitOfWork.SaveChangesAsync(token);
            return (Voucher: MapVoucherResponse(voucher), Failure: (InsufficientStockException?)null);
        }, ct);

        if (result.Failure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(result.Failure);

        return result.Voucher;
    }

    private Task QueueNotificationAsync(ExpenseVoucher voucher, string type, string title, CancellationToken ct) =>
        _notificationOutbox?.EnqueueAsync(voucher.TenantId, voucher.Id, new BusinessNotification(
            type, title, $"Phiếu chi {voucher.VoucherCode}.",
            $"/store-owner/expenses?branchId={voucher.BranchId?.ToString() ?? "ALL"}&search={Uri.EscapeDataString(voucher.VoucherCode)}"), ct) ?? Task.CompletedTask;

    public async Task<ExpenseVoucherResponse> GetExpenseByIdAsync(Guid expenseId, CancellationToken ct = default)
    {
        RequireOwner();
        var voucher = await _expenseRepo.GetByIdAsync(expenseId, ct)
            ?? throw new NotFoundException("Không tìm thấy phiếu chi.");

        var branchNames = await ResolveBranchNamesAsync([voucher], ct);
        return MapVoucherResponse(voucher, branchNames);
    }

    public async Task<List<ExpenseVoucherResponse>> GetExpensesAsync(Guid? branchId, DateTime? fromDate, DateTime? toDate, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var paged = await GetExpensesPageAsync(branchId, fromDate, toDate, page, pageSize, null, null, null, null, ct);
        return paged.Items.ToList();
    }

    public async Task<PagedResult<ExpenseVoucherResponse>> GetExpensesPageAsync(
        Guid? branchId,
        DateTime? fromDate,
        DateTime? toDate,
        int page = 1,
        int pageSize = 50,
        string? keyword = null,
        string? category = null,
        string? status = null,
        string? quickStatus = null,
        CancellationToken ct = default)
    {
        RequireOwner();
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 200);

        var vouchers = await _expenseRepo.FindAsync(e =>
            (!branchId.HasValue || e.BranchId == branchId.Value) &&
            (!fromDate.HasValue || e.ExpenseDate >= fromDate.Value) &&
            (!toDate.HasValue || e.ExpenseDate <= toDate.Value) &&
            e.Nature != FinancialNature.TaxPayment &&
            e.Nature != FinancialNature.OwnerDraw &&
            e.Nature != FinancialNature.InternalTransfer, ct);

        IEnumerable<ExpenseVoucher> query = vouchers;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var q = keyword.Trim();
            query = query.Where(v =>
                (v.VoucherCode != null && v.VoucherCode.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (v.InvoiceNumber != null && v.InvoiceNumber.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (v.BeneficiaryName != null && v.BeneficiaryName.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (v.Note != null && v.Note.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var cat = category.Trim();
            query = query.Where(v => string.Equals(v.Category, cat, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = status.Trim();
            if (string.Equals(st, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(v =>
                    v.Status != ExpenseVoucherStatus.Paid &&
                    v.Status != ExpenseVoucherStatus.Cancelled &&
                    v.Status != ExpenseVoucherStatus.Reversed);
            }
            else if (string.Equals(st, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(v =>
                    v.Status == ExpenseVoucherStatus.Cancelled ||
                    v.Status == ExpenseVoucherStatus.Reversed);
            }
            else
            {
                query = query.Where(v => string.Equals(v.Status.ToString(), st, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (!string.IsNullOrWhiteSpace(quickStatus) && !string.Equals(quickStatus, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var qs = quickStatus.Trim().ToUpperInvariant();
            if (qs == "PENDING_APPROVAL")
            {
                query = query.Where(v =>
                    v.Status == ExpenseVoucherStatus.PendingApproval ||
                    v.Status == ExpenseVoucherStatus.Draft ||
                    v.Status == ExpenseVoucherStatus.Unresolved ||
                    (v.Status != ExpenseVoucherStatus.Paid && v.Status != ExpenseVoucherStatus.Cancelled && v.Status != ExpenseVoucherStatus.Reversed && v.IsFlaggedAsSuspiciousSplit));
            }
            else if (qs == "PENDING_PAYMENT")
            {
                query = query.Where(v =>
                    v.Status != ExpenseVoucherStatus.Paid &&
                    v.Status != ExpenseVoucherStatus.Cancelled &&
                    v.Status != ExpenseVoucherStatus.Reversed);
            }
            else if (qs == "PAID")
            {
                query = query.Where(v => v.Status == ExpenseVoucherStatus.Paid);
            }
            else if (qs == "SUSPICIOUS")
            {
                query = query.Where(v => v.IsFlaggedAsSuspiciousSplit);
            }
        }

        var ordered = query.OrderByDescending(e => e.CreatedAt).ToList();
        var pageEntities = ordered
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToList();
        var branchNames = await ResolveBranchNamesAsync(pageEntities, ct);
        var items = pageEntities
            .Select(v => MapVoucherResponse(v, branchNames))
            .ToList();

        return new PagedResult<ExpenseVoucherResponse>
        {
            Items = items,
            Page = safePage,
            PageSize = safePageSize,
            TotalItems = ordered.Count,
        };
    }

    public async Task<ExpenseSummaryResponse> GetExpenseSummaryAsync(Guid? branchId, DateTime? fromDate, DateTime? toDate, CancellationToken ct = default)
    {
        RequireOwner();
        var vouchers = await _expenseRepo.FindAsync(e =>
            (!branchId.HasValue || e.BranchId == branchId.Value) &&
            (!fromDate.HasValue || e.ExpenseDate >= fromDate.Value) &&
            (!toDate.HasValue || e.ExpenseDate <= toDate.Value) &&
            e.Nature != FinancialNature.TaxPayment &&
            e.Nature != FinancialNature.OwnerDraw &&
            e.Nature != FinancialNature.InternalTransfer, ct);

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

        var todayVn = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var overdueCount = pendingVouchers.Count(v => v.DueDate.HasValue && v.DueDate.Value < todayVn);

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
            NonTaxDeductibleCount: nonTaxDeductibleVouchers.Count,
            OverdueCount: overdueCount
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

        var list = vouchers.OrderByDescending(e => e.CreatedAt).ToList();
        var branchNames = await ResolveBranchNamesAsync(list, ct);
        return list
            .Select(v => MapVoucherResponse(v, branchNames))
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

    private async Task<Dictionary<Guid, string>> ResolveBranchNamesAsync(IEnumerable<ExpenseVoucher> vouchers, CancellationToken ct)
    {
        var ids = vouchers.Where(v => v.BranchId.HasValue).Select(v => v.BranchId!.Value).Distinct().ToList();
        return ids.Count == 0
            ? new Dictionary<Guid, string>()
            : await _expenseRepo.GetBranchNamesAsync(ids, ct);
    }

    private static ExpenseVoucherResponse MapVoucherResponse(ExpenseVoucher v) => MapVoucherResponse(v, null);

    private static ExpenseVoucherResponse MapVoucherResponse(ExpenseVoucher v, Dictionary<Guid, string>? branchNames)
    {
        string? branchName = null;
        if (v.BranchId.HasValue && branchNames != null && branchNames.TryGetValue(v.BranchId.Value, out var resolved))
        {
            branchName = resolved;
        }
        return new(v.Id, v.VoucherCode, v.BranchId, v.BeneficiaryName, v.Category, v.Amount, v.Status.ToString(), v.PaymentSourceType.ToString(), v.PaymentSourceAccountId, v.IsTaxDeductible, v.InvoiceNumber, v.SellerTaxCode, v.EvidenceAttachmentUrl, v.IsFlaggedAsSuspiciousSplit, v.SuspiciousReason, v.ApprovedByUserId, v.ApprovedAtUtc, v.PaidAtUtc, v.ExpenseDate, v.Note, v.Nature, v.ReferenceCode, v.RefundedAmount, v.IsSettled, v.DueDate, branchName);
    }
}
