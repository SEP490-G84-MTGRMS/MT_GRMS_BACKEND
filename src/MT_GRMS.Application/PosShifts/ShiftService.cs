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

        // 0. Kiểm tra điều kiện trực ca của thu ngân nếu là vai trò EMPLOYEE (REQ-STRICT-SCHEDULE-VALIDATION)
        var eligibility = await CheckCashierShiftEligibilityAsync(request.BranchId, cashierUserId, _tenantContext.CurrentRole, cancellationToken);
        if (!eligibility.IsAllowed)
        {
            throw new BusinessException(
                "EMPLOYEE_NOT_SCHEDULED",
                eligibility.Reason ?? "Bạn không có lịch làm việc trong ca hiện tại của chi nhánh.");
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

        // Ghi nhận nhân viên mở ca vào danh sách người tham gia ca
        await _shiftRepository.AddParticipantAsync(new ShiftParticipant
        {
            TenantId = _tenantContext.CurrentTenantId,
            ShiftId = shift.Id,
            UserId = cashierUserId,
            UserName = "Thu ngân mở ca",
            JoinedAtUtc = DateTime.UtcNow,
            LastActiveAtUtc = DateTime.UtcNow
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDetailResponse(shift);
    }

    public async Task<ShiftDetailResponse?> GetCurrentShiftAsync(
        Guid branchId,
        Guid? tillId = null,
        CancellationToken cancellationToken = default)
    {
        var shift = await _shiftRepository.GetActiveShiftAsync(branchId, tillId, cancellationToken);
        if (shift == null) return null;

        var (cashSales, bankRevenue, ordersCount) = await _shiftRepository.GetShiftSalesMetricsAsync(shift.Id, cancellationToken);
        shift.TotalBankRevenue = bankRevenue;
        shift.TotalOrdersCount = ordersCount;
        shift.ExpectedCash = shift.OpeningFloat + cashSales + shift.TotalCashIn - shift.TotalCashOut;

        return MapToDetailResponse(shift);
    }

    public async Task<ShiftDetailResponse?> GetShiftByIdAsync(
        Guid shiftId,
        CancellationToken cancellationToken = default)
    {
        var shift = await _shiftRepository.GetByIdAsync(shiftId, cancellationToken);
        if (shift == null) return null;

        if (shift.Status == ShiftState.Open)
        {
            var (cashSales, bankRevenue, ordersCount) = await _shiftRepository.GetShiftSalesMetricsAsync(shift.Id, cancellationToken);
            shift.TotalBankRevenue = bankRevenue;
            shift.TotalOrdersCount = ordersCount;
            shift.ExpectedCash = shift.OpeningFloat + cashSales + shift.TotalCashIn - shift.TotalCashOut;
        }

        return MapToDetailResponse(shift);
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

        // Truy vấn doanh số bán hàng tiền mặt hiện tại để tính tiền mặt thực có trong két
        var (cashSales, _, _) = await _shiftRepository.GetShiftSalesMetricsAsync(shiftId, cancellationToken);
        decimal currentDrawerCash = shift.OpeningFloat + cashSales + shift.TotalCashIn - shift.TotalCashOut;

        if (request.MovementType == CashMovementType.OUT && request.Amount > currentDrawerCash)
        {
            throw new BusinessException(
                "EXCEED_DRAWER_CASH",
                $"Số tiền chi ({request.Amount:N0} đ) vượt quá số tiền mặt hiện có trong két quầy ({Math.Max(0, currentDrawerCash):N0} đ).");
        }

        // 1. Kiểm tra cấu hình hạn mức chi của tenant
        var paymentConfig = await _shiftRepository.GetPaymentConfigAsync(_tenantContext.CurrentTenantId, cancellationToken);
        decimal threshold = paymentConfig?.CashOutApprovalThreshold ?? 200000m;

        CashMovementStatus initialStatus = CashMovementStatus.Recorded;

        if (request.MovementType == CashMovementType.IN)
        {
            initialStatus = CashMovementStatus.Recorded;
            shift.TotalCashIn += request.Amount;
        }
        else // OUT
        {
            if (request.Category == CashMovementCategory.CASH_DRAW)
            {
                // Rút két nộp chủ: trừ tiền ngay, nhưng cần chủ xác nhận đã nhận
                initialStatus = CashMovementStatus.Recorded;
                shift.TotalCashOut += request.Amount;
            }
            else // EXPENSE
            {
                if (request.Amount > threshold)
                {
                    // Vượt hạn mức: Cần chủ duyệt, chưa trừ vào két (chưa ảnh hưởng G)
                    initialStatus = CashMovementStatus.PendingApproval;
                }
                else
                {
                    initialStatus = CashMovementStatus.Recorded;
                    shift.TotalCashOut += request.Amount;
                }
            }
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
            OccurredAtUtc = DateTime.UtcNow,
            Status = initialStatus,
            IsConfirmedByOwner = false
        };

        // Cập nhật lại ExpectedCash tạm tính
        shift.ExpectedCash = shift.OpeningFloat + cashSales + shift.TotalCashIn - shift.TotalCashOut;

        await _shiftRepository.AddCashMovementAsync(movement, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToMovementDto(movement);
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

        if (shift.Status != ShiftState.Open && shift.Status != ShiftState.Closing)
        {
            throw new BusinessException("SHIFT_ALREADY_CLOSED", "Ca làm việc này đã được chốt trước đó.");
        }

        // Bắt buộc: Chặn chốt ca nếu còn phiếu chi PendingApproval
        var hasPending = shift.CashMovements.Any(m => m.Status == CashMovementStatus.PendingApproval);
        if (hasPending)
        {
            throw new BusinessException(
                "PENDING_CASH_MOVEMENTS",
                "Ca làm việc vẫn còn phiếu chi đang chờ Chủ cửa hàng phê duyệt. Vui lòng phê duyệt hoặc hủy các phiếu này trước khi chốt ca.");
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
            .Select(MapToMovementDto)
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

    public async Task<ShiftCashMovementDto> ApproveCashMovementAsync(
        Guid movementId,
        string approverName,
        CancellationToken cancellationToken = default)
    {
        var movement = await _shiftRepository.GetCashMovementByIdAsync(movementId, cancellationToken);
        if (movement == null)
        {
            throw new BusinessException("MOVEMENT_NOT_FOUND", "Không tìm thấy phiếu thu/chi két.");
        }

        if (movement.Status != CashMovementStatus.PendingApproval)
        {
            throw new BusinessException("INVALID_STATUS", "Chỉ có thể phê duyệt các phiếu đang ở trạng thái chờ duyệt.");
        }

        movement.Status = CashMovementStatus.Recorded;
        movement.ApprovedBy = approverName;
        movement.ApprovedAtUtc = DateTime.UtcNow;

        if (movement.Shift != null)
        {
            // Trừ tiền chi vào ca
            movement.Shift.TotalCashOut += movement.Amount;
            var (cashSales, _, _) = await _shiftRepository.GetShiftSalesMetricsAsync(movement.ShiftId, cancellationToken);
            movement.Shift.ExpectedCash = movement.Shift.OpeningFloat + cashSales + movement.Shift.TotalCashIn - movement.Shift.TotalCashOut;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToMovementDto(movement);
    }

    public async Task<ShiftCashMovementDto> RejectCashMovementAsync(
        Guid movementId,
        string approverName,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessException("REQUIRED_REASON", "Vui lòng nhập lý do từ chối phiếu chi.");
        }

        var movement = await _shiftRepository.GetCashMovementByIdAsync(movementId, cancellationToken);
        if (movement == null)
        {
            throw new BusinessException("MOVEMENT_NOT_FOUND", "Không tìm thấy phiếu thu/chi két.");
        }

        if (movement.Status != CashMovementStatus.PendingApproval)
        {
            throw new BusinessException("INVALID_STATUS", "Chỉ có thể từ chối các phiếu đang ở trạng thái chờ duyệt.");
        }

        movement.Status = CashMovementStatus.Rejected;
        movement.ApprovedBy = approverName;
        movement.RejectionReason = reason.Trim();
        movement.ApprovedAtUtc = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToMovementDto(movement);
    }

    public async Task<ShiftCashMovementDto> CancelCashMovementAsync(
        Guid movementId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var movement = await _shiftRepository.GetCashMovementByIdAsync(movementId, cancellationToken);
        if (movement == null)
        {
            throw new BusinessException("MOVEMENT_NOT_FOUND", "Không tìm thấy phiếu thu/chi két.");
        }

        if (movement.Status != CashMovementStatus.PendingApproval)
        {
            throw new BusinessException("INVALID_STATUS", "Chỉ có thể hủy phiếu đang ở trạng thái chờ duyệt.");
        }

        movement.Status = CashMovementStatus.Cancelled;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToMovementDto(movement);
    }

    public async Task<ShiftCashMovementDto> ConfirmCashDropAsync(
        Guid movementId,
        string ownerName,
        CancellationToken cancellationToken = default)
    {
        var movement = await _shiftRepository.GetCashMovementByIdAsync(movementId, cancellationToken);
        if (movement == null)
        {
            throw new BusinessException("MOVEMENT_NOT_FOUND", "Không tìm thấy phiếu thu/chi két.");
        }

        if (movement.Category != CashMovementCategory.CASH_DRAW)
        {
            throw new BusinessException("INVALID_CATEGORY", "Chỉ phiếu rút két nộp chủ mới cần xác nhận nhận tiền.");
        }

        movement.IsConfirmedByOwner = true;
        movement.ConfirmedByOwnerAtUtc = DateTime.UtcNow;
        movement.ApprovedBy = ownerName;

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToMovementDto(movement);
    }

    public async Task<PendingMovementsSummaryResponse> GetPendingMovementsSummaryAsync(
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var list = await _shiftRepository.GetPendingMovementsAsync(branchId, cancellationToken);
        var dtos = list.Select(MapToMovementDto).ToList();
        int pendingApprovals = dtos.Count(m => m.Status == CashMovementStatus.PendingApproval);
        int unconfirmedDrops = dtos.Count(m => m.Category == CashMovementCategory.CASH_DRAW && !m.IsConfirmedByOwner);

        return new PendingMovementsSummaryResponse(pendingApprovals, unconfirmedDrops, dtos);
    }

    public async Task<CashMovementSettingsDto> GetCashMovementSettingsAsync(CancellationToken cancellationToken = default)
    {
        var config = await _shiftRepository.GetPaymentConfigAsync(_tenantContext.CurrentTenantId, cancellationToken);
        return new CashMovementSettingsDto(
            config?.CashOutApprovalThreshold ?? 200000m,
            config?.CashOutShiftTotalLimit ?? 0m
        );
    }

    public async Task<CashMovementSettingsDto> UpdateCashMovementSettingsAsync(
        UpdateCashMovementSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.CashOutApprovalThreshold < 0)
        {
            throw new BusinessException("INVALID_THRESHOLD", "Hạn mức duyệt chi không được âm.");
        }

        var config = await _shiftRepository.GetPaymentConfigAsync(_tenantContext.CurrentTenantId, cancellationToken)
            ?? new StorePaymentConfig
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.CurrentTenantId,
                BankCode = "MB",
                BankName = "MBBank",
                BankAccountNumber = "0918889999",
                BankAccountName = "STORE OWNER",
                CreatedAt = DateTime.UtcNow
            };

        config.CashOutApprovalThreshold = request.CashOutApprovalThreshold;
        config.CashOutShiftTotalLimit = request.CashOutShiftTotalLimit;
        config.UpdatedAt = DateTime.UtcNow;

        await _shiftRepository.SavePaymentConfigAsync(config, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CashMovementSettingsDto(config.CashOutApprovalThreshold, config.CashOutShiftTotalLimit);
    }

    public async Task<ShiftDetailResponse> StartClosingShiftAsync(
        Guid shiftId,
        Guid userId,
        StartClosingShiftRequest request,
        CancellationToken cancellationToken = default)
    {
        var shift = await _shiftRepository.GetByIdAsync(shiftId, cancellationToken);
        if (shift == null)
        {
            throw new BusinessException("SHIFT_NOT_FOUND", "Không tìm thấy ca làm việc.");
        }

        if (shift.Status != ShiftState.Open)
        {
            throw new BusinessException("INVALID_SHIFT_STATE", "Chỉ có thể bắt đầu chốt ca khi ca đang mở (Open).");
        }

        var (cashSales, bankRevenue, ordersCount) = await _shiftRepository.GetShiftSalesMetricsAsync(shiftId, cancellationToken);
        shift.TotalBankRevenue = bankRevenue;
        shift.TotalOrdersCount = ordersCount;
        shift.ExpectedCash = shift.OpeningFloat + cashSales + shift.TotalCashIn - shift.TotalCashOut;

        shift.Status = ShiftState.Closing;
        shift.ClosingStartedByUserId = userId;
        shift.ClosingStartedAtUtc = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            shift.Notes = string.IsNullOrWhiteSpace(shift.Notes) ? request.Notes : $"{shift.Notes} | {request.Notes}";
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDetailResponse(shift);
    }

    public async Task<ShiftDetailResponse> CancelClosingShiftAsync(
        Guid shiftId,
        Guid userId,
        CancelClosingShiftRequest request,
        CancellationToken cancellationToken = default)
    {
        var shift = await _shiftRepository.GetByIdAsync(shiftId, cancellationToken);
        if (shift == null)
        {
            throw new BusinessException("SHIFT_NOT_FOUND", "Không tìm thấy ca làm việc.");
        }

        if (shift.Status != ShiftState.Closing)
        {
            throw new BusinessException("INVALID_SHIFT_STATE", "Chỉ có thể hủy chốt ca khi ca đang ở trạng thái kiểm đếm (Closing).");
        }

        shift.Status = ShiftState.Open;
        shift.ClosingStartedByUserId = null;
        shift.ClosingStartedAtUtc = null;
        shift.ActualCashCounted = null;
        shift.Discrepancy = null;

        await _shiftRepository.ClearCloseConfirmationsAsync(shiftId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            var cancelNote = $"[Hủy chốt ca lúc {DateTime.UtcNow:HH:mm dd/MM}: {request.Reason}]";
            shift.Notes = string.IsNullOrWhiteSpace(shift.Notes) ? cancelNote : $"{shift.Notes} | {cancelNote}";
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDetailResponse(shift);
    }

    public async Task<ShiftDetailResponse> UpdateCountedCashAsync(
        Guid shiftId,
        Guid userId,
        UpdateCountedCashRequest request,
        CancellationToken cancellationToken = default)
    {
        var shift = await _shiftRepository.GetByIdAsync(shiftId, cancellationToken);
        if (shift == null)
        {
            throw new BusinessException("SHIFT_NOT_FOUND", "Không tìm thấy ca làm việc.");
        }

        if (shift.Status != ShiftState.Closing)
        {
            throw new BusinessException("INVALID_SHIFT_STATE", "Chỉ có thể cập nhật số tiền đếm khi ca đang ở trạng thái kiểm đếm (Closing).");
        }

        if (request.CountedCash < 0)
        {
            throw new BusinessException("INVALID_ACTUAL_CASH", "Tiền kiểm đếm thực tế không được âm.");
        }

        var (cashSales, bankRevenue, ordersCount) = await _shiftRepository.GetShiftSalesMetricsAsync(shiftId, cancellationToken);
        shift.TotalBankRevenue = bankRevenue;
        shift.TotalOrdersCount = ordersCount;
        shift.ExpectedCash = shift.OpeningFloat + cashSales + shift.TotalCashIn - shift.TotalCashOut;

        shift.ActualCashCounted = request.CountedCash;
        shift.Discrepancy = shift.ActualCashCounted - shift.ExpectedCash;

        await _shiftRepository.ClearCloseConfirmationsAsync(shiftId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            shift.Notes = string.IsNullOrWhiteSpace(shift.Notes) ? request.Notes : $"{shift.Notes} | {request.Notes}";
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDetailResponse(shift);
    }

    public async Task<ShiftDetailResponse> ConfirmCloseShiftAsync(
        Guid shiftId,
        Guid userId,
        string userName,
        ConfirmCloseShiftRequest request,
        CancellationToken cancellationToken = default)
    {
        var shift = await _shiftRepository.GetByIdAsync(shiftId, cancellationToken);
        if (shift == null)
        {
            throw new BusinessException("SHIFT_NOT_FOUND", "Không tìm thấy ca làm việc.");
        }

        if (shift.Status != ShiftState.Closing && shift.Status != ShiftState.Open)
        {
            throw new BusinessException("INVALID_SHIFT_STATE", "Ca làm việc này đã được chốt trước đó.");
        }

        if (request.ActualCashCounted < 0)
        {
            throw new BusinessException("INVALID_ACTUAL_CASH", "Tiền kiểm đếm thực tế không được âm.");
        }

        decimal allocatedSum = request.RetainedFloatAmount + request.DroppedToSafeAmount;
        if (Math.Abs(allocatedSum - request.ActualCashCounted) > 0.01m)
        {
            throw new BusinessException(
                "INVALID_CASH_ALLOCATION",
                $"Tổng tiền để lại két ({request.RetainedFloatAmount:N0}đ) và tiền nộp két an toàn ({request.DroppedToSafeAmount:N0}đ) " +
                $"không khớp với tiền mặt thực đếm ({request.ActualCashCounted:N0}đ). Chênh lệch: {Math.Abs(allocatedSum - request.ActualCashCounted):N0}đ.");
        }

        var hasPending = shift.CashMovements.Any(m => m.Status == CashMovementStatus.PendingApproval);
        if (hasPending)
        {
            throw new BusinessException(
                "PENDING_CASH_MOVEMENTS",
                "Không thể chốt ca do còn phiếu chi tiền đang chờ Chủ cửa hàng phê duyệt. Vui lòng duyệt hoặc từ chối phiếu chi trước khi đóng ca.");
        }

        await _shiftRepository.AddCloseConfirmationAsync(new ShiftCloseConfirmation
        {
            TenantId = _tenantContext.CurrentTenantId,
            ShiftId = shiftId,
            UserId = userId,
            UserName = userName,
            ConfirmedCountedCash = request.ActualCashCounted,
            ConfirmedAtUtc = DateTime.UtcNow
        }, cancellationToken);

        var (cashSales, bankRevenue, ordersCount) = await _shiftRepository.GetShiftSalesMetricsAsync(shiftId, cancellationToken);
        shift.TotalBankRevenue = bankRevenue;
        shift.TotalOrdersCount = ordersCount;
        shift.ExpectedCash = shift.OpeningFloat + cashSales + shift.TotalCashIn - shift.TotalCashOut;

        shift.ActualCashCounted = request.ActualCashCounted;
        shift.Discrepancy = shift.ActualCashCounted - shift.ExpectedCash;
        shift.RetainedFloatAmount = request.RetainedFloatAmount;
        shift.DroppedToSafeAmount = request.DroppedToSafeAmount;
        shift.ReceiverUserId = request.ReceiverUserId;
        shift.ClosedAtUtc = DateTime.UtcNow;
        shift.ClosedByUserId = userId;
        shift.Status = ShiftState.Closed;
        shift.HandoverStatus = HandoverStatus.PENDING;

        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            shift.Notes = string.IsNullOrWhiteSpace(shift.Notes) ? request.Notes : $"{shift.Notes} | {request.Notes}";
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDetailResponse(shift);
    }

    public async Task<ShiftDetailResponse> ForceCloseShiftAsync(
        Guid shiftId,
        Guid ownerUserId,
        ForceCloseShiftRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new BusinessException("REASON_REQUIRED", "Lý do đóng ca cưỡng bức là bắt buộc.");
        }

        if (request.ActualCashCounted < 0)
        {
            throw new BusinessException("INVALID_ACTUAL_CASH", "Tiền kiểm đếm thực tế không được âm.");
        }

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

        if (shift.Status == ShiftState.Closed)
        {
            throw new BusinessException("SHIFT_ALREADY_CLOSED", "Ca làm việc này đã được chốt trước đó.");
        }

        var (cashSales, bankRevenue, ordersCount) = await _shiftRepository.GetShiftSalesMetricsAsync(shiftId, cancellationToken);
        shift.TotalBankRevenue = bankRevenue;
        shift.TotalOrdersCount = ordersCount;
        shift.ExpectedCash = shift.OpeningFloat + cashSales + shift.TotalCashIn - shift.TotalCashOut;

        shift.ActualCashCounted = request.ActualCashCounted;
        shift.Discrepancy = shift.ActualCashCounted - shift.ExpectedCash;
        shift.RetainedFloatAmount = request.RetainedFloatAmount;
        shift.DroppedToSafeAmount = request.DroppedToSafeAmount;
        shift.ClosedAtUtc = DateTime.UtcNow;
        shift.ClosedByUserId = ownerUserId;
        shift.ForceCloseReason = request.Reason.Trim();
        shift.Status = ShiftState.Closed;
        shift.HandoverStatus = HandoverStatus.PENDING;

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDetailResponse(shift);
    }

    public async Task RecordParticipantAsync(
        Guid shiftId,
        Guid userId,
        string? userName,
        string? fullName,
        CancellationToken cancellationToken = default)
    {
        var participant = await _shiftRepository.GetParticipantAsync(shiftId, userId, cancellationToken);
        if (participant == null)
        {
            participant = new ShiftParticipant
            {
                TenantId = _tenantContext.CurrentTenantId,
                ShiftId = shiftId,
                UserId = userId,
                UserName = userName,
                FullName = fullName,
                JoinedAtUtc = DateTime.UtcNow,
                LastActiveAtUtc = DateTime.UtcNow
            };
            await _shiftRepository.AddParticipantAsync(participant, cancellationToken);
        }
        else
        {
            participant.LastActiveAtUtc = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(userName)) participant.UserName = userName;
            if (!string.IsNullOrWhiteSpace(fullName)) participant.FullName = fullName;
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public Task<Guid?> ResolveBranchIdAsync(string? branchIdOrCode, CancellationToken cancellationToken = default)
    {
        return _shiftRepository.ResolveBranchIdAsync(branchIdOrCode, cancellationToken);
    }

    private static ShiftCashMovementDto MapToMovementDto(ShiftCashMovement movement)
    {
        return new ShiftCashMovementDto(
            movement.Id,
            movement.ShiftId,
            movement.MovementType,
            movement.Category,
            movement.Amount,
            movement.Reason,
            movement.CreatedByName,
            movement.OccurredAtUtc,
            movement.Status,
            movement.ApprovedBy,
            movement.ApprovedAtUtc,
            movement.RejectionReason,
            movement.IsConfirmedByOwner,
            movement.ConfirmedByOwnerAtUtc
        );
    }

    private static ShiftDetailResponse MapToDetailResponse(ShiftZReport shift)
    {
        var movements = shift.CashMovements
            .OrderBy(m => m.OccurredAtUtc)
            .Select(MapToMovementDto)
            .ToList();

        var participants = shift.Participants
            .OrderBy(p => p.JoinedAtUtc)
            .Select(p => new ShiftParticipantDto(
                p.Id,
                p.ShiftId,
                p.UserId,
                p.UserName,
                p.FullName,
                p.JoinedAtUtc,
                p.LastActiveAtUtc
            ))
            .ToList();

        var confirmations = shift.CloseConfirmations
            .OrderBy(c => c.ConfirmedAtUtc)
            .Select(c => new ShiftCloseConfirmationDto(
                c.Id,
                c.ShiftId,
                c.UserId,
                c.UserName,
                c.ConfirmedCountedCash,
                c.ConfirmedAtUtc
            ))
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
            movements,
            shift.ClosingStartedByUserId,
            shift.ClosingStartedAtUtc,
            shift.ClosedByUserId,
            shift.ForceCloseReason,
            participants,
            confirmations
        );
    }

    public async Task<CashierShiftEligibilityDto> CheckCashierShiftEligibilityAsync(
        Guid branchId,
        Guid userId,
        string? userRole,
        CancellationToken cancellationToken = default)
    {
        var roleUpper = (userRole ?? _tenantContext.CurrentRole ?? string.Empty).ToUpperInvariant();

        // 1. Miễn trừ cho Chủ cửa hàng / Quản lý / Admin
        if (roleUpper.Contains("OWNER") || roleUpper.Contains("ADMIN") || roleUpper.Contains("MANAGER"))
        {
            return new CashierShiftEligibilityDto(
                IsAllowed: true,
                IsExempt: true,
                Role: roleUpper,
                EmployeeName: "Quản lý / Chủ cửa hàng",
                Reason: null,
                ActiveSchedule: null
            );
        }

        // 2. Tra cứu hồ sơ nhân viên qua UserId
        var employee = await _shiftRepository.GetEmployeeByUserIdAsync(userId.ToString(), cancellationToken);
        if (employee == null)
        {
            return new CashierShiftEligibilityDto(
                IsAllowed: false,
                IsExempt: false,
                Role: roleUpper,
                EmployeeName: null,
                Reason: "Không tìm thấy hồ sơ nhân viên tương ứng với tài khoản đăng nhập.",
                ActiveSchedule: null
            );
        }

        // 3. Lấy thời gian hiện tại theo múi giờ Việt Nam (UTC+7)
        var nowVn = DateTime.UtcNow.AddHours(7);
        var todayDate = nowVn.Date;
        var currentTimeOfDay = nowVn.TimeOfDay;

        // 4. Lấy lịch làm việc hôm nay của nhân viên tại chi nhánh
        var schedules = await _shiftRepository.GetCashierSchedulesAsync(employee.Id, branchId, todayDate, cancellationToken);
        if (schedules == null || schedules.Count == 0)
        {
            return new CashierShiftEligibilityDto(
                IsAllowed: false,
                IsExempt: false,
                Role: roleUpper,
                EmployeeName: employee.FullName,
                Reason: "Bạn không có lịch làm việc trong ca hiện tại của chi nhánh.",
                ActiveSchedule: null
            );
        }

        // 5. Kiểm tra thời gian hiện tại có nằm trong ca trực nào (kèm dung sai 30 phút)
        var buffer = TimeSpan.FromMinutes(30);
        foreach (var s in schedules)
        {
            var startWindow = s.PlannedStartTime > buffer ? s.PlannedStartTime.Subtract(buffer) : TimeSpan.Zero;
            var endWindow = s.PlannedEndTime.Add(buffer);

            bool isInShift;
            if (!s.IsCheckInNextDay && endWindow >= startWindow)
            {
                isInShift = currentTimeOfDay >= startWindow && currentTimeOfDay <= endWindow;
            }
            else
            {
                // Ca qua đêm vắt qua nửa đêm (VD: 22:00 -> 06:00)
                isInShift = currentTimeOfDay >= startWindow || currentTimeOfDay <= endWindow;
            }

            if (isInShift)
            {
                return new CashierShiftEligibilityDto(
                    IsAllowed: true,
                    IsExempt: false,
                    Role: roleUpper,
                    EmployeeName: employee.FullName,
                    Reason: null,
                    ActiveSchedule: new CashierShiftInfo(
                        s.Id,
                        s.ShiftCode,
                        s.ShiftName,
                        s.PlannedStartTime,
                        s.PlannedEndTime
                    )
                );
            }
        }

        return new CashierShiftEligibilityDto(
            IsAllowed: false,
            IsExempt: false,
            Role: roleUpper,
            EmployeeName: employee.FullName,
            Reason: "Bạn không có lịch làm việc trong ca hiện tại của chi nhánh.",
            ActiveSchedule: null
        );
    }
}
