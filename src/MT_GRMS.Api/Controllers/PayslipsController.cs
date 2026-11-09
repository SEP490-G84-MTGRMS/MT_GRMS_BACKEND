using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Payslips;
using MT_GRMS.Application.HR.Payslips.Dtos;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Api.Controllers;

[Authorize(Roles = SystemRoles.AllStaffAndOwner)]
[Route("api/payslips")]
public class PayslipsController : BaseApiController
{
    private readonly IPayslipService _payslipService;
    private readonly IEmployeeRepository? _employeeRepository;
    private readonly ITenantContext? _tenantContext;

    public PayslipsController(
        IPayslipService payslipService,
        IEmployeeRepository? employeeRepository = null,
        ITenantContext? tenantContext = null)
    {
        _payslipService = payslipService;
        _employeeRepository = employeeRepository;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// GET /api/v1/payslips
    /// Lấy danh sách phiếu lương có phân trang và bộ lọc (kỳ tháng, chi nhánh, trạng thái, từ khóa).
    /// </summary>
    [Authorize(Roles = SystemRoles.AllStaffAndOwner)]
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<PayslipSummaryDto>>>> GetPayslipsPaged(
        [FromQuery] string? keyword,
        [FromQuery] string? periodMonth,
        [FromQuery] Guid? branchId,
        [FromQuery] PayslipStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var currentRole = User.FindFirst(ClaimTypes.Role)?.Value
                       ?? User.FindFirst("role")?.Value
                       ?? _tenantContext?.CurrentRole;

        var isOwnerOrAdmin = User.IsInRole(SystemRoles.StoreOwner) || User.IsInRole(SystemRoles.Admin)
                          || string.Equals(currentRole, SystemRoles.StoreOwner, StringComparison.OrdinalIgnoreCase)
                          || string.Equals(currentRole, SystemRoles.Admin, StringComparison.OrdinalIgnoreCase);

        Guid? employeeId = null;
        if (!isOwnerOrAdmin)
        {
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? User.FindFirst("sub")?.Value
                             ?? (_tenantContext != null && _tenantContext.CurrentUserId != Guid.Empty ? _tenantContext.CurrentUserId.ToString() : null);

            if (!string.IsNullOrWhiteSpace(currentUserId))
            {
                var emp = await _employeeRepository.GetByUserIdAsync(currentUserId, ct);
                if (emp != null)
                {
                    employeeId = emp.Id;
                }
                else
                {
                    return Ok(ApiResponse<PagedResult<PayslipSummaryDto>>.Ok(new PagedResult<PayslipSummaryDto>
                    {
                        Items = Array.Empty<PayslipSummaryDto>(),
                        Page = page,
                        PageSize = pageSize,
                        TotalItems = 0
                    }));
                }
            }
        }

        var filter = new PayslipQueryFilter(
            Keyword: keyword,
            PeriodMonth: periodMonth,
            BranchId: branchId,
            Status: status,
            Page: page,
            PageSize: pageSize,
            EmployeeId: employeeId
        );

        var result = await _payslipService.GetPayslipsPagedAsync(filter, ct);
        return Ok(ApiResponse<PagedResult<PayslipSummaryDto>>.Ok(result));
    }

    /// <summary>
    /// GET /api/v1/payslips/{id}
    /// Lấy thông tin chi tiết một phiếu lương.
    /// </summary>
    [Authorize(Roles = SystemRoles.AllStaffAndOwner)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<PayslipResponse>>> GetPayslipById(
        Guid id,
        CancellationToken ct = default)
    {
        var result = await _payslipService.GetPayslipByIdAsync(id, ct);

        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? User.FindFirst("sub")?.Value
                         ?? (_tenantContext != null && _tenantContext.CurrentUserId != Guid.Empty ? _tenantContext.CurrentUserId.ToString() : null);

        var currentRole = User.FindFirst(ClaimTypes.Role)?.Value
                       ?? User.FindFirst("role")?.Value
                       ?? _tenantContext?.CurrentRole;

        var isOwnerOrAdmin = User.IsInRole(SystemRoles.StoreOwner) || User.IsInRole(SystemRoles.Admin)
                          || string.Equals(currentRole, SystemRoles.StoreOwner, StringComparison.OrdinalIgnoreCase)
                          || string.Equals(currentRole, SystemRoles.Admin, StringComparison.OrdinalIgnoreCase);

        if (!isOwnerOrAdmin)
        {
            if (string.IsNullOrWhiteSpace(currentUserId) || result.UserId != currentUserId)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(ErrorCodes.Forbidden, "Bạn không có quyền truy cập phiếu lương của nhân sự khác."));
            }
        }

        return Ok(ApiResponse<PayslipResponse>.Ok(result));
    }

