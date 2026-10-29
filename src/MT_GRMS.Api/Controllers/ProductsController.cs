using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Application.Suppliers.Dtos;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/v1/products")]
public class ProductsController : BaseApiController
{
    private readonly ICatalogService _catalogService;
    private readonly ISupplierService _supplierService;

    public ProductsController(ICatalogService catalogService, ISupplierService supplierService)
    {
        _catalogService = catalogService;
        _supplierService = supplierService;
    }

    /// <summary>
    /// Lấy danh sách sản phẩm có phân trang, tìm kiếm đa tiêu chí và lọc theo danh mục
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetProducts([FromQuery] ProductQueryFilter filter, CancellationToken cancellationToken)
    {
        var result = await _catalogService.GetProductsPagedAsync(filter, cancellationToken);
        return Ok(ApiResponse<PagedResult<ProductResponse>>.Ok(result));
    }

    /// <summary>
    /// Lấy thông tin chi tiết một sản phẩm theo ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _catalogService.GetByIdAsync(id, cancellationToken);
        if (result == null)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Không tìm thấy sản phẩm."));
        }
        return Ok(ApiResponse<ProductResponse>.Ok(result));
    }

    /// <summary>
    /// Tạo mới một sản phẩm
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ProductCreateRequest request, CancellationToken cancellationToken)
    {
        var result = await _catalogService.CreateProductAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<ProductResponse>.Ok(result, "Tạo sản phẩm thành công."));
    }

    /// <summary>
    /// Cập nhật thông tin sản phẩm
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ProductUpdateRequest request, CancellationToken cancellationToken)
    {
        var result = await _catalogService.UpdateProductAsync(id, request, cancellationToken);
        return Ok(ApiResponse<ProductResponse>.Ok(result, "Cập nhật sản phẩm thành công."));
    }

    /// <summary>
    /// Xóa mềm một sản phẩm
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _catalogService.DeleteProductAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null!, "Xóa sản phẩm thành công."));
    }

    /// <summary>
    /// Tra cứu ngược danh sách nhà cung cấp cung ứng một sản phẩm (REQ-SP05)
    /// </summary>
    [HttpGet("{id:guid}/suppliers")]
    public async Task<IActionResult> GetSuppliersForProduct(
        Guid id,
        [FromQuery] bool onlyActive = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _supplierService.GetSuppliersForProductAsync(id, onlyActive, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ProductSupplierMappingDto>>.Ok(result));
    }
}
