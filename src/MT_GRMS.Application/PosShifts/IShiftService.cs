using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.PosShifts.Dtos;

namespace MT_GRMS.Application.PosShifts;

public interface IShiftService
{
    /// <summary>
    /// Mở ca làm việc POS mới. Tự động kiểm tra ràng buộc duy nhất 1 ca mở trên mỗi quầy,
    /// đồng thời kế thừa số tiền bàn giao từ ca trước (OpeningFloatExpected).
    /// </summary>
    Task<ShiftDetailResponse> OpenShiftAsync(OpenShiftRequest request, Guid cashierUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy thông tin ca làm việc đang mở của chi nhánh / quầy thu ngân.
    /// </summary>
    Task<ShiftDetailResponse?> GetCurrentShiftAsync(Guid branchId, Guid? tillId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy chi tiết ca làm việc theo ID.
    /// </summary>
    Task<ShiftDetailResponse?> GetShiftByIdAsync(Guid shiftId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Thêm phiếu thu/chi tiền lẻ trong ca (nạp tiền lẻ, chi vặt, rút nộp két...).
    /// </summary>
    Task<ShiftCashMovementDto> AddCashMovementAsync(Guid shiftId, CashMovementRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chốt ca bán hàng POS: Tính toán doanh thu thực tế từ Orders, kiểm đếm chênh lệch két,
    /// và kiểm tra ràng buộc RetainedFloatAmount + DroppedToSafeAmount == ActualCashCounted.
    /// </summary>
    Task<ShiftDetailResponse> CloseShiftAsync(Guid shiftId, CloseShiftRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Người nhận ca tiếp theo xác nhận bàn giao (CONFIRMED hoặc DISPUTED).
    /// </summary>
    Task<ShiftDetailResponse> ConfirmHandoverAsync(Guid shiftId, ConfirmHandoverRequest request, string receiverUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy báo cáo Z-Report phục vụ in hóa đơn hoặc đối soát cuối ngày.
    /// </summary>
    Task<ZReportResponse> GetZReportAsync(Guid shiftId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy lịch sử các ca làm việc của chi nhánh.
    /// </summary>
    Task<IReadOnlyList<ShiftDetailResponse>> GetShiftHistoryAsync(Guid branchId, DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default);
}

