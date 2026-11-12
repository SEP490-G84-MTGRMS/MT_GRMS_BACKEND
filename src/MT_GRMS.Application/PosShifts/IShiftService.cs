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

    /// <summary>
    /// Chủ cửa hàng phê duyệt phiếu chi vượt hạn mức.
    /// </summary>
    Task<ShiftCashMovementDto> ApproveCashMovementAsync(Guid movementId, string approverName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chủ cửa hàng từ chối phiếu chi.
    /// </summary>
    Task<ShiftCashMovementDto> RejectCashMovementAsync(Guid movementId, string approverName, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nhân viên tự hủy phiếu chi của chính mình khi còn chờ duyệt (PendingApproval).
    /// </summary>
    Task<ShiftCashMovementDto> CancelCashMovementAsync(Guid movementId, Guid currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chủ cửa hàng xác nhận đã nhận tiền mặt từ phiếu rút két nộp chủ (CashDrop).
    /// </summary>
    Task<ShiftCashMovementDto> ConfirmCashDropAsync(Guid movementId, string ownerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách các phiếu chờ duyệt chi và phiếu rút két chờ chủ xác nhận.
    /// </summary>
    Task<PendingMovementsSummaryResponse> GetPendingMovementsSummaryAsync(Guid? branchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc cấu hình hạn mức duyệt chi két của cửa hàng.
    /// </summary>
    Task<CashMovementSettingsDto> GetCashMovementSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật cấu hình hạn mức duyệt chi két của cửa hàng.
    /// </summary>
    Task<CashMovementSettingsDto> UpdateCashMovementSettingsAsync(UpdateCashMovementSettingsRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bắt đầu kiểm đếm chốt ca (chuyển ca sang trạng thái Closing và khóa bán mọi máy).
    /// </summary>
    Task<ShiftDetailResponse> StartClosingShiftAsync(Guid shiftId, Guid userId, StartClosingShiftRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hủy kiểm đếm chốt ca (trả ca về Open để tiếp tục bán hàng).
    /// </summary>
    Task<ShiftDetailResponse> CancelClosingShiftAsync(Guid shiftId, Guid userId, CancelClosingShiftRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật số tiền thực đếm trong két khi ca đang Closing.
    /// </summary>
    Task<ShiftDetailResponse> UpdateCountedCashAsync(Guid shiftId, Guid userId, UpdateCountedCashRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nhân viên xác nhận số đếm và hoàn tất đóng ca chung.
    /// </summary>
    Task<ShiftDetailResponse> ConfirmCloseShiftAsync(Guid shiftId, Guid userId, string userName, ConfirmCloseShiftRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chủ cửa hàng đóng ca cưỡng bức từ xa kèm số đếm và lý do bắt buộc.
    /// </summary>
    Task<ShiftDetailResponse> ForceCloseShiftAsync(Guid shiftId, Guid ownerUserId, ForceCloseShiftRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ghi nhận nhân viên tham gia ca bán hàng chung.
    /// </summary>
    Task RecordParticipantAsync(Guid shiftId, Guid userId, string? userName, string? fullName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chuyển đổi mã chuỗi hoặc Guid chi nhánh sang GUID thật trong cơ sở dữ liệu.
    /// </summary>
    Task<Guid?> ResolveBranchIdAsync(string? branchIdOrCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra tính đủ điều kiện trực ca của nhân viên thu ngân để bán hàng POS (REQ-STRICT-SCHEDULE-VALIDATION).
    /// Miễn trừ kiểm tra cho vai trò STORE_OWNER / ADMIN / MANAGER.
    /// </summary>
    Task<CashierShiftEligibilityDto> CheckCashierShiftEligibilityAsync(Guid branchId, Guid userId, string? userRole, CancellationToken cancellationToken = default);
}

