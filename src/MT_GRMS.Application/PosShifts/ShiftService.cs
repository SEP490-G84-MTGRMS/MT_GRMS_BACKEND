using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PosShifts.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.PosShifts;

public class ShiftService : IShiftService
{
    private readonly IShiftRepository _shiftRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public ShiftService(
        IShiftRepository shiftRepository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext)
    {
        _shiftRepository = shiftRepository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<ShiftDetailResponse> OpenShiftAsync(
        OpenShiftRequest request,
        Guid cashierUserId,
        CancellationToken cancellationToken = default)
    {
        if (request.OpeningFloat < 0)
        {
            throw new BusinessException("INVALID_OPENING_FLOAT", "Số tiền mặt ban đầu không được âm.");
        }

        // 1. Kiểm tra xem quầy/chi nhánh đã có ca nào đang mở chưa
        var activeShift = await _shiftRepository.GetActiveShiftAsync(request.BranchId, request.TillId, cancellationToken);
        if (activeShift != null)
        {
            throw new BusinessException(
                "SHIFT_ALREADY_OPEN",
                $"Quầy này đang có ca làm việc '{activeShift.ShiftName}' ({activeShift.ShiftCode}) đang mở. Vui lòng chốt ca trước khi mở ca mới.");
        }

        // 2. Tìm ca đã đóng liền trước đó trên cùng quầy/chi nhánh để lấy số tiền bàn giao dự kiến
        var prevShift = await _shiftRepository.GetLatestClosedShiftAsync(request.BranchId, request.TillId, cancellationToken);
        decimal openingFloatExpected = prevShift?.RetainedFloatAmount ?? request.OpeningFloat;
        decimal openingDiscrepancy = request.OpeningFloat - openingFloatExpected;

        // 3. Sinh mã ca tự động
        var shiftCode = await _shiftRepository.GenerateNextShiftCodeAsync(request.BranchId, cancellationToken);

        var shift = new ShiftZReport
        {
            TenantId = _tenantContext.CurrentTenantId,
            BranchId = request.BranchId,
            TillId = request.TillId,
            ShiftCode = shiftCode,
            ShiftName = string.IsNullOrWhiteSpace(request.ShiftName) ? $"Ca {DateTime.UtcNow:dd/MM/yyyy HH:mm}" : request.ShiftName.Trim(),
            CashierUserId = cashierUserId,
            PrevShiftId = prevShift?.Id,
            OpenedAtUtc = DateTime.UtcNow,
            OpeningFloatExpected = openingFloatExpected,
            OpeningFloat = request.OpeningFloat,
            OpeningDiscrepancy = openingDiscrepancy,
            TotalCashIn = 0.00m,
            TotalCashOut = 0.00m,
            TotalBankRevenue = 0.00m,
            TotalOrdersCount = 0,
            ExpectedCash = request.OpeningFloat,
            RetainedFloatAmount = 0.00m,
            DroppedToSafeAmount = 0.00m,
            HandoverStatus = HandoverStatus.PENDING,
            Status = ShiftState.Open,
            Notes = request.Notes
        };

        await _shiftRepository.AddShiftAsync(shift, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDetailResponse(shift);
    }

    public async Task<ShiftDetailResponse?> GetCurrentShiftAsync(
        Guid branchId,
        Guid? tillId = null,
        CancellationToken cancellationToken = default)
    {
        var shift = await _shiftRepository.GetActiveShiftAsync(branchId, tillId, cancellationToken);
        return shift == null ? null : MapToDetailResponse(shift);
    }

    public async Task<ShiftDetailResponse?> GetShiftByIdAsync(
        Guid shiftId,
        CancellationToken cancellationToken = default)
    {
        var shift = await _shiftRepository.GetByIdAsync(shiftId, cancellationToken);
        return shift == null ? null : MapToDetailResponse(shift);
    }

    public async Task<ShiftCashMovementDto> AddCashMovementAsync(
        Guid shiftId,
        CashMovementRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền thu chi két phải lớn hơn 0.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new BusinessException("REQUIRED_REASON", "Lý do thu chi két không được để trống.");
        }

        var shift = await _shiftRepository.GetByIdAsync(shiftId, cancellationToken);
        if (shift == null)
        {
            throw new BusinessException("SHIFT_NOT_FOUND", "Không tìm thấy ca làm việc.");
        }

        if (shift.Status != ShiftState.Open)
        {
            throw new BusinessException("SHIFT_NOT_OPEN", "Chỉ có thể ghi nhận phiếu thu/chi khi ca đang mở.");
        }

        var movement = new ShiftCashMovement
        {
            TenantId = _tenantContext.CurrentTenantId,
            ShiftId = shiftId,
            MovementType = request.MovementType,
            Category = request.Category,
            Amount = request.Amount,
            Reason = request.Reason.Trim(),
            CreatedByName = request.CreatedByName,
            OccurredAtUtc = DateTime.UtcNow
        };

        if (request.MovementType == CashMovementType.IN)
        {
            shift.TotalCashIn += request.Amount;
        }
        else
        {
            shift.TotalCashOut += request.Amount;
        }

        // Cập nhật lại ExpectedCash tạm tính
        var (cashSales, _, _) = await _shiftRepository.GetShiftSalesMetricsAsync(shiftId, cancellationToken);
        shift.ExpectedCash = shift.OpeningFloat + cashSales + shift.TotalCashIn - shift.TotalCashOut;

        await _shiftRepository.AddCashMovementAsync(movement, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ShiftCashMovementDto(
            movement.Id,
            movement.ShiftId,
            movement.MovementType,
            movement.Category,
            movement.Amount,
            movement.Reason,
            movement.CreatedByName,
            movement.OccurredAtUtc
        );
    }

    public async Task<ShiftDetailResponse> CloseShiftAsync(
        Guid shiftId,
        CloseShiftRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ActualCashCounted < 0)
        {
            throw new BusinessException("INVALID_ACTUAL_CASH", "Tiền kiểm đếm thực tế không được âm.");
        }

        // Bắt buộc Invariant: RetainedFloat + DroppedToSafe == ActualCashCounted
        decimal allocatedSum = request.RetainedFloatAmount + request.DroppedToSafeAmount;
        if (Math.Abs(allocatedSum - request.ActualCashCounted) > 0.01m)
        {
            throw new BusinessException(
                "INVALID_CASH_ALLOCATION",
                $"Tổng tiền để lại két ({request.RetainedFloatAmount:N0}đ) và tiền nộp két an toàn ({request.DroppedToSafeAmount:N0}đ) " +
                $"không khớp với tiền mặt thực đếm ({request.ActualCashCounted:N0}đ). Chênh lệch: {Math.Abs(allocatedSum - request.ActualCashCounted):N0}đ.");
        }

        var shift = await _shiftRepository.GetByIdAsync(shiftId, cancellationToken);
        if (shift == null)
        {
            throw new BusinessException("SHIFT_NOT_FOUND", "Không tìm thấy ca làm việc.");
        }

        if (shift.Status != ShiftState.Open)
        {
            throw new BusinessException("SHIFT_ALREADY_CLOSED", "Ca làm việc này đã được chốt trước đó.");
        }

        // Truy vấn số liệu bán hàng gắn với ShiftId từ Orders
        var (cashSales, bankRevenue, ordersCount) = await _shiftRepository.GetShiftSalesMetricsAsync(shiftId, cancellationToken);

        // Công thức tính tiền lý thuyết G: OpeningFloat + CashSales + TotalCashIn - TotalCashOut
        decimal expectedCash = shift.OpeningFloat + cashSales + shift.TotalCashIn - shift.TotalCashOut;
        decimal discrepancy = request.ActualCashCounted - expectedCash;

        shift.ClosedAtUtc = DateTime.UtcNow;
        shift.ActualCashCounted = request.ActualCashCounted;
        shift.ExpectedCash = expectedCash;
        shift.Discrepancy = discrepancy;
        shift.RetainedFloatAmount = request.RetainedFloatAmount;
        shift.DroppedToSafeAmount = request.DroppedToSafeAmount;
        shift.TotalBankRevenue = bankRevenue;
        shift.TotalOrdersCount = ordersCount;
        shift.ReceiverUserId = request.ReceiverUserId;
        shift.Status = ShiftState.Closed;
        shift.HandoverStatus = HandoverStatus.PENDING;

        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            shift.Notes = string.IsNullOrWhiteSpace(shift.Notes)
                ? request.Notes.Trim()
                : $"{shift.Notes} | {request.Notes.Trim()}";
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDetailResponse(shift);
    }

    public async Task<ShiftDetailResponse> ConfirmHandoverAsync(
        Guid shiftId,
        ConfirmHandoverRequest request,
        string receiverUserId,
        CancellationToken cancellationToken = default)
    {
        var shift = await _shiftRepository.GetByIdAsync(shiftId, cancellationToken);
        if (shift == null)
        {
            throw new BusinessException("SHIFT_NOT_FOUND", "Không tìm thấy ca làm việc.");
        }

        shift.HandoverStatus = request.Status;
        shift.ReceiverUserId = receiverUserId;

        if (request.Status == HandoverStatus.DISPUTED && !string.IsNullOrWhiteSpace(request.DisputeReason))
        {
            shift.Notes = string.IsNullOrWhiteSpace(shift.Notes)
                ? $"[KHIẾU NẠI BÀN GIAO]: {request.DisputeReason.Trim()}"
                : $"{shift.Notes} | [KHIẾU NẠI BÀN GIAO]: {request.DisputeReason.Trim()}";
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDetailResponse(shift);
    }

    public async Task<ZReportResponse> GetZReportAsync(
        Guid shiftId,
        CancellationToken cancellationToken = default)
    {
        var shift = await _shiftRepository.GetByIdAsync(shiftId, cancellationToken);
        if (shift == null)
        {
            throw new BusinessException("SHIFT_NOT_FOUND", "Không tìm thấy ca làm việc.");
        }

        var (cashSales, bankRevenue, ordersCount) = await _shiftRepository.GetShiftSalesMetricsAsync(shiftId, cancellationToken);

        var movements = shift.CashMovements
            .OrderBy(m => m.OccurredAtUtc)
            .Select(m => new ShiftCashMovementDto(
                m.Id,
                m.ShiftId,
                m.MovementType,
                m.Category,
                m.Amount,
                m.Reason,
                m.CreatedByName,
                m.OccurredAtUtc))
            .ToList();

        return new ZReportResponse(
            shift.Id,
            shift.ShiftCode,
            shift.ShiftName,
            shift.BranchId,
            shift.Branch?.Name,
            shift.TillId,
            shift.OpenedAtUtc,
            shift.ClosedAtUtc,
            shift.OpeningFloat,
            cashSales,
            shift.TotalCashIn,
            shift.TotalCashOut,
            bankRevenue,
            ordersCount,
            shift.ExpectedCash,
            shift.ActualCashCounted,
            shift.Discrepancy,
            shift.RetainedFloatAmount,
            shift.DroppedToSafeAmount,
            shift.HandoverStatus,
            shift.ReceiverUserId,
            shift.Notes,
            movements
        );
    }

    public async Task<IReadOnlyList<ShiftDetailResponse>> GetShiftHistoryAsync(
        Guid branchId,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var list = await _shiftRepository.GetHistoryAsync(branchId, fromUtc, toUtc, cancellationToken);
        return list.Select(MapToDetailResponse).ToList();
    }

    private static ShiftDetailResponse MapToDetailResponse(ShiftZReport shift)
    {
        var movements = shift.CashMovements
            .OrderBy(m => m.OccurredAtUtc)
            .Select(m => new ShiftCashMovementDto(
                m.Id,
                m.ShiftId,
                m.MovementType,
                m.Category,
                m.Amount,
                m.Reason,
                m.CreatedByName,
                m.OccurredAtUtc))
            .ToList();

        return new ShiftDetailResponse(
            shift.Id,
            shift.TenantId,
            shift.BranchId,
            shift.Branch?.Name,
            shift.TillId,
            shift.ShiftCode,
            shift.ShiftName,
            shift.CashierUserId,
            shift.ReceiverUserId,
            shift.PrevShiftId,
            shift.OpenedAtUtc,
            shift.ClosedAtUtc,
            shift.OpeningFloatExpected,
            shift.OpeningFloat,
            shift.OpeningDiscrepancy,
            shift.TotalCashIn,
            shift.TotalCashOut,
            shift.TotalBankRevenue,
            shift.TotalOrdersCount,
            shift.ExpectedCash,
            shift.ActualCashCounted,
            shift.Discrepancy,
            shift.RetainedFloatAmount,
            shift.DroppedToSafeAmount,
            shift.HandoverStatus,
            shift.Status,
            shift.Notes,
            movements
        );
    }
}
