using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/v1/admin/plans")]
[Authorize(Roles = "ADMIN")]
public class AdminPlansController : BaseApiController
{
    private readonly ISubscriptionPlanService _planService;
    private readonly IValidator<CreatePlanRequest> _createValidator;
    private readonly IValidator<UpdatePlanRequest> _updateValidator;

    public AdminPlansController(
        ISubscriptionPlanService planService,
        IValidator<CreatePlanRequest> createValidator,
        IValidator<UpdatePlanRequest> updateValidator)
    {
        _planService = planService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>
    /// Lấy danh sách gói cước dịch vụ SaaS có bộ lọc theo từ khóa và trạng thái (REQ-ADMIN-BE-01)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPlans([FromQuery] PlanFilterRequest filter, CancellationToken cancellationToken)
    {
        var plans = await _planService.GetPlansAsync(filter, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<PlanSummaryResponse>>.Ok(plans));
    }

    /// <summary>
    /// Lấy số liệu thống kê tổng quan KPI cho màn hình quản lý gói cước (REQ-ADMIN-BE-01)
    /// </summary>
    [HttpGet("kpis")]
    public async Task<IActionResult> GetKpis(CancellationToken cancellationToken)
    {
        var kpis = await _planService.GetKpisAsync(cancellationToken);
        return Ok(ApiResponse<PlanKpiBannerResponse>.Ok(kpis));
    }

    /// <summary>
    /// Lấy chi tiết thông tin gói cước bao gồm hạn ngạch Quotas và tính năng Features (REQ-ADMIN-BE-01)
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken cancellationToken)
    {
        try
        {
            var plan = await _planService.GetByIdAsync(id, cancellationToken);
            return Ok(ApiResponse<PlanDetailResponse>.Ok(plan));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<PlanDetailResponse>.Fail("PLAN_NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// Tạo mới một gói cước dịch vụ SaaS (REQ-ADMIN-BE-01)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreatePlan([FromBody] CreatePlanRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
            return BadRequest(ApiResponse<PlanDetailResponse>.Fail("VALIDATION_ERROR", errors));
        }

        try
        {
            var created = await _planService.CreatePlanAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, ApiResponse<PlanDetailResponse>.Ok(created, "Tạo gói dịch vụ thành công"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<PlanDetailResponse>.Fail("PLAN_DUPLICATE_CODE", ex.Message));
        }
    }

    /// <summary>
    /// Cập nhật thông tin gói cước dịch vụ SaaS (REQ-ADMIN-BE-01)
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePlan(string id, [FromBody] UpdatePlanRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
            return BadRequest(ApiResponse<PlanDetailResponse>.Fail("VALIDATION_ERROR", errors));
        }

        try
        {
            var updated = await _planService.UpdatePlanAsync(id, request, cancellationToken);
            return Ok(ApiResponse<PlanDetailResponse>.Ok(updated, "Cập nhật gói dịch vụ thành công"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<PlanDetailResponse>.Fail("PLAN_NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// Bật hoặc tắt trạng thái kích hoạt của gói cước (REQ-ADMIN-BE-01)
    /// </summary>
    [HttpPatch("{id}/status")]
    public async Task<IActionResult> ToggleStatus(string id, CancellationToken cancellationToken)
    {
        try
        {
            var newStatus = await _planService.ToggleStatusAsync(id, cancellationToken);
            var message = newStatus ? "Đã kích hoạt gói dịch vụ" : "Đã tạm dừng gói dịch vụ";
            return Ok(ApiResponse<bool>.Ok(newStatus, message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<bool>.Fail("PLAN_NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// Xóa gói cước dịch vụ (Chặn xóa nếu có khách thuê đang sử dụng) (REQ-ADMIN-BE-01)
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePlan(string id, CancellationToken cancellationToken)
    {
        try
        {
            await _planService.DeletePlanAsync(id, cancellationToken);
            return Ok(ApiResponse<bool>.Ok(true, "Xóa gói cước thành công"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<bool>.Fail("PLAN_NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<bool>.Fail("PLAN_IN_USE", ex.Message));
        }
    }
}