    /// <summary>
    /// POST /api/v1/payslips/generate-batch
    /// Tự động tổng hợp dữ liệu chấm công và khởi tạo/cập nhật bảng lương tháng theo lô.
    /// </summary>
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
    [HttpPost("generate-batch")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PayslipResponse>>>> GeneratePayrollBatch(
        [FromBody] GeneratePayrollBatchRequest request,
        CancellationToken ct = default)
    {
        var result = await _payslipService.GenerateMonthlyPayrollAsync(request, ct);
        return Ok(ApiResponse<IReadOnlyList<PayslipResponse>>.Ok(result, "Khởi tạo bảng lương thành công."));
    }

    /// <summary>
    /// POST /api/payslips/recalculate
    /// Tính toán lại bảng lương cho kỳ tháng (bảo lưu các phiếu đã chi trả, chỉ tính lại các phiếu chưa thanh toán).
    /// </summary>
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
    [HttpPost("recalculate")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PayslipResponse>>>> RecalculatePayroll(
        [FromBody] GeneratePayrollBatchRequest request,
        CancellationToken ct = default)
    {
        var result = await _payslipService.GenerateMonthlyPayrollAsync(request, ct);
        return Ok(ApiResponse<IReadOnlyList<PayslipResponse>>.Ok(result, "Tính toán lại bảng lương thành công."));
    }

    /// <summary>
    /// POST /api/v1/payslips/{id}/pay
    /// Thực hiện chi trả một phiếu lương đơn lẻ, tự động sinh phiếu chi Sổ quỹ.
    /// </summary>
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
    [HttpPost("{id:guid}/pay")]
    public async Task<ActionResult<ApiResponse<PayslipResponse>>> PayPayslip(
        Guid id,
        [FromBody] PayPayslipRequest request,
        CancellationToken ct = default)
    {
        var result = await _payslipService.PayPayslipAsync(id, request, ct);
        return Ok(ApiResponse<PayslipResponse>.Ok(result, "Thanh toán phiếu lương thành công."));
    }

    /// <summary>
    /// POST /api/v1/payslips/batch-pay
    /// Chi trả lương hàng loạt cho danh sách phiếu lương chưa thanh toán.
    /// </summary>
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
    [HttpPost("batch-pay")]
    public async Task<ActionResult<ApiResponse<BatchPayResultDto>>> BatchPayPayslips(
        [FromBody] BatchPayPayslipsRequest request,
        CancellationToken ct = default)
    {
        var result = await _payslipService.BatchPayPayslipsAsync(request, ct);
        return Ok(ApiResponse<BatchPayResultDto>.Ok(result, "Xử lý chi trả hàng loạt thành công."));
    }

    /// <summary>
    /// DELETE /api/v1/payslips/{id}
    /// Xóa phiếu lương (chỉ cho phép xóa khi phiếu chưa thanh toán).
    /// </summary>
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeletePayslip(
        Guid id,
        CancellationToken ct = default)
    {
        var result = await _payslipService.DeletePayslipAsync(id, ct);
        return Ok(ApiResponse<bool>.Ok(result, "Xóa phiếu lương thành công."));
    }
}
