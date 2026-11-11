using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Employees.Dtos;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Api.Controllers;

[Authorize(Roles = SystemRoles.AllStaffAndOwner)]
[ApiController]
[Route("api/employees")]
public class EmployeesController : BaseApiController
{
    private readonly IEmployeeService _employeeService;
    private readonly ITenantContext? _tenantContext;

    public EmployeesController(IEmployeeService employeeService, ITenantContext? tenantContext = null)
    {
        _employeeService = employeeService;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Lấy danh sách nhân viên có phân trang, tìm kiếm họ tên/mã/SĐT và lọc theo chi nhánh, trạng thái
    /// </summary>
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
    [HttpGet]
    public async Task<IActionResult> GetEmployees(
        [FromQuery] EmployeeQueryFilter filter,
        CancellationToken cancellationToken)
    {
        var result = await _employeeService.GetEmployeesPagedAsync(filter, cancellationToken);
        return Ok(ApiResponse<PagedResult<EmployeeSummaryDto>>.Ok(result));
    }

    /// <summary>
    /// Lấy thông tin hồ sơ nhân viên của tài khoản người dùng đang đăng nhập
    /// </summary>
    [Authorize(Roles = SystemRoles.AllStaffAndOwner)]
    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile(CancellationToken cancellationToken)
    {
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                         ?? User.FindFirst("sub")?.Value
                         ?? (_tenantContext != null && _tenantContext.CurrentUserId != Guid.Empty ? _tenantContext.CurrentUserId.ToString() : null);

        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Không xác định được danh tính người dùng hiện tại."));
        }

        var profile = await _employeeService.GetEmployeeByUserIdAsync(currentUserId, cancellationToken);
        if (profile == null)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Không tìm thấy hồ sơ nhân viên liên kết với tài khoản này."));
        }

