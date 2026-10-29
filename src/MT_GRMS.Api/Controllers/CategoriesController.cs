using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/v1/categories")]
public class CategoriesController : BaseApiController
{
    private readonly ICategoryService _categoryService;

    public CategoriesController(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    /// <summary>
    /// Lấy danh sách toàn bộ nhóm hàng kèm số lượng sản phẩm (productCount)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _categoryService.GetAllAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<CategoryResponse>>.Ok(result));
    }

    /// <summary>
    /// Lấy chi tiết thông tin một nhóm hàng theo ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _categoryService.GetByIdAsync(id, cancellationToken);
        if (result == null)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Không tìm thấy nhóm hàng."));
        }
        return Ok(ApiResponse<CategoryResponse>.Ok(result));
    }

    /// <summary>
    /// Tạo mới một nhóm hàng
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CategoryCreateRequest request, CancellationToken cancellationToken)
    {
        var result = await _categoryService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<CategoryResponse>.Ok(result, "Tạo nhóm hàng thành công."));
    }

    /// <summary>
    /// Cập nhật thông tin nhóm hàng
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CategoryUpdateRequest request, CancellationToken cancellationToken)
    {
        var result = await _categoryService.UpdateAsync(id, request, cancellationToken);
        return Ok(ApiResponse<CategoryResponse>.Ok(result, "Cập nhật nhóm hàng thành công."));
    }

    /// <summary>
    /// Xóa mềm nhóm hàng (chặn nếu còn sản phẩm)
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _categoryService.DeleteAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null!, "Xóa nhóm hàng thành công."));
    }
}

