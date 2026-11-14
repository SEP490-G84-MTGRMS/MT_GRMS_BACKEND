using MT_GRMS.Application.BusinessDays.Dtos;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using System.Security.Cryptography;
using System.Text;

namespace MT_GRMS.Application.BusinessDays;

public class BusinessDayClosingService : IBusinessDayClosingService
{
    private readonly IBusinessDayClosingRepository _repository;
    private readonly ICashbookService _cashbook;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public BusinessDayClosingService(
        IBusinessDayClosingRepository repository,
        ICashbookService cashbook,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext)
    {
        _repository = repository;
        _cashbook = cashbook;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<BusinessDateResponse> GetCurrentBusinessDateAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        EnsureOwnerOrAdmin();
        var tenantId = _tenantContext.CurrentTenantId;
        if (branchId == Guid.Empty || !await _repository.IsBranchAccessibleAsync(tenantId, branchId, cancellationToken))
            throw new BusinessException("BRANCH_NOT_FOUND", "Không tìm thấy chi nhánh trong phạm vi được phép.");
        var startTime = await _repository.GetBusinessDayStartTimeAsync(tenantId, branchId, cancellationToken);
        return new BusinessDateResponse(branchId, BusinessDateResolver.Resolve(DateTime.UtcNow, startTime), startTime);
    }

