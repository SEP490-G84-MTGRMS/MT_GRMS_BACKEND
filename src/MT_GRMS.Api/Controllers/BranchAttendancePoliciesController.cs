using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.AttendancePolicies;
using MT_GRMS.Application.HR.AttendancePolicies.Dtos;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/v1/attendance-policies")]
[Route("api/attendance-policies")]
public class BranchAttendancePoliciesController : BaseApiController
{
    private readonly IBranchAttendancePolicyService _policyService;

    public BranchAttendancePoliciesController(IBranchAttendancePolicyService policyService)
    {
        _policyService = policyService;
    }

    /// <summary>
    /// Lấy chính sách chấm công của một chi nhánh (nếu chưa từng cấu hình sẽ trả về giá trị mặc định chuẩn)
    /// </summary>
    [HttpGet("branch/{branchId:guid}")]
    public async Task<IActionResult> GetByBranch(
        Guid branchId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _policyService.GetByBranchIdAsync(branchId, cancellationToken);
            return Ok(ApiResponse<AttendancePolicyResponse>.Ok(result));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// Thiết lập / Cập nhật chính sách chấm công cho chi nhánh (Upsert)
    /// </summary>
    [HttpPut("branch/{branchId:guid}")]
    public async Task<IActionResult> UpsertPolicy(
        Guid branchId,
        [FromBody] UpsertAttendancePolicyRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _policyService.UpsertPolicyAsync(branchId, request, cancellationToken);
            return Ok(ApiResponse<AttendancePolicyResponse>.Ok(result, "Cập nhật chính sách chấm công chi nhánh thành công."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.ErrorCode ?? "BAD_REQUEST", ex.Message));
        }
    }

    /// <summary>
    /// Sao chép chính sách chấm công từ một chi nhánh nguồn sang danh sách các chi nhánh đích
    /// </summary>
    [HttpPost("copy")]
    public async Task<IActionResult> CopyPolicy(
        [FromBody] CopyAttendancePolicyRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _policyService.CopyPolicyAsync(request, cancellationToken);
            return Ok(ApiResponse<CopyAttendancePolicyResultDto>.Ok(result, result.Message));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.ErrorCode ?? "BAD_REQUEST", ex.Message));
        }
    }
}

