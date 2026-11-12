using MT_GRMS.Application.BusinessDays.Dtos;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

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
        if (request.ActualCashCounted < 0)
            throw new BusinessException("INVALID_ACTUAL_CASH", "Tiền thực đếm không được âm.");
        if (request.Note?.Length > 500)
            throw new BusinessException("NOTE_TOO_LONG", "Ghi chú tối đa 500 ký tự.");

        var existing = await _repository.GetAsync(request.BranchId, request.BusinessDate, cancellationToken);
        if (existing != null)
            throw new BusinessException("BUSINESS_DAY_ALREADY_CLOSED", "Ngày nghiệp vụ này đã được khóa sổ hoặc đang được xử lý.");

        var shifts = await _repository.GetShiftsAsync(request.BranchId, request.BusinessDate, cancellationToken);
        if (shifts.Any(s => s.Status is ShiftState.Open or ShiftState.Closing))
            throw new BusinessException("OPEN_SHIFTS_EXIST", "Vẫn còn ca đang mở hoặc đang kiểm đếm.");
        if (shifts.Any(s => s.HandoverStatus != HandoverStatus.CONFIRMED))
            throw new BusinessException("UNCONFIRMED_HANDOVERS", "Tất cả ca phải được Chủ cửa hàng xác nhận bàn giao.");
        if (shifts.SelectMany(s => s.CashMovements).Any(m => m.Status == CashMovementStatus.PendingApproval))
            throw new BusinessException("PENDING_CASH_MOVEMENTS", "Vẫn còn phiếu thu/chi đang chờ duyệt.");
        if (shifts.Any(s => !s.SnapshotLocked))
            throw new BusinessException("SHIFT_SNAPSHOT_NOT_LOCKED", "Tất cả ca phải có snapshot đã khóa.");
        if (await _repository.HasPendingPaymentOrdersAsync(request.BranchId, request.BusinessDate, cancellationToken))
            throw new BusinessException("PENDING_PAYMENT_ORDERS", "Vẫn còn đơn PendingPayment chưa xử lý.");

        var shiftlessOrders = await _repository.GetShiftlessOrdersAsync(request.BranchId, request.BusinessDate, cancellationToken);
        var completedShiftless = shiftlessOrders.Where(o => o.Status == "Completed" && o.PaymentMethod != "DEBT").ToList();
        var ownerCash = completedShiftless.Where(o => o.PaymentMethod == "CASH").Sum(o => o.FinalAmount);
        var ownerBank = completedShiftless.Where(o => IsBank(o.PaymentMethod)).Sum(o => o.FinalAmount);
        var shiftCash = shifts.Sum(s => s.TotalCashSales);
        var shiftBank = shifts.Sum(s => s.TotalBankRevenue);
        var cashIn = shifts.SelectMany(s => s.CashMovements).Where(IsPostableIn).Sum(m => m.Amount);
        var cashOut = shifts.SelectMany(s => s.CashMovements).Where(IsPostableOut).Sum(m => m.Amount);
        var expectedCash = shiftCash + ownerCash + cashIn - cashOut;
        var discrepancy = request.ActualCashCounted - expectedCash;
        var now = DateTime.UtcNow;
        var reference = $"EOD-{request.BusinessDate:yyyyMMdd}-{request.BranchId.ToString("N")[..8].ToUpperInvariant()}";
        var startTime = await _repository.GetBusinessDayStartTimeAsync(request.BranchId, cancellationToken);

        var closing = new BusinessDayClosing
        {
            Id = Guid.NewGuid(), TenantId = _tenantContext.CurrentTenantId, BranchId = request.BranchId,
            BusinessDate = request.BusinessDate, BusinessDayStartTime = startTime,
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
            if (closing.TotalCashSales > 0)
                await _cashbook.ReceiveCashAsync(safe.Id, FinanceAccountType.Safe, closing.TotalCashSales, "BUSINESS_DAY_CLOSE", closing.Id, reference + "-CASH", "Thu doanh thu tiền mặt ngày nghiệp vụ", cancellationToken);
            if (closing.TotalBankSales > 0)
                await _cashbook.ReceiveCashAsync(bank.Id, FinanceAccountType.Bank, closing.TotalBankSales, "BUSINESS_DAY_CLOSE", closing.Id, reference + "-BANK", "Thu doanh thu chuyển khoản/VietQR ngày nghiệp vụ", cancellationToken);
            if (cashIn > 0)
                await _cashbook.ReceiveCashAsync(safe.Id, FinanceAccountType.Safe, cashIn, "BUSINESS_DAY_CLOSE", closing.Id, reference + "-IN", "Tổng thu khác đã duyệt trong ngày", cancellationToken);
            if (cashOut > 0)
                await _cashbook.DisburseCashAsync(safe.Id, FinanceAccountType.Safe, cashOut, "BUSINESS_DAY_CLOSE", closing.Id, reference + "-OUT", "Tổng chi đã duyệt trong ngày", cancellationToken);

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
            expectedCash, request.ActualCashCounted, discrepancy, reference, now);
    }

    private static bool IsOwnerOrAdmin(string role) => role.Equals("STORE_OWNER", StringComparison.OrdinalIgnoreCase) || role.Equals("ADMIN", StringComparison.OrdinalIgnoreCase);
    private static bool IsBank(string method) => method is "VIETQR" or "BANK" or "TRANSFER";
    private static bool IsApproved(ShiftCashMovement m) => m.Status == CashMovementStatus.Recorded && m.PostedAtUtc == null;
    private static bool IsPostableIn(ShiftCashMovement m) => IsApproved(m) && m.MovementType == CashMovementType.IN;
    private static bool IsPostableOut(ShiftCashMovement m) => IsApproved(m) && m.MovementType == CashMovementType.OUT;
}
