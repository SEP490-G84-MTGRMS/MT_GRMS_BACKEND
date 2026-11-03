using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Branches;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/v1/branches")]
public class BranchesController : BaseApiController
{
    private readonly IBranchService _branchService;

    public BranchesController(IBranchService branchService)
    {
        _branchService = branchService;
    }

    /// <summary>
    /// Lấy hạn mức và số lượng chi nhánh đang hoạt động của gian hàng (REQ-BR-01)
    /// </summary>
    [HttpGet("quota")]
    public async Task<IActionResult> GetQuota(CancellationToken cancellationToken)
    {
        var result = await _branchService.GetBranchQuotaAsync(cancellationToken);
        return Ok(ApiResponse<BranchQuotaDto>.Ok(result));
    }

    /// <summary>
    /// Lấy danh sách chi nhánh có phân trang, tìm kiếm từ khóa và lọc trạng thái/mã chi nhánh (REQ-BR-01)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetBranches([FromQuery] BranchFilterQuery query, CancellationToken cancellationToken)
    {
        var result = await _branchService.GetPagedBranchesAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<BranchDto>>.Ok(result));
    }

    /// <summary>
    /// Lấy thông tin chi tiết một chi nhánh theo ID (REQ-BR-02)
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _branchService.GetBranchByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<BranchDto>.Ok(result));
    }

    /// <summary>
    /// Tạo mới một chi nhánh trong chuỗi cửa hàng (REQ-BR-02)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBranchRequest request, CancellationToken cancellationToken)
    {
        var result = await _branchService.CreateBranchAsync(request, cancellationToken);
        return StatusCode(201, ApiResponse<BranchDto>.Ok(result, "Tạo chi nhánh mới thành công."));
    }

    /// <summary>
    /// Cập nhật thông tin chi nhánh (REQ-BR-03)
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBranchRequest request, CancellationToken cancellationToken)
    {
        var result = await _branchService.UpdateBranchAsync(id, request, cancellationToken);
        return Ok(ApiResponse<BranchDto>.Ok(result, "Cập nhật thông tin chi nhánh thành công."));
    }

    /// <summary>
    /// Bật / Tắt trạng thái hoạt động của chi nhánh (REQ-BR-04)
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> ToggleStatus(Guid id, [FromBody] UpdateBranchStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _branchService.ToggleBranchStatusAsync(id, request, cancellationToken);
        return Ok(ApiResponse<BranchDto>.Ok(result, $"Đã {(request.IsActive ? "kích hoạt" : "tạm dừng")} hoạt động chi nhánh."));
    }
}
