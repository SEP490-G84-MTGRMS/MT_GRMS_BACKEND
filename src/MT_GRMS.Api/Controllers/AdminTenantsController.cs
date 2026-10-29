using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/v1/admin/tenants")]
[Authorize(Roles = "ADMIN")]
public class AdminTenantsController : BaseApiController
{
    private readonly ITenantService _tenantService;
    private readonly IValidator<LockTenantRequest> _lockValidator;
    private readonly IValidator<ExtendGraceRequest> _graceValidator;

    public AdminTenantsController(
        ITenantService tenantService,
        IValidator<LockTenantRequest> lockValidator,
        IValidator<ExtendGraceRequest> graceValidator)
    {
        _tenantService = tenantService;
        _lockValidator = lockValidator;
        _graceValidator = graceValidator;
    }

    /// <summary>
    /// Lấy danh sách khách thuê có phân trang và bộ lọc theo từ khóa, trạng thái, gói cước (REQ-ADMIN-BE-02)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetTenants([FromQuery] TenantFilterRequest filter, CancellationToken cancellationToken)
    {
        var result = await _tenantService.GetTenantsAsync(filter, cancellationToken);
        return Ok(ApiResponse<PagedResult<TenantSummaryResponse>>.Ok(result));
    }

    /// <summary>
    /// Lấy thông tin chi tiết một khách thuê bao gồm danh sách chi nhánh (REQ-ADMIN-BE-02)
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken cancellationToken)
    {
        try
        {
            var tenant = await _tenantService.GetByIdAsync(id, cancellationToken);
            return Ok(ApiResponse<TenantDetailResponse>.Ok(tenant));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<TenantDetailResponse>.Fail("TENANT_NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// Cập nhật thông tin hành chính của khách thuê (Tên chuỗi, MST, Hotline, Địa chỉ) (REQ-ADMIN-BE-02)
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTenant(string id, [FromBody] UpdateTenantRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _tenantService.UpdateTenantAsync(id, request, cancellationToken);
            return Ok(ApiResponse<TenantDetailResponse>.Ok(updated, "Cập nhật thông tin gian hàng thành công"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<TenantDetailResponse>.Fail("TENANT_NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// Áp dụng khóa gian hàng (SOFT lock chỉ cho đọc hoặc HARD lock chặn hoàn toàn) (REQ-ADMIN-BE-02)
    /// </summary>
    [HttpPost("{id}/lock")]
    public async Task<IActionResult> LockTenant(string id, [FromBody] LockTenantRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _lockValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
            return BadRequest(ApiResponse<bool>.Fail("VALIDATION_ERROR", errors));
        }

        try
        {
            var adminUsername = User.Identity?.Name ?? "SuperAdmin";
            await _tenantService.LockTenantAsync(id, request, adminUsername, cancellationToken);
            return Ok(ApiResponse<bool>.Ok(true, $"Đã áp dụng khóa {request.LockType.ToUpperInvariant()} thành công"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<bool>.Fail("TENANT_NOT_FOUND", ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<bool>.Fail("INVALID_LOCK_TYPE", ex.Message));
        }
    }

    /// <summary>
    /// Mở khóa gian hàng và khôi phục trạng thái hoạt động (REQ-ADMIN-BE-02)
    /// </summary>
    [HttpPost("{id}/unlock")]
    public async Task<IActionResult> UnlockTenant(string id, CancellationToken cancellationToken)
    {
        try
        {
            await _tenantService.UnlockTenantAsync(id, cancellationToken);
            return Ok(ApiResponse<bool>.Ok(true, "Mở khóa gian hàng thành công"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<bool>.Fail("TENANT_NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// Gia hạn thêm số ngày ân hạn (Grace Period) cho khách thuê (REQ-ADMIN-BE-02)
    /// </summary>
    [HttpPost("{id}/extend-grace")]
    public async Task<IActionResult> ExtendGracePeriod(string id, [FromBody] ExtendGraceRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _graceValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
            return BadRequest(ApiResponse<bool>.Fail("VALIDATION_ERROR", errors));
        }

        try
        {
            await _tenantService.ExtendGracePeriodAsync(id, request, cancellationToken);
            return Ok(ApiResponse<bool>.Ok(true, $"Đã gia hạn ân hạn thêm {request.AdditionalDays} ngày thành công"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<bool>.Fail("TENANT_NOT_FOUND", ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<bool>.Fail("INVALID_GRACE_DAYS", ex.Message));
        }
    }
}

