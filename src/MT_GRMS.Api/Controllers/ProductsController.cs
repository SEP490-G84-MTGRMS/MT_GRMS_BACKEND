using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Application.Suppliers.Dtos;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/products")]
[Route("api/v1/products")]
[Authorize]
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
    /// Tra cứu sản phẩm theo mã vạch Barcode hoặc SKU phục vụ bán hàng POS & máy quét mã vạch USB HID
    /// </summary>
    [HttpGet("barcode/{barcode}")]
    public async Task<IActionResult> GetByBarcode([FromRoute] string barcode, CancellationToken cancellationToken)
    {
        var result = await _catalogService.GetByBarcodeAsync(barcode, cancellationToken);
        if (result == null)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", $"Không tìm thấy sản phẩm với mã vạch hoặc mã hàng '{barcode}'."));
        }
        return Ok(ApiResponse<ProductResponse>.Ok(result));
    }

    /// <summary>
    /// Tạo mới một sản phẩm
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ProductCreateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _catalogService.CreateProductAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<ProductResponse>.Ok(result, "Tạo sản phẩm thành công."));
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
        {
            var constraint = pgEx.ConstraintName ?? string.Empty;
            if (constraint.Contains("ProductCode", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(ApiResponse<object>.Fail("DUPLICATE_CODE", "Mã sản phẩm đã tồn tại trong hệ thống. Vui lòng nhập mã khác."));
            }
            if (constraint.Contains("Barcode", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(ApiResponse<object>.Fail("DUPLICATE_BARCODE", "Mã vạch (Barcode) đã tồn tại trong hệ thống. Vui lòng kiểm tra lại."));
            }
            return Conflict(ApiResponse<object>.Fail("DUPLICATE_KEY", "Dữ liệu sản phẩm bị trùng lặp trong hệ thống."));
        }
        catch (BusinessException ex)
        {
            var status = ex.ErrorCode.Contains("DUPLICATE") || ex.ErrorCode.Contains("CONFLICT")
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status422UnprocessableEntity;
            return StatusCode(status, ApiResponse<object>.Fail(ex.ErrorCode, ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", ex.Message));
        }
    }

    /// <summary>
    /// Cập nhật thông tin sản phẩm
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ProductUpdateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _catalogService.UpdateProductAsync(id, request, cancellationToken);
            return Ok(ApiResponse<ProductResponse>.Ok(result, "Cập nhật sản phẩm thành công."));
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
        {
            var constraint = pgEx.ConstraintName ?? string.Empty;
            if (constraint.Contains("ProductCode", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(ApiResponse<object>.Fail("DUPLICATE_CODE", "Mã sản phẩm đã tồn tại trong hệ thống. Vui lòng nhập mã khác."));
            }
            if (constraint.Contains("Barcode", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(ApiResponse<object>.Fail("DUPLICATE_BARCODE", "Mã vạch (Barcode) đã tồn tại trong hệ thống. Vui lòng kiểm tra lại."));
            }
            return Conflict(ApiResponse<object>.Fail("DUPLICATE_KEY", "Dữ liệu sản phẩm bị trùng lặp trong hệ thống."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (BusinessException ex)
        {
            var status = ex.ErrorCode.Contains("DUPLICATE") || ex.ErrorCode.Contains("CONFLICT")
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status422UnprocessableEntity;
            return StatusCode(status, ApiResponse<object>.Fail(ex.ErrorCode, ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", ex.Message));
        }
    }

    /// <summary>
    /// Xóa mềm một sản phẩm
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _catalogService.DeleteProductAsync(id, cancellationToken);
            return Ok(ApiResponse<object>.Ok(null!, "Xóa sản phẩm thành công."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message));
        }
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

