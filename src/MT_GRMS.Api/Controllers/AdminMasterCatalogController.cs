using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Constants;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/admin/master-catalog")]
[Authorize(Roles = RoleConstants.AdminOnly)]
public class AdminMasterCatalogController : BaseApiController
{
    private readonly IMasterCatalogService _masterCatalogService;
    private readonly IMasterCatalogExcelService _excelService;

    public AdminMasterCatalogController(
        IMasterCatalogService masterCatalogService,
        IMasterCatalogExcelService excelService)
    {
        _masterCatalogService = masterCatalogService;
        _excelService = excelService;
    }

    /// <summary>
    /// Lấy danh sách sản phẩm mẫu toàn cầu có phân trang và lọc
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] MasterProductFilterRequest filter,
        CancellationToken cancellationToken)
    {
        var result = await _masterCatalogService.GetPagedListAsync(filter, cancellationToken);
        return Ok(ApiResponse<PagedResult<MasterProductDto>>.Ok(result));
    }

    /// <summary>
    /// Lấy 4 chỉ số KPI tổng quan của Master Catalog
    /// </summary>
    [HttpGet("kpis")]
    public async Task<IActionResult> GetKpis(CancellationToken cancellationToken)
    {
        var result = await _masterCatalogService.GetKpisAsync(cancellationToken);
        return Ok(ApiResponse<MasterCatalogKpiDto>.Ok(result));
    }

    /// <summary>
    /// Lấy danh sách thương hiệu / hãng sản xuất hiện có trong Master Catalog
    /// </summary>
    [HttpGet("brands")]
    public async Task<IActionResult> GetBrands(CancellationToken cancellationToken)
    {
        var result = await _masterCatalogService.GetBrandsAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<string>>.Ok(result));
    }

    /// <summary>
    /// Lấy danh sách ngành hàng hiện có trong Master Catalog
    /// </summary>
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken)
    {
        var result = await _masterCatalogService.GetCategoriesAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<string>>.Ok(result));
    }

    /// <summary>
    /// Xem chi tiết sản phẩm mẫu kèm quy cách đóng gói đa đơn vị
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var result = await _masterCatalogService.GetByIdAsync(id, cancellationToken);
        if (result == null)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", $"Không tìm thấy sản phẩm mẫu với ID: {id}"));
        }
        return Ok(ApiResponse<MasterProductDetailDto>.Ok(result));
    }

    /// <summary>
    /// Thêm mới 1 sản phẩm mẫu vào Thư viện toàn cầu
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateMasterProductRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _masterCatalogService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id },
            ApiResponse<MasterProductDetailDto>.Ok(result, "Tạo sản phẩm mẫu thành công."));
    }

    /// <summary>
    /// Cập nhật thông tin và quy cách sản phẩm mẫu
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateMasterProductRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _masterCatalogService.UpdateAsync(id, request, cancellationToken);
        return Ok(ApiResponse<MasterProductDetailDto>.Ok(result, "Cập nhật sản phẩm mẫu thành công."));
    }

    /// <summary>
    /// Xóa mềm một sản phẩm mẫu khỏi Thư viện toàn cầu
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var success = await _masterCatalogService.DeleteAsync(id, cancellationToken);
        if (!success)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", $"Không tìm thấy sản phẩm mẫu với ID: {id}"));
        }
        return Ok(ApiResponse<bool>.Ok(true, "Xóa sản phẩm mẫu thành công."));
    }

    /// <summary>
    /// Tải file Excel mẫu chuẩn hóa (.xlsx) để chuẩn bị dữ liệu import
    /// </summary>
    [HttpGet("export-template")]
    public async Task<IActionResult> ExportTemplate(CancellationToken cancellationToken)
    {
        var bytes = await _excelService.GenerateTemplateAsync(cancellationToken);
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Master_Catalog_Template.xlsx");
    }

    /// <summary>
    /// Import danh mục hàng mẫu từ file Excel (.xlsx) với tùy chọn ghi đè
    /// </summary>
    [HttpPost("import-excel")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportExcel(
        IFormFile? file,
        [FromQuery] bool upsert = true,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(ApiResponse<object>.Fail(
                "FILE_EMPTY", "Vui lòng chọn tệp Excel (.xlsx) hợp lệ và không rỗng."));
        }

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(ApiResponse<object>.Fail(
                "INVALID_FILE_TYPE", "Hệ thống chỉ chấp nhận định dạng bảng tính Excel (.xlsx)."));
        }

        using var stream = file.OpenReadStream();
        var result = await _excelService.ImportExcelAsync(stream, upsert, cancellationToken);

        return Ok(ApiResponse<MasterProductExcelImportResult>.Ok(
            result, $"Xử lý import hoàn tất: Thành công {result.SuccessCount}/{result.TotalRows} dòng."));
    }
}
