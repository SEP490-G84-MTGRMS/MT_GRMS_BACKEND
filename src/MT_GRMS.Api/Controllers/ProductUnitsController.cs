using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/v1")]
public class ProductUnitsController : BaseApiController
{
    private readonly IProductUnitService _productUnitService;
    private readonly ILogger<ProductUnitsController> _logger;

    public ProductUnitsController(
        IProductUnitService productUnitService,
        ILogger<ProductUnitsController> logger)
    {
        _productUnitService = productUnitService;
        _logger = logger;
    }

    /// <summary>
    /// Lấy danh sách tất cả các đơn vị tính và quy đổi của một sản phẩm
    /// </summary>
    /// <param name="productId">ID sản phẩm</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    [HttpGet("products/{productId:guid}/units")]
    public async Task<IActionResult> GetUnitsByProductId(Guid productId, CancellationToken cancellationToken)
    {
        try
        {
            var units = await _productUnitService.GetUnitsByProductIdAsync(productId, cancellationToken);
            return Ok(ApiResponse<IReadOnlyList<ProductUnitResponse>>.Ok(units));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// Lấy chi tiết một đơn vị tính quy đổi theo ID
    /// </summary>
    /// <param name="id">ID đơn vị tính</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    [HttpGet("product-units/{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var unit = await _productUnitService.GetByIdAsync(id, cancellationToken);
        if (unit == null)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Không tìm thấy đơn vị tính."));
        }

        return Ok(ApiResponse<ProductUnitResponse>.Ok(unit));
    }

    /// <summary>
    /// Thêm mới đơn vị tính quy đổi cho một sản phẩm
    /// </summary>
    /// <param name="productId">ID sản phẩm cha</param>
    /// <param name="request">Thông tin đơn vị tính mới</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    [HttpPost("products/{productId:guid}/units")]
    public async Task<IActionResult> Create(
        Guid productId,
        [FromBody] ProductUnitCreateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _productUnitService.CreateAsync(productId, request, cancellationToken);
            return CreatedAtAction(
                nameof(GetById),
                new { id = result.Id },
                ApiResponse<ProductUnitResponse>.Ok(result, "Tạo đơn vị tính thành công."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("INVALID_OPERATION", ex.Message));
        }
    }

    /// <summary>
    /// Cập nhật thông tin đơn vị tính quy đổi
    /// </summary>
    /// <param name="id">ID đơn vị tính cần sửa</param>
    /// <param name="request">Thông tin cập nhật mới</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    [HttpPut("product-units/{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] ProductUnitUpdateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _productUnitService.UpdateAsync(id, request, cancellationToken);
            return Ok(ApiResponse<ProductUnitResponse>.Ok(result, "Cập nhật đơn vị tính thành công."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("INVALID_OPERATION", ex.Message));
        }
    }

    /// <summary>
    /// Xóa một đơn vị tính quy đổi
    /// </summary>
    /// <param name="id">ID đơn vị tính cần xóa</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    [HttpDelete("product-units/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _productUnitService.DeleteAsync(id, cancellationToken);
            return Ok(ApiResponse<object>.Ok(null!, "Xóa đơn vị tính thành công."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("INVALID_OPERATION", ex.Message));
        }
    }
}

