using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Api.Controllers;

/// <summary>
/// KHUNG MẪU CONTROLLER TẦNG API (Presentation Layer):
/// - CHỈ làm việc với DTO (ProductCreateRequest, ProductResponse).
/// - TUYỆT ĐỐI KHÔNG using MT_GRMS.Domain.Entities.
/// - Đóng gói kết quả trả về trong ApiResponse<T> chuẩn mực.
/// </summary>
[ApiController]
[Route("api/v1/products")]
public class ProductsController : BaseApiController
{
    private readonly ICatalogService _catalogService;

    public ProductsController(ICatalogService catalogService)
    {
        _catalogService = catalogService;
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
}
