using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Api.Filters;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PosShifts;
using MT_GRMS.Application.PosShifts.Dtos;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/pos/shifts")]
[Route("api/shifts")]
public class ShiftsController : BaseApiController
{
    private readonly IShiftService _shiftService;
    private readonly ITenantContext _tenantContext;

    public ShiftsController(IShiftService shiftService, ITenantContext tenantContext)
    {
        _shiftService = shiftService;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Mở ca làm việc POS mới. Tự động liên kết chuỗi tiền ca trước (OpeningFloatExpected).
    /// </summary>
    [HttpPost("open")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<ShiftDetailResponse>>> OpenShift(
        [FromBody] OpenShiftRequest request,
        CancellationToken cancellationToken)
    {
        var cashierId = _tenantContext.CurrentUserId != Guid.Empty
            ? _tenantContext.CurrentUserId
            : Guid.NewGuid();

        var shift = await _shiftService.OpenShiftAsync(request, cashierId, cancellationToken);
        return Ok(ApiResponse<ShiftDetailResponse>.Ok(shift, "Mở ca làm việc thành công."));
    }

    /// <summary>
    /// Lấy thông tin ca làm việc đang mở của chi nhánh / quầy thu ngân.
    /// </summary>
    [HttpGet("current")]
    public async Task<ActionResult<ApiResponse<ShiftDetailResponse>>> GetCurrentShift(
        [FromQuery] Guid branchId,
        [FromQuery] Guid? tillId,
        CancellationToken cancellationToken)
    {
        var targetBranchId = branchId != Guid.Empty ? branchId : (_tenantContext.CurrentBranchId ?? Guid.Empty);
        if (targetBranchId == Guid.Empty)
        {
            return BadRequest(ApiResponse<ShiftDetailResponse>.Fail("REQUIRED_BRANCH_ID", "Vui lòng cung cấp branchId chi nhánh."));
        }

        var shift = await _shiftService.GetCurrentShiftAsync(targetBranchId, tillId, cancellationToken);
        if (shift == null)
        {
            return NotFound(ApiResponse<ShiftDetailResponse>.Fail("NO_ACTIVE_SHIFT", "Hiện tại không có ca làm việc nào đang mở trên quầy này."));
        }

        return Ok(ApiResponse<ShiftDetailResponse>.Ok(shift));
    }

    /// <summary>
    /// Lấy thông tin chi tiết ca làm việc theo ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ShiftDetailResponse>>> GetShiftById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var shift = await _shiftService.GetShiftByIdAsync(id, cancellationToken);
        if (shift == null)
        {
            return NotFound(ApiResponse<ShiftDetailResponse>.Fail("SHIFT_NOT_FOUND", $"Không tìm thấy ca làm việc với ID '{id}'."));
        }

        return Ok(ApiResponse<ShiftDetailResponse>.Ok(shift));
    }

    /// <summary>
    /// Tạo phiếu thu/chi lẻ trong ca (nạp tiền lẻ, chi vặt, rút nộp két...).
    /// </summary>
    [HttpPost("{id:guid}/cash-movements")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<ShiftCashMovementDto>>> AddCashMovement(
        [FromRoute] Guid id,
        [FromBody] CashMovementRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _shiftService.AddCashMovementAsync(id, request, cancellationToken);
        return Ok(ApiResponse<ShiftCashMovementDto>.Ok(result, "Tạo phiếu thu/chi két thành công."));
    }

    /// <summary>
    /// Chốt ca bán hàng POS và kết toán kiểm đếm tiền mặt.
    /// </summary>
    [HttpPost("{id:guid}/close")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<ShiftDetailResponse>>> CloseShift(
        [FromRoute] Guid id,
        [FromBody] CloseShiftRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _shiftService.CloseShiftAsync(id, request, cancellationToken);
        return Ok(ApiResponse<ShiftDetailResponse>.Ok(result, "Chốt ca làm việc thành công."));
    }

    /// <summary>
    /// Nhân viên nhận ca tiếp theo xác nhận bàn giao (CONFIRMED hoặc DISPUTED).
    /// </summary>
    [HttpPost("{id:guid}/confirm-handover")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<ShiftDetailResponse>>> ConfirmHandover(
        [FromRoute] Guid id,
        [FromBody] ConfirmHandoverRequest request,
        CancellationToken cancellationToken)
    {
        var receiverId = !string.IsNullOrWhiteSpace(_tenantContext.CurrentUserId.ToString()) && _tenantContext.CurrentUserId != Guid.Empty
            ? _tenantContext.CurrentUserId.ToString()
            : "cashier_next";

        var result = await _shiftService.ConfirmHandoverAsync(id, request, receiverId, cancellationToken);
        return Ok(ApiResponse<ShiftDetailResponse>.Ok(result, "Xác nhận bàn giao ca thành công."));
    }

    /// <summary>
    /// Lấy báo cáo Z-Report phục vụ in kết ca và đối soát tiền két.
    /// </summary>
    [HttpGet("{id:guid}/z-report")]
    public async Task<ActionResult<ApiResponse<ZReportResponse>>> GetZReport(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var report = await _shiftService.GetZReportAsync(id, cancellationToken);
        return Ok(ApiResponse<ZReportResponse>.Ok(report));
    }

    /// <summary>
    /// Tra cứu lịch sử các ca làm việc của chi nhánh.
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ShiftDetailResponse>>>> GetShiftHistory(
        [FromQuery] Guid branchId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var targetBranchId = branchId != Guid.Empty ? branchId : (_tenantContext.CurrentBranchId ?? Guid.Empty);
        if (targetBranchId == Guid.Empty)
        {
            return BadRequest(ApiResponse<IReadOnlyList<ShiftDetailResponse>>.Fail("REQUIRED_BRANCH_ID", "Vui lòng cung cấp branchId chi nhánh."));
        }

        var list = await _shiftService.GetShiftHistoryAsync(targetBranchId, fromDate, toDate, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ShiftDetailResponse>>.Ok(list));
    }

    /// <summary>
    /// Chủ cửa hàng duyệt phiếu chi vượt hạn mức.
    /// </summary>
    [HttpPost("/api/pos/cash-movements/{id:guid}/approve")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<ShiftCashMovementDto>>> ApproveCashMovement(
        [FromRoute] Guid id,
        [FromBody] ApproveCashMovementRequest? request,
        CancellationToken cancellationToken)
    {
        var approverName = !string.IsNullOrWhiteSpace(User.Identity?.Name)
            ? User.Identity.Name
            : "Chủ cửa hàng";

        var result = await _shiftService.ApproveCashMovementAsync(id, approverName, cancellationToken);
        return Ok(ApiResponse<ShiftCashMovementDto>.Ok(result, "Phê duyệt phiếu chi thành công."));
    }

    /// <summary>
    /// Chủ cửa hàng từ chối phiếu chi.
    /// </summary>
    [HttpPost("/api/pos/cash-movements/{id:guid}/reject")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<ShiftCashMovementDto>>> RejectCashMovement(
        [FromRoute] Guid id,
        [FromBody] RejectCashMovementRequest request,
        CancellationToken cancellationToken)
    {
        var approverName = !string.IsNullOrWhiteSpace(User.Identity?.Name)
            ? User.Identity.Name
            : "Chủ cửa hàng";

        var result = await _shiftService.RejectCashMovementAsync(id, approverName, request.Reason, cancellationToken);
        return Ok(ApiResponse<ShiftCashMovementDto>.Ok(result, "Đã từ chối phiếu chi."));
    }

    /// <summary>
    /// Thu ngân tự hủy phiếu chi đang chờ duyệt.
    /// </summary>
    [HttpPost("/api/pos/cash-movements/{id:guid}/cancel")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<ShiftCashMovementDto>>> CancelCashMovement(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _shiftService.CancelCashMovementAsync(id, _tenantContext.CurrentUserId, cancellationToken);
        return Ok(ApiResponse<ShiftCashMovementDto>.Ok(result, "Đã hủy phiếu chi."));
    }

    /// <summary>
    /// Chủ cửa hàng xác nhận đã nhận tiền mặt từ phiếu rút két (CashDrop).
    /// </summary>
    [HttpPost("/api/pos/cash-movements/{id:guid}/confirm-drop")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<ShiftCashMovementDto>>> ConfirmCashDrop(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var approverName = !string.IsNullOrWhiteSpace(User.Identity?.Name)
            ? User.Identity.Name
            : "Chủ cửa hàng";

        var result = await _shiftService.ConfirmCashDropAsync(id, approverName, cancellationToken);
        return Ok(ApiResponse<ShiftCashMovementDto>.Ok(result, "Đã xác nhận nhận tiền rút két."));
    }

    /// <summary>
    /// Lấy danh sách các phiếu chờ chủ xử lý (duyệt chi hoặc xác nhận nhận tiền rút két).
    /// </summary>
    [HttpGet("/api/pos/cash-movements/pending")]
    public async Task<ActionResult<ApiResponse<PendingMovementsSummaryResponse>>> GetPendingMovements(
        [FromQuery] Guid branchId,
        CancellationToken cancellationToken)
    {
        var targetBranchId = branchId != Guid.Empty ? branchId : (_tenantContext.CurrentBranchId ?? Guid.Empty);
        if (targetBranchId == Guid.Empty)
        {
            return BadRequest(ApiResponse<PendingMovementsSummaryResponse>.Fail("REQUIRED_BRANCH_ID", "Vui lòng cung cấp branchId chi nhánh."));
        }

        var result = await _shiftService.GetPendingMovementsSummaryAsync(targetBranchId, cancellationToken);
        return Ok(ApiResponse<PendingMovementsSummaryResponse>.Ok(result));
    }

    /// <summary>
    /// Lấy cấu hình hạn mức duyệt chi két POS.
    /// </summary>
    [HttpGet("/api/pos/settings/cash-movement")]
    public async Task<ActionResult<ApiResponse<CashMovementSettingsDto>>> GetCashMovementSettings(
        CancellationToken cancellationToken)
    {
        var result = await _shiftService.GetCashMovementSettingsAsync(cancellationToken);
        return Ok(ApiResponse<CashMovementSettingsDto>.Ok(result));
    }

    /// <summary>
    /// Cập nhật cấu hình hạn mức duyệt chi két POS.
    /// </summary>
    [HttpPut("/api/pos/settings/cash-movement")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<CashMovementSettingsDto>>> UpdateCashMovementSettings(
        [FromBody] UpdateCashMovementSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _shiftService.UpdateCashMovementSettingsAsync(request, cancellationToken);
        return Ok(ApiResponse<CashMovementSettingsDto>.Ok(result, "Cập nhật hạn mức duyệt chi két thành công."));
    }

    /// <summary>
    /// Bắt đầu kiểm đếm chốt ca: Chuyển ca sang Closing, tạm thời khóa tạo đơn hàng và phiếu chi két trên toàn chi nhánh.
    /// </summary>
    [HttpPost("{id:guid}/start-closing")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<ShiftDetailResponse>>> StartClosingShift(
        [FromRoute] Guid id,
        [FromBody] StartClosingShiftRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _tenantContext.CurrentUserId != Guid.Empty
            ? _tenantContext.CurrentUserId
            : Guid.NewGuid();

        var result = await _shiftService.StartClosingShiftAsync(id, userId, request, cancellationToken);
        return Ok(ApiResponse<ShiftDetailResponse>.Ok(result, "Đã bắt đầu kiểm đếm chốt ca. Quầy đã tạm thời khóa bán hàng."));
    }

    /// <summary>
    /// Hủy kiểm đếm chốt ca: Trả ca về trạng thái Open để tiếp tục bán hàng.
    /// </summary>
    [HttpPost("{id:guid}/cancel-closing")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<ShiftDetailResponse>>> CancelClosingShift(
        [FromRoute] Guid id,
        [FromBody] CancelClosingShiftRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _tenantContext.CurrentUserId != Guid.Empty
            ? _tenantContext.CurrentUserId
            : Guid.NewGuid();

        var result = await _shiftService.CancelClosingShiftAsync(id, userId, request, cancellationToken);
        return Ok(ApiResponse<ShiftDetailResponse>.Ok(result, "Đã hủy kiểm đếm chốt ca. Quầy đã mở lại để tiếp tục bán hàng."));
    }

    /// <summary>
    /// Cập nhật số tiền thực đếm trong két khi ca đang Closing.
    /// </summary>
    [HttpPut("{id:guid}/counted-cash")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<ShiftDetailResponse>>> UpdateCountedCash(
        [FromRoute] Guid id,
        [FromBody] UpdateCountedCashRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _tenantContext.CurrentUserId != Guid.Empty
            ? _tenantContext.CurrentUserId
            : Guid.NewGuid();

        var result = await _shiftService.UpdateCountedCashAsync(id, userId, request, cancellationToken);
        return Ok(ApiResponse<ShiftDetailResponse>.Ok(result, "Cập nhật số tiền thực đếm thành công."));
    }

    /// <summary>
    /// Nhân viên xác nhận số đếm và đóng ca chung của chi nhánh.
    /// </summary>
    [HttpPost("{id:guid}/confirm-close")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<ShiftDetailResponse>>> ConfirmCloseShift(
        [FromRoute] Guid id,
        [FromBody] ConfirmCloseShiftRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _tenantContext.CurrentUserId != Guid.Empty
            ? _tenantContext.CurrentUserId
            : Guid.NewGuid();

        var userName = !string.IsNullOrWhiteSpace(User.Identity?.Name)
            ? User.Identity.Name
            : "Thu ngân";

        var result = await _shiftService.ConfirmCloseShiftAsync(id, userId, userName, request, cancellationToken);
        return Ok(ApiResponse<ShiftDetailResponse>.Ok(result, "Chốt ca bán hàng thành công."));
    }

    /// <summary>
    /// Chủ cửa hàng đóng ca cưỡng bức từ xa khi nhân viên đã ra về mà chưa chốt ca.
    /// </summary>
    [HttpPost("{id:guid}/force-close")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<ShiftDetailResponse>>> ForceCloseShift(
        [FromRoute] Guid id,
        [FromBody] ForceCloseShiftRequest request,
        CancellationToken cancellationToken)
    {
        var role = _tenantContext.CurrentRole;
        if (!string.Equals(role, "STORE_OWNER", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(role, "ADMIN", StringComparison.OrdinalIgnoreCase) &&
            !User.IsInRole("STORE_OWNER") &&
            !User.IsInRole("ADMIN"))
        {
            return Forbid();
        }

        var ownerId = _tenantContext.CurrentUserId != Guid.Empty
            ? _tenantContext.CurrentUserId
            : Guid.NewGuid();

        var result = await _shiftService.ForceCloseShiftAsync(id, ownerId, request, cancellationToken);
        return Ok(ApiResponse<ShiftDetailResponse>.Ok(result, "Đóng ca cưỡng bức thành công."));
    }
}

