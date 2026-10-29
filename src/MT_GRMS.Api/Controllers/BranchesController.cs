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
}
