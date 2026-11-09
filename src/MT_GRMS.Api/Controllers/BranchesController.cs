using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Branches;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/branches")]
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
    /// Lấy thông tin chi tiết đầy đủ kèm các thông số tổng quát (Sản phẩm, tồn kho, nhân sự) của chi nhánh
    /// </summary>
    [HttpGet("{id:guid}/detail")]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken cancellationToken)
    {
        var result = await _branchService.GetBranchDetailAsync(id, cancellationToken);
        return Ok(ApiResponse<BranchDetailDto>.Ok(result));
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

    /// <summary>
    /// Xóa chi nhánh khỏi chuỗi cửa hàng (Soft Delete)
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _branchService.DeleteBranchAsync(id, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true, "Xóa chi nhánh thành công."));
    }

    /// <summary>
    /// Lấy danh mục sản phẩm kinh doanh của tất cả chi nhánh hoặc lọc theo chi nhánh (REQ-BR-03)
    /// </summary>
    [HttpGet("products")]
    public async Task<IActionResult> GetAllBranchProducts(
        [FromQuery] Guid? branchId,
        [FromQuery] BranchProductQueryFilter filter,
        CancellationToken cancellationToken)
    {
        var result = await _branchProductService.GetBranchProductsAsync(branchId ?? Guid.Empty, filter, cancellationToken);
        return Ok(ApiResponse<PagedResult<BranchProductDto>>.Ok(result));
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
    /// Lấy sản phẩm khả dụng để gán vào chi nhánh (chỉ SP thuộc NCC đang liên kết chi nhánh)
    /// </summary>
    [HttpGet("{id:guid}/assignable-products")]
    public async Task<IActionResult> GetAssignableProducts(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _branchProductService.GetAssignableProductsAsync(id, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AssignableBranchProductDto>>.Ok(result));
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
