using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Cashbook;
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
    private readonly ICashbookService? _cashbookService;

    public ShiftService(
        IShiftRepository shiftRepository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext,
        ICashbookService? cashbookService = null)
    {
        _shiftRepository = shiftRepository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
        _cashbookService = cashbookService;
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

        var openedAtUtc = DateTime.UtcNow;
        var businessDayStart = await _shiftRepository.GetBusinessDayStartTimeAsync(request.BranchId, cancellationToken);
        var shift = new ShiftZReport
        {
            TenantId = _tenantContext.CurrentTenantId,
            BranchId = request.BranchId,
            TillId = request.TillId,
            ShiftCode = shiftCode,
            ShiftName = string.IsNullOrWhiteSpace(request.ShiftName) ? $"Ca {DateTime.UtcNow:dd/MM/yyyy HH:mm}" : request.ShiftName.Trim(),
            CashierUserId = cashierUserId,
            OpenedScheduleId = eligibility.ActiveSchedule?.ScheduleId,
            PrevShiftId = prevShift?.Id,
            OpenedAtUtc = openedAtUtc,
            BusinessDate = BusinessDateResolver.Resolve(openedAtUtc, businessDayStart),
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
            UserName = !string.IsNullOrWhiteSpace(eligibility.EmployeeName) ? eligibility.EmployeeName : "Thu ngân mở ca",
            FullName = eligibility.EmployeeName,
            JoinedAtUtc = DateTime.UtcNow,
            LastActiveAtUtc = DateTime.UtcNow
        }, cancellationToken);

        // Ghi nhận các nhân viên cùng có lịch trực tại chi nhánh hôm nay vào danh sách nhân sự của ca (tránh trùng lặp)
        var nowVn = DateTime.UtcNow.AddHours(7);
        var todayVn = nowVn.Date;
        var addedUserIds = new HashSet<Guid> { cashierUserId };
        var branchSchedules = await _shiftRepository.GetBranchCashierSchedulesAsync(request.BranchId, todayVn, cancellationToken);
        if (branchSchedules != null)
        {
            foreach (var sched in branchSchedules)
            {
                var start = sched.AllowedCheckInStart != TimeSpan.Zero ? sched.AllowedCheckInStart : sched.PlannedStartTime;
                var end = sched.AllowedCheckInEnd != TimeSpan.Zero ? sched.AllowedCheckInEnd : sched.PlannedEndTime;
                var currentTime = nowVn.TimeOfDay;
                var isCurrentSchedule = (!sched.IsCheckInNextDay && end >= start)
                    ? currentTime >= start && currentTime <= end
                    : currentTime >= start || currentTime <= end;

                if (isCurrentSchedule && sched.Employee != null && sched.Employee.Status == Domain.Enums.EmployeeStatus.Active && !string.IsNullOrWhiteSpace(sched.Employee.UserId))
                {
                    var pGuid = TenantContext.ResolveDeterministicGuid(sched.Employee.UserId);
                    if (!addedUserIds.Contains(pGuid))
                    {
                        var existing = await _shiftRepository.GetParticipantAsync(shift.Id, pGuid, cancellationToken);
                        if (existing == null)
                        {
                            addedUserIds.Add(pGuid);
                            await _shiftRepository.AddParticipantAsync(new ShiftParticipant
                            {
                                TenantId = _tenantContext.CurrentTenantId,
                                ShiftId = shift.Id,
                                UserId = pGuid,
                                UserName = sched.Employee.FullName ?? sched.Employee.EmployeeCode,
                                FullName = sched.Employee.FullName,
                                JoinedAtUtc = DateTime.UtcNow,
                                LastActiveAtUtc = DateTime.UtcNow
                            }, cancellationToken);
                        }
                    }
                }
            }
        }

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

        // Nếu người gọi là STAFF đang trực thuộc chi nhánh này, tự động đảm bảo có tên trong ShiftParticipants
        if (_tenantContext != null && string.Equals(_tenantContext.CurrentRole, "STAFF", StringComparison.OrdinalIgnoreCase))
        {
            var rawUserId = _tenantContext.CurrentRawUserId;
            var currentUserId = _tenantContext.CurrentUserId;
            if (!string.IsNullOrWhiteSpace(rawUserId) && currentUserId != Guid.Empty)
            {
                var existing = await _shiftRepository.GetParticipantAsync(shift.Id, currentUserId, cancellationToken);
                if (existing == null)
                {
                    var emp = await _shiftRepository.GetEmployeeByUserIdAsync(rawUserId, cancellationToken);
                    if (emp != null && emp.BranchId == branchId && emp.Status == Domain.Enums.EmployeeStatus.Active)
                    {
                        await RecordParticipantAsync(shift.Id, currentUserId, emp.FullName ?? emp.EmployeeCode, emp.FullName, cancellationToken);
                        shift = await _shiftRepository.GetActiveShiftAsync(branchId, tillId, cancellationToken) ?? shift;
                    }
                }
            }
        }

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
        var movementType = request.Category switch
        {
            CashMovementCategory.EXPENSE => CashMovementType.OUT,
            CashMovementCategory.CASH_DRAW => CashMovementType.OUT,
            CashMovementCategory.CASH_DEPOSIT => CashMovementType.IN,
            _ => throw new BusinessException(
                "INVALID_MANUAL_CASH_CATEGORY",
                "Loại nghiệp vụ này không được tạo thủ công tại màn hình thu/chi trong ca.")
        };

        if (request.Amount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền thu chi két phải lớn hơn 0.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new BusinessException("REQUIRED_REASON", "Lý do thu chi két không được để trống.");
        }
        if (request.Reason.Length > 500)
            throw new BusinessException("REASON_TOO_LONG", "Lý do tối đa 500 ký tự.");

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

        if (movementType == CashMovementType.OUT && request.Amount > currentDrawerCash)
        {
            throw new BusinessException(
                "EXCEED_DRAWER_CASH",
                $"Số tiền chi ({request.Amount:N0} đ) vượt quá số tiền mặt hiện có trong két quầy ({Math.Max(0, currentDrawerCash):N0} đ).");
        }

        // 1. Kiểm tra cấu hình hạn mức chi của tenant
        var paymentConfig = await _shiftRepository.GetPaymentConfigAsync(_tenantContext.CurrentTenantId, cancellationToken);
        decimal threshold = paymentConfig?.CashOutApprovalThreshold ?? 200000m;

        CashMovementStatus initialStatus = CashMovementStatus.Recorded;

        if (movementType == CashMovementType.IN)
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
            MovementType = movementType,
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
        shift.TotalCashSales = cashSales;
        shift.TotalOrdersCount = ordersCount;
        shift.ReceiverUserId = request.ReceiverUserId;
        shift.Status = ShiftState.Closed;
        shift.HandoverStatus = HandoverStatus.PENDING;
        shift.SnapshotLocked = true;
        shift.CashbookPostingStatus = CashbookPostingStatus.NotPosted;

        if (!string.IsNullOrWhiteSpace(request.EarlyOrLateReason))
        {
            var reasonTag = $"[GIẢI TRÌNH GIỜ CHỐT CA: {request.EarlyOrLateReason.Trim()}]";
            if (shift.Notes == null || !shift.Notes.Contains(request.EarlyOrLateReason.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                shift.Notes = string.IsNullOrWhiteSpace(shift.Notes)
                    ? reasonTag
                    : $"{shift.Notes} | {reasonTag}";
            }
        }

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
        if (!string.Equals(_tenantContext.CurrentRole, "STORE_OWNER", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(_tenantContext.CurrentRole, "ADMIN", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Chỉ Chủ cửa hàng hoặc Admin được xác nhận bàn giao ca.");

        var shift = await _shiftRepository.GetByIdAsync(shiftId, cancellationToken);
        if (shift == null)
        {
            throw new BusinessException("SHIFT_NOT_FOUND", "Không tìm thấy ca làm việc.");
        }

        if (shift.Status != ShiftState.Closed)
        {
            throw new BusinessException(
                "SHIFT_NOT_CLOSED",
                $"Ca làm việc '{shift.ShiftName}' ({shift.ShiftCode}) vẫn đang mở. Vui lòng chốt ca hoặc buộc đóng ca trước khi duyệt bàn giao.");
        }

        if (shift.HandoverStatus == HandoverStatus.CONFIRMED)
            throw new BusinessException("HANDOVER_ALREADY_CONFIRMED", "Ca đã được xác nhận bàn giao trước đó.");
        if (request.Status is not (HandoverStatus.CONFIRMED or HandoverStatus.DISPUTED))
            throw new BusinessException("INVALID_HANDOVER_STATUS", "Chỉ chấp nhận CONFIRMED hoặc DISPUTED.");
        if (request.Status == HandoverStatus.DISPUTED && string.IsNullOrWhiteSpace(request.DisputeReason))
            throw new BusinessException("DISPUTE_REASON_REQUIRED", "Bắt buộc nhập lý do khi tranh chấp bàn giao.");
        if (request.DisputeReason?.Length > 500)
            throw new BusinessException("DISPUTE_REASON_TOO_LONG", "Lý do tranh chấp tối đa 500 ký tự.");

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
        var hasChanges = false;

        foreach (var shift in list)
        {
            var shiftDateVn = shift.OpenedAtUtc.AddHours(7).Date;
            var branchSchedules = await _shiftRepository.GetBranchCashierSchedulesAsync(shift.BranchId, shiftDateVn, cancellationToken);
            foreach (var sched in branchSchedules)
            {
                if (sched.Employee != null && sched.Employee.Status == Domain.Enums.EmployeeStatus.Active && !string.IsNullOrWhiteSpace(sched.Employee.UserId))
                {
                    var pGuid = TenantContext.ResolveDeterministicGuid(sched.Employee.UserId);
                    var existing = shift.Participants.FirstOrDefault(p => p.UserId == pGuid);
                    if (existing == null)
                    {
                        var newPart = new ShiftParticipant
                        {
                            TenantId = shift.TenantId,
                            ShiftId = shift.Id,
                            UserId = pGuid,
                            UserName = sched.Employee.FullName ?? sched.Employee.EmployeeCode,
                            FullName = sched.Employee.FullName,
                            JoinedAtUtc = shift.OpenedAtUtc,
                            LastActiveAtUtc = shift.ClosedAtUtc ?? shift.OpenedAtUtc
                        };
                        await _shiftRepository.AddParticipantAsync(newPart, cancellationToken);
                        shift.Participants.Add(newPart);
                        hasChanges = true;
                    }
                }
            }
        }

        if (hasChanges)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

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

        await EnsureClosingParticipantAsync(shift, userId, cancellationToken);

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

        await EnsureClosingParticipantAsync(shift, userId, cancellationToken);

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

        await EnsureClosingParticipantAsync(shift, userId, cancellationToken);

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

        if (shift.Status != ShiftState.Closing)
        {
            throw new BusinessException("INVALID_SHIFT_STATE", "Ca làm việc này đã được chốt trước đó.");
        }

        var participant = await _shiftRepository.GetParticipantAsync(shiftId, userId, cancellationToken);
        if (shift.CashierUserId != userId && participant == null)
            throw new UnauthorizedAccessException("Bạn không phải người phụ trách hoặc người tham gia ca này.");

        if (request.ActualCashCounted < 0)
        {
            throw new BusinessException("INVALID_ACTUAL_CASH", "Tiền kiểm đếm thực tế không được âm.");
        }

        if (request.RetainedFloatAmount < 0 || request.DroppedToSafeAmount < 0)
            throw new BusinessException("INVALID_CASH_ALLOCATION", "Tiền để lại két và tiền nộp két không được âm.");
        if (request.ReceiverUserId?.Length > 100)
            throw new BusinessException("RECEIVER_TOO_LONG", "Người nhận ca tối đa 100 ký tự.");
        if (request.Notes?.Length > 500 || request.EarlyOrLateReason?.Length > 500 || request.DiscrepancyReason?.Length > 500)
            throw new BusinessException("NOTE_TOO_LONG", "Ghi chú và lý do tối đa 500 ký tự.");

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

        var (cashSales, bankRevenue, ordersCount) = await _shiftRepository.GetShiftSalesMetricsAsync(shiftId, cancellationToken);
        shift.TotalBankRevenue = bankRevenue;
        shift.TotalCashSales = cashSales;
        shift.TotalOrdersCount = ordersCount;
        shift.ExpectedCash = shift.OpeningFloat + cashSales + shift.TotalCashIn - shift.TotalCashOut;

        shift.ActualCashCounted = request.ActualCashCounted;
        shift.Discrepancy = shift.ActualCashCounted - shift.ExpectedCash;
        if (shift.Discrepancy != 0 && string.IsNullOrWhiteSpace(request.DiscrepancyReason))
            throw new BusinessException("DISCREPANCY_REASON_REQUIRED", "Vui lòng nhập lý do chênh lệch tiền trong két.");
        shift.DiscrepancyReason = request.DiscrepancyReason?.Trim();

        await _shiftRepository.AddCloseConfirmationAsync(new ShiftCloseConfirmation
        {
            TenantId = _tenantContext.CurrentTenantId,
            ShiftId = shiftId,
            UserId = userId,
            UserName = userName,
            ConfirmedCountedCash = request.ActualCashCounted,
            ConfirmedAtUtc = DateTime.UtcNow
        }, cancellationToken);
        shift.RetainedFloatAmount = request.RetainedFloatAmount;
        shift.DroppedToSafeAmount = request.DroppedToSafeAmount;
        shift.ReceiverUserId = request.ReceiverUserId;
        shift.ClosedAtUtc = DateTime.UtcNow;
        shift.ClosedByUserId = userId;
        var closingEligibility = await CheckCashierShiftEligibilityAsync(
            shift.BranchId, userId, _tenantContext.CurrentRole, cancellationToken);
        shift.ClosedScheduleId = closingEligibility.ActiveSchedule?.ScheduleId;
        shift.Status = ShiftState.Closed;
        shift.HandoverStatus = HandoverStatus.PENDING;
        shift.SnapshotLocked = true;
        shift.CashbookPostingStatus = CashbookPostingStatus.NotPosted;

        if (!string.IsNullOrWhiteSpace(request.EarlyOrLateReason))
        {
            var reasonTag = $"[GIẢI TRÌNH GIỜ CHỐT CA: {request.EarlyOrLateReason.Trim()}]";
            if (shift.Notes == null || !shift.Notes.Contains(request.EarlyOrLateReason.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                shift.Notes = string.IsNullOrWhiteSpace(shift.Notes)
                    ? reasonTag
                    : $"{shift.Notes} | {reasonTag}";
            }
        }

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
        if (request.Reason.Length > 500 || request.Notes?.Length > 500)
            throw new BusinessException("NOTE_TOO_LONG", "Lý do và ghi chú tối đa 500 ký tự.");
        if (request.RetainedFloatAmount < 0 || request.DroppedToSafeAmount < 0)
            throw new BusinessException("INVALID_CASH_ALLOCATION", "Tiền để lại két và tiền nộp két không được âm.");

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
        if (shift.CashierUserId == ownerUserId)
            throw new BusinessException("CANNOT_FORCE_CLOSE_OWN_SHIFT", "Chủ cửa hàng phải tự chốt ca của mình theo luồng đóng ca thông thường.");
        if (shift.CashMovements.Any(m => m.Status == CashMovementStatus.PendingApproval))
            throw new BusinessException("PENDING_CASH_MOVEMENTS", "Không thể buộc đóng khi còn phiếu thu/chi chờ duyệt.");

        var (cashSales, bankRevenue, ordersCount) = await _shiftRepository.GetShiftSalesMetricsAsync(shiftId, cancellationToken);
        shift.TotalBankRevenue = bankRevenue;
        shift.TotalCashSales = cashSales;
        shift.TotalOrdersCount = ordersCount;
        shift.ExpectedCash = shift.OpeningFloat + cashSales + shift.TotalCashIn - shift.TotalCashOut;

        shift.ActualCashCounted = request.ActualCashCounted;
        shift.Discrepancy = shift.ActualCashCounted - shift.ExpectedCash;
        shift.RetainedFloatAmount = request.RetainedFloatAmount;
        shift.DroppedToSafeAmount = request.DroppedToSafeAmount;
        shift.ClosedAtUtc = DateTime.UtcNow;
        shift.ClosedByUserId = ownerUserId;
        shift.ForceCloseReason = request.Reason.Trim();
        shift.Status = ShiftState.ForceClosed;
        shift.HandoverStatus = HandoverStatus.CONFIRMED;
        shift.SnapshotLocked = true;
        shift.CashbookPostingStatus = CashbookPostingStatus.NotPosted;

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDetailResponse(shift);
    }

    private async Task RecordShiftCashbookSettlementAsync(
        ShiftZReport shift,
        decimal cashSales,
        decimal bankRevenue,
        int ordersCount,
        bool recordSafeDrop,
        CancellationToken cancellationToken)
    {
        if (_cashbookService == null) return;

        try
        {
            var existingEntries = await _cashbookService.GetEntriesAsync(
                accountId: null,
                branchId: shift.BranchId,
                fromDate: null,
                toDate: null,
                page: 1,
                pageSize: 50,
                entryType: null,
                sourceType: null,
                keyword: shift.ShiftCode,
                ct: cancellationToken) ?? new List<MT_GRMS.Application.Cashbook.Dtos.CashbookEntryResponse>();

            // 1. Gộp doanh thu chuyển khoản VietQR của cả ca vào Ngân hàng Chủ tiệm (1 bút toán/ca)
            if (bankRevenue > 0 && !existingEntries.Any(e => e.AccountType == "Bank" && e.ReferenceCode == shift.ShiftCode))
            {
                var paymentCfg = !string.IsNullOrWhiteSpace(shift.TenantId)
                    ? await _shiftRepository.GetPaymentConfigAsync(shift.TenantId, cancellationToken)
                    : null;

                var bankDisplayName = !string.IsNullOrWhiteSpace(paymentCfg?.BankName)
                    ? paymentCfg.BankName
                    : (!string.IsNullOrWhiteSpace(paymentCfg?.BankCode) ? $"Ngân hàng {paymentCfg.BankCode}" : null);

                var bank = await _cashbookService.GetOrCreateBankAccountAsync(
                    shift.BranchId,
                    bankDisplayName,
                    paymentCfg?.BankAccountNumber,
                    paymentCfg?.BankAccountName,
                    cancellationToken);

                if (bank != null && bank.Id != Guid.Empty)
                {
                    await _cashbookService.ReceiveCashAsync(
                        bank.Id,
                        FinanceAccountType.Bank,
                        bankRevenue,
                        "POS_SALE",
                        shift.Id,
                        shift.ShiftCode,
                        $"Kết toán doanh thu Chuyển khoản/VietQR ca {shift.ShiftCode} ({ordersCount} đơn hàng)",
                        cancellationToken);
                }
            }

            // 2. Gộp doanh thu tiền mặt của cả ca vào Két quầy thu ngân (1 bút toán/ca)
            if (cashSales > 0 && !existingEntries.Any(e => e.AccountType == "Till" && e.ReferenceCode == shift.ShiftCode))
            {
                var till = await _cashbookService.GetOrCreateTillAsync(shift.BranchId, "Két quầy chính", cancellationToken);
                if (till != null && till.Id != Guid.Empty)
                {
                    await _cashbookService.ReceiveCashAsync(
                        till.Id,
                        FinanceAccountType.Till,
                        cashSales,
                        "POS_SALE",
                        shift.Id,
                        shift.ShiftCode,
                        $"Kết toán doanh thu Tiền mặt ca {shift.ShiftCode} ({ordersCount} đơn hàng)",
                        cancellationToken);
                }
            }

            // 3. Khi Chủ cửa hàng xác nhận bàn giao (hoặc đóng ca cưỡng bức): Ghi nhận tiền nộp về Két sắt Chủ tiệm
            if (recordSafeDrop && shift.DroppedToSafeAmount > 0 && !existingEntries.Any(e => e.AccountType == "Safe" && e.ReferenceCode == shift.ShiftCode))
            {
                var safe = await _cashbookService.GetOrCreateSafeAsync(shift.BranchId, cancellationToken);
                if (safe != null && safe.Id != Guid.Empty)
                {
                    await _cashbookService.ReceiveCashAsync(
                        safe.Id,
                        FinanceAccountType.Safe,
                        shift.DroppedToSafeAmount,
                        "CASH_DROP",
                        shift.Id,
                        shift.ShiftCode,
                        $"Thu tiền mặt bàn giao kết ca {shift.ShiftCode} ({shift.ShiftName}) về Két sắt Chủ tiệm",
                        cancellationToken);
                }
            }
        }
        catch
        {
            // Không làm gián đoạn luồng đóng ca/bàn giao nếu sổ quỹ chưa khởi tạo trong môi trường test độc lập
        }
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
            .GroupBy(p => p.UserId)
            .Select(g => g.First())
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
            confirmations,
            shift.BusinessDate,
            shift.DiscrepancyReason,
            shift.OpenedScheduleId,
            shift.ClosedScheduleId
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

        // 2. Tra cứu hồ sơ nhân viên qua UserId (ưu tiên CurrentRawUserId cho tài khoản string ID như usr_employee_*)
        var rawCurrentUserId = (userId == Guid.Empty || userId == _tenantContext.CurrentUserId)
            ? _tenantContext.CurrentRawUserId
            : null;
        var employee = !string.IsNullOrWhiteSpace(rawCurrentUserId)
            ? await _shiftRepository.GetEmployeeByUserIdAsync(rawCurrentUserId, cancellationToken)
            : null;
        if (employee == null && userId != Guid.Empty && !string.Equals(rawCurrentUserId, userId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            employee = await _shiftRepository.GetEmployeeByUserIdAsync(userId.ToString(), cancellationToken);
        }
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

        // 4. Lấy lịch làm việc hôm nay của nhân viên tại chi nhánh (kèm các ca xuyên đêm mở từ hôm qua vắt sang sáng nay)
        var todaySchedules = await _shiftRepository.GetCashierSchedulesAsync(employee.Id, branchId, todayDate, cancellationToken)
            ?? Array.Empty<Domain.Entities.HR.EmployeeSchedule>();
        var yesterdaySchedules = await _shiftRepository.GetCashierSchedulesAsync(employee.Id, branchId, todayDate.AddDays(-1), cancellationToken)
            ?? Array.Empty<Domain.Entities.HR.EmployeeSchedule>();
        var overnightFromYesterday = yesterdaySchedules
            .Where(s => s.IsCheckInNextDay || s.PlannedEndTime < s.PlannedStartTime)
            .ToList();

        var schedules = overnightFromYesterday.Concat(todaySchedules).ToList();
        if (schedules.Count == 0)
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

        // 5. Kiểm tra thời gian hiện tại có nằm trong ca trực nào (kèm dung sai 30 phút, hỗ trợ ca all-time 00:00:00 - 23:59:59)
        var buffer = TimeSpan.FromMinutes(30);
        foreach (var s in schedules)
        {
            var startWindow = (s.PlannedStartTime == TimeSpan.Zero || s.AllowedCheckInStart != TimeSpan.Zero)
                ? s.AllowedCheckInStart
                : (s.PlannedStartTime > buffer ? s.PlannedStartTime.Subtract(buffer) : TimeSpan.Zero);

            var endWindow = (s.AllowedCheckInEnd >= new TimeSpan(23, 59, 0) || s.PlannedEndTime >= new TimeSpan(23, 59, 0))
                ? TimeSpan.FromDays(1)
                : (s.AllowedCheckInEnd != TimeSpan.Zero ? s.AllowedCheckInEnd : s.PlannedEndTime.Add(buffer));

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
                bool isFullTime = employee.SalaryType == SalaryType.Monthly;
                var todayStartUtc = todayDate.AddHours(-7);
                var todayEndUtc = todayStartUtc.AddDays(1);
                var todayShifts = await _shiftRepository.GetHistoryAsync(branchId, todayStartUtc, todayEndUtc, cancellationToken);
                var empUserGuid = !string.IsNullOrWhiteSpace(employee.UserId)
                    ? TenantContext.ResolveDeterministicGuid(employee.UserId)
                    : userId;

                int closedByEmpCount = 0;
                if (todayShifts != null)
                {
                    closedByEmpCount = todayShifts.Count(sh =>
                        sh.Status == ShiftState.Closed &&
                        (sh.ClosedByUserId == empUserGuid ||
                         (userId != Guid.Empty && sh.ClosedByUserId == userId) ||
                         sh.CashierUserId == empUserGuid ||
                         (userId != Guid.Empty && sh.CashierUserId == userId)));
                }

                var activeShift = await _shiftRepository.GetActiveShiftAsync(branchId, null, cancellationToken);
                if (activeShift == null && todayShifts != null)
                {
                    // Khóa theo đúng lịch phân công, không khóa nhân viên cả ngày.
                    // Người làm liên tiếp ca sau vẫn được mở bằng ScheduleId khác.
                    bool hasClosedCurrentSchedule = todayShifts.Any(sh =>
                        sh.Status == ShiftState.Closed &&
                        (sh.OpenedScheduleId == s.Id || sh.ClosedScheduleId == s.Id));

                    if (hasClosedCurrentSchedule)
                    {
                        return new CashierShiftEligibilityDto(
                            IsAllowed: false,
                            IsExempt: false,
                            Role: roleUpper,
                            EmployeeName: employee.FullName,
                            Reason: $"Bạn ({employee.FullName}) đã chốt ca làm việc ({s.ShiftName}) hôm nay. Sau khi đã chốt ca, nhân viên không được phép mở lại ca mới (ca tiếp theo do nhân viên ca sau mở).",
                            ActiveSchedule: new CashierShiftInfo(
                                s.Id,
                                s.ShiftCode,
                                s.ShiftName,
                                s.PlannedStartTime,
                                s.PlannedEndTime
                            ),
                            IsFullTime: isFullTime,
                            HasClosedShiftToday: true,
                            SalaryType: employee.SalaryType.ToString()
                        );
                    }
                }

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
                    ),
                    IsFullTime: isFullTime,
                    HasClosedShiftToday: closedByEmpCount > 0,
                    SalaryType: employee.SalaryType.ToString()
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


    private async Task EnsureClosingParticipantAsync(
        ShiftZReport shift,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var role = (_tenantContext.CurrentRole ?? string.Empty).ToUpperInvariant();
        if (role.Contains("OWNER") || role.Contains("ADMIN") || role.Contains("MANAGER"))
            return;

        if (shift.CashierUserId == userId)
            return;

        var participant = await _shiftRepository.GetParticipantAsync(shift.Id, userId, cancellationToken);
        if (participant == null)
        {
            throw new UnauthorizedAccessException(
                "Bạn không thuộc nhóm nhân viên thực tế tham gia ca này nên không thể thao tác chốt ca.");
        }
    }
}