    public async Task<BusinessDayClosingPreview> PreviewAsync(Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default)
    {
        EnsureOwnerOrAdmin();
        var tenantId = _tenantContext.CurrentTenantId;
        if (branchId == Guid.Empty || !await _repository.IsBranchAccessibleAsync(tenantId, branchId, cancellationToken))
            throw new BusinessException("BRANCH_NOT_FOUND", "Không tìm thấy chi nhánh trong phạm vi được phép.");
        if (businessDate == default)
            throw new BusinessException("BUSINESS_DATE_REQUIRED", "Ngày nghiệp vụ là bắt buộc.");

        var startTime = await _repository.GetBusinessDayStartTimeAsync(tenantId, branchId, cancellationToken);
        var shifts = await _repository.GetShiftsAsync(tenantId, branchId, businessDate, cancellationToken);
        var orders = await _repository.GetShiftlessOrdersAsync(tenantId, branchId, businessDate, cancellationToken);
        var funds = await _repository.GetFundsAsync(tenantId, branchId, cancellationToken);
        var blockers = new List<BusinessDayBlocker>();

        foreach (var shift in shifts)
        {
            if (shift.Status is ShiftState.Open or ShiftState.Closing)
                blockers.Add(new("OPEN_SHIFTS_EXIST", "Shift", shift.Id.ToString(), shift.ShiftCode, null, shift.Status.ToString(), $"/store-owner/shifts?shiftId={shift.Id}"));
            if (shift.HandoverStatus != HandoverStatus.CONFIRMED)
                blockers.Add(new("UNCONFIRMED_HANDOVER", "Shift", shift.Id.ToString(), shift.ShiftCode, shift.DroppedToSafeAmount, shift.HandoverStatus.ToString(), $"/store-owner/shifts?shiftId={shift.Id}"));
            foreach (var movement in shift.CashMovements.Where(x => x.Status == CashMovementStatus.PendingApproval))
                blockers.Add(new("PENDING_CASH_MOVEMENT", "CashMovement", movement.Id.ToString(), movement.Reason, movement.Amount, movement.Status.ToString(), "/store-owner/pos-cash-control"));
        }
        if (await _repository.HasPendingPaymentOrdersAsync(tenantId, branchId, businessDate, cancellationToken))
            blockers.Add(new("PENDING_PAYMENT_ORDERS", "Order", businessDate.ToString("yyyy-MM-dd"), "Đơn hàng chờ thanh toán", null, "PendingPayment", "/store-owner/invoices"));

        var completed = orders.Where(o => o.Status == "Completed" && o.PaymentMethod != "DEBT").ToList();
        var ownerCash = completed.Where(o => o.PaymentMethod == "CASH").Sum(o => o.FinalAmount);
        var ownerBank = completed.Where(o => IsBank(o.PaymentMethod)).Sum(o => o.FinalAmount);
        var cashIn = shifts.SelectMany(s => s.CashMovements).Where(IsEffectiveIn).Sum(m => m.Amount);
        var cashOut = shifts.SelectMany(s => s.CashMovements).Where(IsEffectiveOut).Sum(m => m.Amount);
        var fundDtos = funds.Select(f => new BusinessDayFundPreview(f.Id, f.AccountName, f.AccountType.ToString(), f.CurrentBalance, f.AccountType != FinanceAccountType.Bank)).ToList();

        var localStart = businessDate.ToDateTime(TimeOnly.FromTimeSpan(startTime), DateTimeKind.Unspecified);
        var periodStartUtc = DateTime.SpecifyKind(localStart.AddHours(-7), DateTimeKind.Utc);
        var periodEndUtc = periodStartUtc.AddDays(1);
        var tokenSource = string.Join('|', shifts.OrderBy(s => s.Id).Select(s =>
                $"S:{s.Id}:{s.Status}:{s.HandoverStatus}:{s.CashbookPostingStatus}:{s.SnapshotLocked}:{s.TotalCashSales}:{s.TotalBankRevenue}:{s.RetainedFloatAmount}:{s.DroppedToSafeAmount}:{s.UpdatedAt:O}"))
            + "|" + string.Join('|', shifts.SelectMany(s => s.CashMovements).OrderBy(m => m.Id).Select(m =>
                $"M:{m.Id}:{m.MovementType}:{m.Amount}:{m.Status}:{m.PostedAtUtc:O}:{m.UpdatedAt:O}"))
            + "|" + string.Join('|', completed.OrderBy(o => o.Id).Select(o =>
                $"O:{o.Id}:{o.Status}:{o.PaymentMethod}:{o.FinalAmount}:{o.UpdatedAt:O}"))
            + "|" + string.Join('|', funds.OrderBy(f => f.Id).Select(f =>
                $"F:{f.Id}:{f.AccountType}:{f.CurrentBalance}:{f.IsActive}:{f.UpdatedAt:O}"));
        var previewToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenSource)));

        return new BusinessDayClosingPreview(
            branchId, businessDate, periodStartUtc, periodEndUtc,
            shifts.Sum(s => s.TotalCashSales) + ownerCash,
            shifts.Sum(s => s.TotalBankRevenue) + ownerBank,
            cashIn, cashOut,
            shifts.Sum(s => s.RetainedFloatAmount),
            shifts.Sum(s => s.DroppedToSafeAmount),
            fundDtos, blockers, previewToken);
    }

    public async Task<BusinessDayClosingResponse> CloseAsync(CloseBusinessDayRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.CurrentUserId == Guid.Empty)
            throw new UnauthorizedAccessException("Không xác định được người thực hiện khóa sổ.");
        if (!IsOwnerOrAdmin(_tenantContext.CurrentRole))
            throw new UnauthorizedAccessException("Chỉ Chủ cửa hàng hoặc Admin được chốt doanh thu ngày.");
        if (request.BranchId == Guid.Empty)
            throw new BusinessException("BRANCH_REQUIRED", "Chi nhánh là bắt buộc.");
        if (request.BusinessDate == default)
            throw new BusinessException("BUSINESS_DATE_REQUIRED", "Ngày nghiệp vụ là bắt buộc.");
        if (request.FundCounts == null)
            throw new BusinessException("FUND_COUNTS_REQUIRED", "Phải nhập kết quả kiểm đếm theo quỹ.");
        if (request.FundCounts.Any(x => x.FundId == Guid.Empty || x.ActualCashCounted < 0 || x.ActualCashCounted > 999_999_999_999m))
            throw new BusinessException("INVALID_FUND_COUNT", "Kết quả kiểm đếm theo quỹ không hợp lệ.");
        if (request.FundCounts.Select(x => x.FundId).Distinct().Count() != request.FundCounts.Count)
            throw new BusinessException("DUPLICATE_FUND_COUNT", "Mỗi quỹ chỉ được kiểm đếm một lần.");
        if (string.IsNullOrWhiteSpace(request.PreviewToken))
            throw new BusinessException("PREVIEW_TOKEN_REQUIRED", "Cần tải số liệu tạm tính trước khi khóa sổ.");
        if (request.Note?.Length > 500)
            throw new BusinessException("NOTE_TOO_LONG", "Ghi chú tối đa 500 ký tự.");

        var tenantId = _tenantContext.CurrentTenantId;
        if (!await _repository.IsBranchAccessibleAsync(tenantId, request.BranchId, cancellationToken))
            throw new BusinessException("BRANCH_NOT_FOUND", "Không tìm thấy chi nhánh trong phạm vi được phép.");

        var existing = await _repository.GetAsync(tenantId, request.BranchId, request.BusinessDate, cancellationToken);
        if (existing != null)
            throw new BusinessException("BUSINESS_DAY_ALREADY_CLOSED", "Ngày nghiệp vụ này đã được khóa sổ hoặc đang được xử lý.");

        var previousClosing = await _repository.GetLatestAsync(tenantId, request.BranchId, request.BusinessDate, cancellationToken);
        var version = (previousClosing?.Version ?? 0) + 1;

        var currentPreview = await PreviewAsync(request.BranchId, request.BusinessDate, cancellationToken);
        if (currentPreview.Blockers.Count > 0)
            throw new BusinessException(currentPreview.Blockers[0].Code, "Vẫn còn việc phải xử lý trước khi khóa sổ.");
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(currentPreview.PreviewToken),
                Encoding.UTF8.GetBytes(request.PreviewToken.Trim().ToUpperInvariant())))
            throw new BusinessException("STALE_PREVIEW", "Số liệu đã thay đổi. Vui lòng tải lại trước khi khóa sổ.");

        var activeFunds = await _repository.GetFundsAsync(tenantId, request.BranchId, cancellationToken);
        var activeFundIds = activeFunds.Select(x => x.Id).ToHashSet();
        if (request.FundCounts.Any(x => !activeFundIds.Contains(x.FundId)))
            throw new BusinessException("FUND_NOT_FOUND", "Quỹ kiểm đếm không thuộc chi nhánh hoặc đã ngừng hoạt động.");
        var requiredFundIds = currentPreview.Funds.Where(x => x.RequiresPhysicalCount).Select(x => x.FundId).ToHashSet();
        if (!requiredFundIds.SetEquals(request.FundCounts.Select(x => x.FundId)))
            throw new BusinessException("INCOMPLETE_FUND_COUNTS", "Phải kiểm đếm đủ tất cả quỹ tiền mặt đang hoạt động.");

        var shifts = await _repository.GetShiftsAsync(tenantId, request.BranchId, request.BusinessDate, cancellationToken);
        if (shifts.Any(s => s.Status is ShiftState.Open or ShiftState.Closing))
            throw new BusinessException("OPEN_SHIFTS_EXIST", "Vẫn còn ca đang mở hoặc đang kiểm đếm.");
        if (shifts.Any(s => s.HandoverStatus != HandoverStatus.CONFIRMED))
            throw new BusinessException("UNCONFIRMED_HANDOVERS", "Tất cả ca phải được Chủ cửa hàng xác nhận bàn giao.");
        if (shifts.SelectMany(s => s.CashMovements).Any(m => m.Status == CashMovementStatus.PendingApproval))
            throw new BusinessException("PENDING_CASH_MOVEMENTS", "Vẫn còn phiếu thu/chi đang chờ duyệt.");
        if (shifts.Any(s => !s.SnapshotLocked))
            throw new BusinessException("SHIFT_SNAPSHOT_NOT_LOCKED", "Tất cả ca phải có snapshot đã khóa.");
        if (await _repository.HasPendingPaymentOrdersAsync(tenantId, request.BranchId, request.BusinessDate, cancellationToken))
            throw new BusinessException("PENDING_PAYMENT_ORDERS", "Vẫn còn đơn PendingPayment chưa xử lý.");

        var shiftlessOrders = await _repository.GetShiftlessOrdersAsync(tenantId, request.BranchId, request.BusinessDate, cancellationToken);
        var completedShiftless = shiftlessOrders.Where(o => o.Status == "Completed" && o.PaymentMethod != "DEBT").ToList();
        var ownerCash = completedShiftless.Where(o => o.PaymentMethod == "CASH").Sum(o => o.FinalAmount);
        var ownerBank = completedShiftless.Where(o => IsBank(o.PaymentMethod)).Sum(o => o.FinalAmount);
        var shiftCash = shifts.Sum(s => s.TotalCashSales);
        var shiftBank = shifts.Sum(s => s.TotalBankRevenue);
        var cashIn = shifts.SelectMany(s => s.CashMovements).Where(IsEffectiveIn).Sum(m => m.Amount);
        var cashOut = shifts.SelectMany(s => s.CashMovements).Where(IsEffectiveOut).Sum(m => m.Amount);
        var expectedCash = shiftCash + ownerCash + cashIn - cashOut;
        var actualCashCounted = request.FundCounts.Sum(x => x.ActualCashCounted);
        var discrepancy = actualCashCounted - expectedCash;
        var now = DateTime.UtcNow;
        var reference = $"EOD-{request.BusinessDate:yyyyMMdd}-{request.BranchId.ToString("N")[..8].ToUpperInvariant()}-V{version}";
        var startTime = await _repository.GetBusinessDayStartTimeAsync(tenantId, request.BranchId, cancellationToken);

        var closing = new BusinessDayClosing
        {
            Id = Guid.NewGuid(), TenantId = _tenantContext.CurrentTenantId, BranchId = request.BranchId,
            BusinessDate = request.BusinessDate, BusinessDayStartTime = startTime,
            Version = version, IsActive = true, SupersedesClosingId = previousClosing?.Id,
            PreviewToken = currentPreview.PreviewToken,
            TotalShifts = shifts.Count, TotalOrders = shifts.Sum(s => s.TotalOrdersCount) + completedShiftless.Count,
            TotalCashSales = shiftCash + ownerCash, TotalBankSales = shiftBank + ownerBank,
            OwnerShiftlessCashSales = ownerCash, OwnerShiftlessBankSales = ownerBank,
            TotalCashIn = cashIn, TotalCashOut = cashOut,
            TotalRetainedFloat = shifts.Sum(s => s.RetainedFloatAmount),
            TotalDroppedToSafe = shifts.Sum(s => s.DroppedToSafeAmount),
            TotalDiscrepancy = shifts.Sum(s => s.Discrepancy ?? 0m) + discrepancy,
            ClosedByUserId = _tenantContext.CurrentUserId, Note = request.Note?.Trim(),
            ReferenceCode = reference, Status = BusinessDayClosingStatus.Closing
        };

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _repository.AddAsync(closing, cancellationToken);
            var safe = await _cashbook.GetOrCreateSafeAsync(request.BranchId, cancellationToken);
            var bank = await _cashbook.GetOrCreateBankAccountAsync(request.BranchId, ct: cancellationToken);
            // Ca đã Posted chỉ được tổng hợp vào snapshot, không ghi Cashbook lần hai.
            var unpostedShiftCash = shifts.Where(s => s.CashbookPostingStatus != CashbookPostingStatus.Posted).Sum(s => s.TotalCashSales);
            var unpostedShiftBank = shifts.Where(s => s.CashbookPostingStatus != CashbookPostingStatus.Posted).Sum(s => s.TotalBankRevenue);
            var unpostedCashIn = shifts.SelectMany(s => s.CashMovements).Where(IsPostableIn).Sum(m => m.Amount);
            var unpostedCashOut = shifts.SelectMany(s => s.CashMovements).Where(IsPostableOut).Sum(m => m.Amount);
            var cashToPost = ownerCash + unpostedShiftCash;
            var bankToPost = ownerBank + unpostedShiftBank;
            if (cashToPost > 0)
                await _cashbook.ReceiveCashAsync(safe.Id, FinanceAccountType.Safe, cashToPost, "BUSINESS_DAY_CLOSE", closing.Id, reference + "-CASH", "Thu doanh thu tiền mặt chưa kết toán của ngày nghiệp vụ", cancellationToken);
            if (bankToPost > 0)
                await _cashbook.ReceiveCashAsync(bank.Id, FinanceAccountType.Bank, bankToPost, "BUSINESS_DAY_CLOSE", closing.Id, reference + "-BANK", "Thu doanh thu chuyển khoản/VietQR chưa kết toán của ngày nghiệp vụ", cancellationToken);
            if (unpostedCashIn > 0)
                await _cashbook.ReceiveCashAsync(safe.Id, FinanceAccountType.Safe, unpostedCashIn, "BUSINESS_DAY_CLOSE", closing.Id, reference + "-IN", "Tổng thu khác đã duyệt trong ngày", cancellationToken);
            if (unpostedCashOut > 0)
                await _cashbook.DisburseCashAsync(safe.Id, FinanceAccountType.Safe, unpostedCashOut, "BUSINESS_DAY_CLOSE", closing.Id, reference + "-OUT", "Tổng chi đã duyệt trong ngày", cancellationToken);

            foreach (var shift in shifts)
            {
                shift.CashbookPostingStatus = CashbookPostingStatus.Posted;
                shift.BusinessDayClosingId = closing.Id;
                foreach (var movement in shift.CashMovements.Where(m => IsPostableIn(m) || IsPostableOut(m)))
                    movement.PostedAtUtc = now;
            }
            foreach (var order in completedShiftless) order.BusinessDayClosingId = closing.Id;
            closing.Status = BusinessDayClosingStatus.Closed;
            closing.ClosedAtUtc = now;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        return new BusinessDayClosingResponse(closing.Id, closing.BranchId, closing.BusinessDate, closing.Status.ToString(),
            closing.TotalShifts, closing.TotalOrders, closing.TotalCashSales, closing.TotalBankSales,
            ownerCash, ownerBank, cashIn, cashOut, closing.TotalRetainedFloat, closing.TotalDroppedToSafe,
            expectedCash, actualCashCounted, discrepancy, reference, now);
    }

    public async Task<IReadOnlyList<BusinessDayClosingVersionResponse>> GetHistoryAsync(
        Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default)
    {
        EnsureOwnerOrAdmin();
        var tenantId = _tenantContext.CurrentTenantId;
        if (branchId == Guid.Empty || !await _repository.IsBranchAccessibleAsync(tenantId, branchId, cancellationToken))
            throw new BusinessException("BRANCH_NOT_FOUND", "Không tìm thấy chi nhánh trong phạm vi được phép.");
        if (businessDate == default)
            throw new BusinessException("BUSINESS_DATE_REQUIRED", "Ngày nghiệp vụ là bắt buộc.");
        var history = await _repository.GetHistoryAsync(tenantId, branchId, businessDate, cancellationToken);
        return history.Select(ToVersionResponse).ToList();
    }

    public async Task<BusinessDayClosingVersionResponse> ReopenAsync(
        Guid closingId, ReopenBusinessDayRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOwnerOrAdmin();
        if (closingId == Guid.Empty)
            throw new BusinessException("BUSINESS_DAY_CLOSING_NOT_FOUND", "Không tìm thấy phiên chốt ngày nghiệp vụ.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BusinessException("REOPEN_REASON_REQUIRED", "Vui lòng nhập lý do mở lại ngày nghiệp vụ.");
        if (request.Reason.Trim().Length > 500)
            throw new BusinessException("REOPEN_REASON_TOO_LONG", "Lý do mở lại tối đa 500 ký tự.");

        var closing = await _repository.GetByIdAsync(_tenantContext.CurrentTenantId, closingId, cancellationToken);
        if (closing == null)
            throw new BusinessException("BUSINESS_DAY_CLOSING_NOT_FOUND", "Không tìm thấy phiên chốt ngày nghiệp vụ.");
        if (!closing.IsActive || closing.Status != BusinessDayClosingStatus.Closed)
            throw new BusinessException("BUSINESS_DAY_ALREADY_REOPENED", "Ngày nghiệp vụ này đã được mở lại hoặc không còn là phiên chốt hiện hành.");

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            closing.Status = BusinessDayClosingStatus.Reopened;
            closing.IsActive = false;
            closing.ReopenedAtUtc = DateTime.UtcNow;
            closing.ReopenedByUserId = _tenantContext.CurrentUserId;
            closing.ReopenReason = request.Reason.Trim();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
        return ToVersionResponse(closing);
    }

    private static BusinessDayClosingVersionResponse ToVersionResponse(BusinessDayClosing closing) => new(
        closing.Id, closing.BranchId, closing.BusinessDate, closing.Version, closing.Status.ToString(), closing.IsActive,
        closing.SupersedesClosingId, closing.ReferenceCode, closing.TotalCashSales, closing.TotalBankSales,
        closing.TotalCashIn, closing.TotalCashOut, closing.TotalDiscrepancy, closing.ClosedAtUtc,
        closing.ReopenedAtUtc, closing.ReopenedByUserId, closing.ReopenReason);

    private static bool IsOwnerOrAdmin(string role) => role.Equals("STORE_OWNER", StringComparison.OrdinalIgnoreCase) || role.Equals("ADMIN", StringComparison.OrdinalIgnoreCase);
    private void EnsureOwnerOrAdmin()
    {
        if (_tenantContext.CurrentUserId == Guid.Empty)
            throw new UnauthorizedAccessException("Không xác định được người thực hiện.");
        if (!IsOwnerOrAdmin(_tenantContext.CurrentRole))
            throw new UnauthorizedAccessException("Chỉ Chủ cửa hàng hoặc Admin được truy cập chốt ngày.");
    }
    private static bool IsBank(string method) => method is "VIETQR" or "BANK" or "TRANSFER";
    private static bool IsEffective(ShiftCashMovement m) => m.Status == CashMovementStatus.Recorded;
    private static bool IsEffectiveIn(ShiftCashMovement m) => IsEffective(m) && m.MovementType == CashMovementType.IN;
    private static bool IsEffectiveOut(ShiftCashMovement m) => IsEffective(m) && m.MovementType == CashMovementType.OUT;
    private static bool IsPostableIn(ShiftCashMovement m) => IsEffectiveIn(m) && m.PostedAtUtc == null;
    private static bool IsPostableOut(ShiftCashMovement m) => IsEffectiveOut(m) && m.PostedAtUtc == null;
}