        return Ok(ApiResponse<EmployeeResponse>.Ok(profile));
    }

    /// <summary>
    /// Lấy thông tin chi tiết một nhân viên theo ID
    /// </summary>
    [Authorize(Roles = SystemRoles.AllStaffAndOwner)]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _employeeService.GetEmployeeByIdAsync(id, cancellationToken);

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
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(ErrorCodes.Forbidden, "Bạn không có quyền truy cập hồ sơ nhân sự của người khác."));
                }
            }

            return Ok(ApiResponse<EmployeeResponse>.Ok(result));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// Tạo mới hồ sơ nhân viên (tự sinh mã NVxxxx và kiểm tra CCCD/SĐT trùng lặp)
    /// </summary>
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _employeeService.CreateEmployeeAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<EmployeeResponse>.Ok(result, "Tạo hồ sơ nhân viên thành công."));
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
        {
            var constraint = pgEx.ConstraintName ?? string.Empty;
            if (constraint.Contains("CitizenId", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(ApiResponse<object>.Fail("DUPLICATE_CITIZEN_ID", "Số CCCD đã tồn tại trong hệ thống. Vui lòng kiểm tra lại."));
            }
            if (constraint.Contains("EmployeeCode", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(ApiResponse<object>.Fail("DUPLICATE_CODE", "Mã nhân viên đã tồn tại trong hệ thống."));
            }
            if (constraint.Contains("Phone", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(ApiResponse<object>.Fail("DUPLICATE_PHONE", "Số điện thoại đã tồn tại trong hệ thống."));
            }
            return Conflict(ApiResponse<object>.Fail("DUPLICATE_KEY", "Dữ liệu nhân viên bị trùng lặp trong hệ thống."));
        }
        catch (BusinessException ex)
        {
            var status = ex.ErrorCode.StartsWith("DUPLICATE") || ex.ErrorCode == "CONFLICT"
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status422UnprocessableEntity;
            return StatusCode(status, ApiResponse<object>.Fail(ex.ErrorCode, ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", ex.Message));
        }
    }

    /// <summary>
    /// Cập nhật thông tin hồ sơ nhân viên và mức lương
    /// </summary>
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _employeeService.UpdateEmployeeAsync(id, request, cancellationToken);
            return Ok(ApiResponse<EmployeeResponse>.Ok(result));
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
        {
            var constraint = pgEx.ConstraintName ?? string.Empty;
            if (constraint.Contains("CitizenId", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(ApiResponse<object>.Fail("DUPLICATE_CITIZEN_ID", "Số CCCD đã tồn tại trong hệ thống. Vui lòng kiểm tra lại."));
            }
            if (constraint.Contains("Phone", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(ApiResponse<object>.Fail("DUPLICATE_PHONE", "Số điện thoại đã tồn tại trong hệ thống."));
            }
            return Conflict(ApiResponse<object>.Fail("DUPLICATE_KEY", "Dữ liệu nhân viên bị trùng lặp trong hệ thống."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (BusinessException ex)
        {
            var status = ex.ErrorCode.StartsWith("DUPLICATE") || ex.ErrorCode == "CONFLICT"
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status422UnprocessableEntity;
            return StatusCode(status, ApiResponse<object>.Fail(ex.ErrorCode, ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", ex.Message));
        }
    }

    /// <summary>
    /// Cập nhật trạng thái hoạt động của nhân viên (active / inactive)
    /// </summary>
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateEmployeeStatusRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _employeeService.UpdateStatusAsync(id, request.Status, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { message = "Cập nhật trạng thái nhân viên thành công." }));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", ex.Message));
        }
    }

    /// <summary>
    /// Cấp tài khoản đăng nhập Identity cho nhân viên
    /// </summary>
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
    [HttpPost("{id:guid}/identity-account")]
    public async Task<IActionResult> CreateIdentityAccount(
        Guid id,
        [FromBody] CreateIdentityAccountRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var role = string.IsNullOrWhiteSpace(request.Role) || string.Equals(request.Role, "EMPLOYEE", StringComparison.OrdinalIgnoreCase)
                ? SystemRoles.Staff
                : request.Role.Trim().ToUpper();

            var normalizedRequest = request with { Role = role };
            var userId = await _employeeService.CreateIdentityAccountAsync(id, normalizedRequest, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new
            {
                userId,
                username = normalizedRequest.Username,
                role = SystemRoles.Staff,
                message = "Cấp tài khoản đăng nhập cho nhân viên thành công."
            }));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (BusinessException ex)
        {
            var status = ex.ErrorCode.StartsWith("DUPLICATE") || ex.ErrorCode == "CONFLICT"
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status422UnprocessableEntity;
            return StatusCode(status, ApiResponse<object>.Fail(ex.ErrorCode, ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", ex.Message));
        }
    }

    /// <summary>
    /// Cập nhật tài khoản đăng nhập Identity cho nhân viên
    /// </summary>
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
    [HttpPut("{id:guid}/identity-account")]
    public async Task<IActionResult> UpdateIdentityAccount(
        Guid id,
        [FromBody] CreateIdentityAccountRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var role = string.IsNullOrWhiteSpace(request.Role) || string.Equals(request.Role, "EMPLOYEE", StringComparison.OrdinalIgnoreCase)
                ? SystemRoles.Staff
                : request.Role.Trim().ToUpper();

            var normalizedRequest = request with { Role = role };
            var userId = await _employeeService.UpdateIdentityAccountAsync(id, normalizedRequest, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new
            {
                userId,
                username = normalizedRequest.Username,
                role = SystemRoles.Staff,
                message = "Cập nhật tài khoản đăng nhập cho nhân viên thành công."
            }));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (BusinessException ex)
        {
            var status = ex.ErrorCode.StartsWith("DUPLICATE") || ex.ErrorCode == "CONFLICT"
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status422UnprocessableEntity;
            return StatusCode(status, ApiResponse<object>.Fail(ex.ErrorCode, ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", ex.Message));
        }
    }

    /// <summary>
    /// Xóa hoặc ngưng hoạt động nhân viên (Soft-delete)
    /// </summary>
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            await _employeeService.DeleteEmployeeAsync(id, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { message = "Đã xóa nhân viên thành công." }));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (BusinessException ex) when (ex.ErrorCode == "EMPLOYEE_HAS_WORK_HISTORY")
        {
            return Ok(ApiResponse<object>.Ok(
                new { action = "deactivated", status = "inactive", message = ex.Message },
                ex.Message));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message));
        }
    }

    /// <summary>
    /// Đăng ký hoặc cập nhật vector khuôn mặt FaceID cho nhân viên
    /// </summary>
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
    [HttpPost("{id:guid}/face-descriptor")]
    public async Task<IActionResult> RegisterFaceDescriptor(
        Guid id,
        [FromBody] RegisterFaceDescriptorRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.FaceDescriptor))
            {
                return BadRequest(ApiResponse<bool>.Fail("INVALID_FACE_DESCRIPTOR", "Vector khuôn mặt không được để trống."));
            }

            var result = await _employeeService.RegisterFaceDescriptorAsync(id, request.FaceDescriptor, request.FaceImageUrls, cancellationToken);
            return Ok(ApiResponse<bool>.Ok(result, "Cập nhật vector khuôn mặt FaceID thành công."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<bool>.Fail("NOT_FOUND", ex.Message));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<bool>.Fail("BAD_REQUEST", ex.Message));
        }
    }
}
