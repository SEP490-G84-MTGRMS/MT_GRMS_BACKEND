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
    private readonly IBranchProductService _branchProductService;

    public BranchesController(
        IBranchService branchService,
        IBranchProductService branchProductService)
    {
        _branchService = branchService;
        _branchProductService = branchProductService;
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
    /// Lấy bảng chỉ số tổng quan của các chi nhánh (Tổng số SKU, Tổng tồn kho SOH, Cảnh báo thiếu hàng)
    /// </summary>
    [HttpGet("summaries")]
    public async Task<IActionResult> GetBranchSummaries(CancellationToken cancellationToken)
    {
        var result = await _branchProductService.GetBranchSummariesAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<BranchSummaryDto>>.Ok(result));
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
    /// Lấy danh mục sản phẩm kinh doanh thuộc một chi nhánh có phân trang & bộ lọc
    /// </summary>
    [HttpGet("{id:guid}/products")]
    public async Task<IActionResult> GetBranchProducts(
        Guid id,
        [FromQuery] BranchProductQueryFilter filter,
        CancellationToken cancellationToken)
    {
        var result = await _branchProductService.GetBranchProductsAsync(id, filter, cancellationToken);
        return Ok(ApiResponse<PagedResult<BranchProductDto>>.Ok(result));
    }

    /// <summary>
    /// Gán một hoặc nhiều sản phẩm từ Master Catalog vào Chi nhánh
    /// </summary>
    [HttpPost("{id:guid}/products")]
    public async Task<IActionResult> AssignProducts(
        Guid id,
        [FromBody] AssignBranchProductsCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _branchProductService.AssignProductsAsync(id, command, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<BranchProductDto>>.Ok(result, "Gán sản phẩm vào chi nhánh thành công."));
    }

    /// <summary>
    /// Cập nhật giá bán lẻ niêm yết, định mức tồn tối thiểu và vị trí quầy kệ tại chi nhánh
    /// </summary>
    [HttpPut("{id:guid}/products/{productId:guid}")]
    public async Task<IActionResult> UpdateProduct(
        Guid id,
        Guid productId,
        [FromBody] UpdateBranchProductCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _branchProductService.UpdateProductAsync(id, productId, command, cancellationToken);
        return Ok(ApiResponse<BranchProductDto>.Ok(result, "Cập nhật thông tin sản phẩm tại chi nhánh thành công."));
    }

    /// <summary>
    /// Chuyển đổi trạng thái kinh doanh (Active / Inactive) của sản phẩm tại chi nhánh
    /// </summary>
    [HttpPatch("{id:guid}/products/{productId:guid}/status")]
    public async Task<IActionResult> ToggleProductStatus(
        Guid id,
        Guid productId,
        [FromBody] ToggleBranchProductStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _branchProductService.ToggleStatusAsync(id, productId, request.Status, cancellationToken);
        return Ok(ApiResponse<BranchProductDto>.Ok(result, "Cập nhật trạng thái kinh doanh thành công."));
    }

    /// <summary>
    /// Gỡ sản phẩm khỏi danh mục kinh doanh của chi nhánh (chặn nếu tồn kho thực tế SOH > 0)
    /// </summary>
    [HttpDelete("{id:guid}/products/{productId:guid}")]
    public async Task<IActionResult> RemoveProduct(
        Guid id,
        Guid productId,
        CancellationToken cancellationToken)
    {
        await _branchProductService.RemoveProductAsync(id, productId, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null!, "Gỡ sản phẩm khỏi chi nhánh thành công."));
    }
}
