using System;
using System.Collections.Generic;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.PosShifts.Dtos;

/// <summary>
/// Yêu cầu mở ca làm việc POS mới.
/// </summary>
public record OpenShiftRequest(
    Guid BranchId,
    Guid TillId,
    string ShiftName,
    decimal OpeningFloat,
    string? Notes = null
);

/// <summary>
/// Yêu cầu tạo phiếu thu/chi lẻ trong ca POS (nạp tiền lẻ, chi vặt, rút nộp két...).
/// </summary>
public record CashMovementRequest(
    CashMovementType MovementType,
    CashMovementCategory Category,
    decimal Amount,
    string Reason,
    string? CreatedByName = null
);

/// <summary>
/// Yêu cầu chốt ca POS và kết toán kiểm đếm tiền mặt.
/// Bắt buộc RetainedFloatAmount + DroppedToSafeAmount == ActualCashCounted.
/// </summary>
public record CloseShiftRequest(
    decimal ActualCashCounted,
    decimal RetainedFloatAmount,
    decimal DroppedToSafeAmount,
    string? ReceiverUserId = null,
    string? Notes = null
);

/// <summary>
/// Yêu cầu xác nhận hoặc khiếu nại bàn giao ca từ nhân viên ca kế tiếp.
/// </summary>
public record ConfirmHandoverRequest(
    HandoverStatus Status,
    string? DisputeReason = null
);

/// <summary>
/// Chi tiết một giao dịch thu/chi két lẻ trong ca.
/// </summary>
public record ShiftCashMovementDto(
    Guid Id,
    Guid ShiftId,
    CashMovementType MovementType,
    CashMovementCategory Category,
    decimal Amount,
    string Reason,
    string? CreatedByName,
    DateTime OccurredAtUtc,
    CashMovementStatus Status = CashMovementStatus.Recorded,
    string? ApprovedBy = null,
    DateTime? ApprovedAtUtc = null,
    string? RejectionReason = null,
    bool IsConfirmedByOwner = false,
    DateTime? ConfirmedByOwnerAtUtc = null
);

/// <summary>
/// Yêu cầu phê duyệt phiếu chi vượt hạn mức từ Chủ cửa hàng.
/// </summary>
public record ApproveCashMovementRequest(
    string? Note = null
);

/// <summary>
/// Yêu cầu từ chối phiếu chi từ Chủ cửa hàng kèm lý do bắt buộc.
/// </summary>
public record RejectCashMovementRequest(
    string Reason
);

/// <summary>
/// Cấu hình hạn mức phê duyệt thu chi tiền két POS.
/// </summary>
public record CashMovementSettingsDto(
    decimal CashOutApprovalThreshold,
    decimal CashOutShiftTotalLimit
);

/// <summary>
/// Cập nhật cấu hình hạn mức phê duyệt thu chi két POS.
/// </summary>
public record UpdateCashMovementSettingsRequest(
    decimal CashOutApprovalThreshold,
    decimal CashOutShiftTotalLimit
);

/// <summary>
/// Danh sách các yêu cầu chờ chủ xử lý (duyệt chi hoặc xác nhận nhận tiền rút két).
/// </summary>
public record PendingMovementsSummaryResponse(
    int PendingApprovalsCount,
    int UnconfirmedDropsCount,
    IReadOnlyList<ShiftCashMovementDto> Movements
);

/// <summary>
/// DTO thông tin chi tiết một ca làm việc và các phiếu thu chi phát sinh.
/// </summary>
public record ShiftDetailResponse(
    Guid Id,
    string TenantId,
    Guid BranchId,
    string? BranchName,
    Guid TillId,
    string ShiftCode,
    string ShiftName,
    Guid CashierUserId,
    string? ReceiverUserId,
    Guid? PrevShiftId,
    DateTime OpenedAtUtc,
    DateTime? ClosedAtUtc,
    decimal OpeningFloatExpected,
    decimal OpeningFloat,
    decimal OpeningDiscrepancy,
    decimal TotalCashIn,
    decimal TotalCashOut,
    decimal TotalBankRevenue,
    int TotalOrdersCount,
    decimal ExpectedCash,
    decimal? ActualCashCounted,
    decimal? Discrepancy,
    decimal RetainedFloatAmount,
    decimal DroppedToSafeAmount,
    HandoverStatus HandoverStatus,
    ShiftState Status,
    string? Notes,
    IReadOnlyList<ShiftCashMovementDto> CashMovements
);

/// <summary>
/// Báo cáo Z-Report phục vụ đối soát và in hóa đơn kết ca.
/// </summary>
public record ZReportResponse(
    Guid ShiftId,
    string ShiftCode,
    string ShiftName,
    Guid BranchId,
    string? BranchName,
    Guid TillId,
    DateTime OpenedAtUtc,
    DateTime? ClosedAtUtc,
    decimal OpeningFloat,
    decimal CashSales,
    decimal TotalCashIn,
    decimal TotalCashOut,
    decimal TotalBankRevenue,
    int TotalOrdersCount,
    decimal ExpectedCashInTill,
    decimal? ActualCashCounted,
    decimal? Discrepancy,
    decimal RetainedFloatAmount,
    decimal DroppedToSafeAmount,
    HandoverStatus HandoverStatus,
    string? ReceiverUserId,
    string? Notes,
    IReadOnlyList<ShiftCashMovementDto> CashMovements
);

