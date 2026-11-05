using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Employees.Dtos;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/v1/employees")]
public class EmployeesController : BaseApiController
{
    private readonly IEmployeeService _employeeService;

    public EmployeesController(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    /// <summary>
    /// Lấy danh sách nhân viên có phân trang, tìm kiếm họ tên/mã/SĐT và lọc theo chi nhánh, trạng thái
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetEmployees(
        [FromQuery] EmployeeQueryFilter filter,
        CancellationToken cancellationToken)
    {
        var result = await _employeeService.GetEmployeesPagedAsync(filter, cancellationToken);
        return Ok(ApiResponse<PagedResult<EmployeeSummaryDto>>.Ok(result));
    }

    /// <summary>
    /// Lấy thông tin chi tiết một nhân viên theo ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _employeeService.GetEmployeeByIdAsync(id, cancellationToken);
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
    [HttpPost("{id:guid}/identity-account")]
    public async Task<IActionResult> CreateIdentityAccount(
        Guid id,
        [FromBody] CreateIdentityAccountRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = await _employeeService.CreateIdentityAccountAsync(id, request, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new
            {
                userId,
                username = request.Username,
                role = request.Role,
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
    /// Xóa hoặc ngưng hoạt động nhân viên (Soft-delete)
    /// </summary>
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
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message));
        }
    }
}
