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


    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _catalogService.GetAllAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ProductResponse>>.Ok(result));
    }

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

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ProductCreateRequest request, CancellationToken cancellationToken)
    {
        var result = await _catalogService.CreateProductAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<ProductResponse>.Ok(result, "Tạo sản phẩm thành công."));
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

