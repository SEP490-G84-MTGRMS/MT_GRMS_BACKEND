using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Application.Suppliers.Dtos;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/v1/suppliers")]
public class SuppliersController : BaseApiController
{
    private readonly ISupplierService _supplierService;

    public SuppliersController(ISupplierService supplierService)
    {
        _supplierService = supplierService;
    }

    /// <summary>
    /// Lấy danh sách nhà cung cấp có phân trang và bộ lọc đa điều kiện (REQ-01, REQ-06)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSuppliers([FromQuery] SupplierQueryFilter filter, CancellationToken cancellationToken)
    {
        var result = await _supplierService.GetSuppliersAsync(filter, cancellationToken);
        return Ok(ApiResponse<PagedResult<SupplierSummaryDto>>.Ok(result));
    }

    /// <summary>
    /// Gợi ý tìm kiếm nhanh phân nhóm đa thực thể (NCC, Sản phẩm, Liên hệ, SĐT) (REQ-SEARCH-01)
    /// </summary>
    [HttpGet("search-suggestions")]
    public async Task<IActionResult> GetSearchSuggestions([FromQuery] string query, [FromQuery] int limit = 10, CancellationToken cancellationToken = default)
    {
        var result = await _supplierService.GetSearchSuggestionsAsync(query, limit, cancellationToken);
        return Ok(ApiResponse<SupplierSearchSuggestionsDto>.Ok(result));
    }

    /// <summary>
    /// Lấy thông tin chi tiết nhà cung cấp theo ID (REQ-01, REQ-06)
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _supplierService.GetByIdAsync(id, cancellationToken);
        if (result == null)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", $"Không tìm thấy nhà cung cấp với ID: {id}"));
        }
        return Ok(ApiResponse<SupplierDetailDto>.Ok(result));
    }

    /// <summary>
    /// Tạo mới hồ sơ nhà cung cấp (REQ-01)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSupplierCommand command, CancellationToken cancellationToken)
    {
        var result = await _supplierService.CreateSupplierAsync(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<SupplierDetailDto>.Ok(result, "Tạo nhà cung cấp thành công."));
    }

    /// <summary>
    /// Cập nhật hồ sơ nhà cung cấp (REQ-02, BR-S07)
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSupplierCommand command, CancellationToken cancellationToken)
    {
        var result = await _supplierService.UpdateSupplierAsync(id, command, cancellationToken);
        return Ok(ApiResponse<SupplierDetailDto>.Ok(result, "Cập nhật nhà cung cấp thành công."));
    }

    /// <summary>
    /// Chuyển đổi trạng thái hoạt động của nhà cung cấp (REQ-03, BR-S03)
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateSupplierStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _supplierService.UpdateStatusAsync(id, request, cancellationToken);
        return Ok(ApiResponse<SupplierDetailDto>.Ok(result, "Cập nhật trạng thái nhà cung cấp thành công."));
    }

    /// <summary>
    /// Xóa mềm hồ sơ nhà cung cấp (REQ-03, BR-S03)
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _supplierService.DeleteSupplierAsync(id, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true, "Xóa nhà cung cấp thành công."));
    }

    /// <summary>
    /// Lấy cấu hình lịch giao hàng và ngày nghỉ lễ của nhà cung cấp (REQ-DEL01, REQ-DEL02)
    /// </summary>
    [HttpGet("{id:guid}/delivery-config")]
    public async Task<IActionResult> GetDeliveryConfig(Guid id, CancellationToken cancellationToken)
    {
        var result = await _supplierService.GetDeliveryConfigAsync(id, cancellationToken);
        return Ok(ApiResponse<SupplierDeliveryConfigDto>.Ok(result));
    }

    /// <summary>
    /// Cập nhật cấu hình lịch giao hàng và ngày nghỉ lễ (REQ-DEL01, REQ-DEL02)
    /// </summary>
    [HttpPut("{id:guid}/delivery-config")]
    public async Task<IActionResult> UpdateDeliveryConfig(Guid id, [FromBody] UpdateSupplierDeliveryConfigCommand command, CancellationToken cancellationToken)
    {
        var result = await _supplierService.UpdateDeliveryConfigAsync(id, command, cancellationToken);
        return Ok(ApiResponse<SupplierDeliveryConfigDto>.Ok(result, "Cập nhật cấu hình giao hàng thành công."));
    }

    /// <summary>
    /// Xem sổ cái biến động công nợ AP của nhà cung cấp (REQ-04)
    /// </summary>
    [HttpGet("{id:guid}/ap/summary")]
    public async Task<IActionResult> GetApSummary(Guid id, CancellationToken cancellationToken)
    {
        var result = await _supplierService.GetApSummaryAsync(id, cancellationToken);
        return Ok(ApiResponse<SupplierApSummaryDto>.Ok(result));
    }

    /// <summary>
    /// Lấy lịch sử giao dịch đơn đặt hàng và nhập kho của NCC theo phạm vi chi nhánh (REQ-HIST-01)
    /// </summary>
    [HttpGet("{id:guid}/transactions")]
    public async Task<IActionResult> GetTransactions(
        Guid id,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _supplierService.GetTransactionsAsync(id, pageIndex, pageSize, cancellationToken);
        return Ok(ApiResponse<PagedResult<SupplierTransactionDto>>.Ok(result));
    }

    [HttpGet("{id:guid}/branches")]
    public async Task<IActionResult> GetAssignedBranches(Guid id, CancellationToken cancellationToken)
    {
        var result = await _supplierService.GetAssignedBranchesAsync(id, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<SupplierBranchAssignmentDto>>.Ok(result));
    }

    [HttpPut("{id:guid}/branches")]
    public async Task<IActionResult> ReplaceAssignedBranches(Guid id, [FromBody] ReplaceSupplierBranchesCommand command, CancellationToken cancellationToken)
    {
        var result = await _supplierService.ReplaceAssignedBranchesAsync(id, command, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<SupplierBranchAssignmentDto>>.Ok(result, "Cập nhật chi nhánh gán NCC thành công."));
    }

    /// <summary>
    /// Lấy danh sách liên kết Nhà cung cấp - Chi nhánh / Cửa hàng có phân trang và bộ lọc (REQ-SBA02)
    /// </summary>
    [HttpGet("branch-assignments")]
    public async Task<IActionResult> GetBranchAssignments(
        [FromQuery] SupplierBranchAssignmentQueryFilter filter,
        CancellationToken cancellationToken)
    {
        var result = await _supplierService.GetBranchAssignmentsAsync(filter, cancellationToken);
        return Ok(ApiResponse<PagedResult<SupplierBranchAssignmentDetailDto>>.Ok(result));
    }

    /// <summary>
    /// Gán Nhà cung cấp cho một hoặc nhiều Cửa hàng / Chi nhánh (REQ-SBA01)
    /// </summary>
    [HttpPost("branch-assignments")]
    public async Task<IActionResult> AssignSupplierToBranches(
        [FromBody] AssignSupplierToBranchesCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _supplierService.AssignSupplierToBranchesAsync(command, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<SupplierBranchAssignmentDetailDto>>.Ok(result, "Gán nhà cung cấp cho cửa hàng thành công."));
    }

    /// <summary>
    /// Hủy liên kết Nhà cung cấp khỏi một Cửa hàng / Chi nhánh (REQ-SBA03)
    /// </summary>
    [HttpDelete("{id:guid}/branches/{branchId:guid}")]
    public async Task<IActionResult> UnassignSupplierBranch(
        Guid id,
        Guid branchId,
        CancellationToken cancellationToken)
    {
        await _supplierService.UnassignSupplierBranchAsync(id, branchId, cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { }, "Hủy liên kết cửa hàng khỏi nhà cung cấp thành công."));
    }

    /// <summary>
    /// Chuyển trạng thái Active / Inactive của phân bổ NCC với cửa hàng. Inactive bắt buộc có lý do.
    /// </summary>
    [HttpPatch("{id:guid}/branches/{branchId:guid}/status")]
    public async Task<IActionResult> UpdateBranchAssignmentStatus(
        Guid id,
        Guid branchId,
        [FromBody] UpdateSupplierBranchAssignmentStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _supplierService.UpdateBranchAssignmentStatusAsync(id, branchId, request, cancellationToken);
        var message = result.Status == "Inactive"
            ? "Đã tạm ngưng phân bổ nhà cung cấp với cửa hàng."
            : "Đã kích hoạt phân bổ nhà cung cấp với cửa hàng.";
        return Ok(ApiResponse<SupplierBranchAssignmentDetailDto>.Ok(result, message));
    }

    [HttpGet("{id:guid}/ap/ledger")]
    public async Task<IActionResult> GetAPLedger(
        Guid id,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _supplierService.GetAPLedgerAsync(id, fromDate, toDate, pageIndex, pageSize, cancellationToken);
        return Ok(ApiResponse<PagedResult<SupplierAPLedgerDto>>.Ok(result));
    }

    /// <summary>
    /// Ghi nhận giao dịch tăng/giảm công nợ AP với IdempotencyKey (REQ-04, BR-S08)
    /// </summary>
    [HttpPost("{id:guid}/ap/transactions")]
    public async Task<IActionResult> RecordAPTransaction(Guid id, [FromBody] RecordAPTransactionCommand command, CancellationToken cancellationToken)
    {
        command.SupplierId = id;
        var result = await _supplierService.RecordAPTransactionAsync(command, cancellationToken);
        return CreatedAtAction(nameof(GetAPLedger), new { id = result.SupplierId }, ApiResponse<SupplierAPLedgerDto>.Ok(result, "Ghi nhận biến động công nợ thành công."));
    }

    /// <summary>
    /// Liên kết hồ sơ khách hàng cho nhà cung cấp song hành (REQ-05, BR-S11)
    /// </summary>
    [HttpPost("{id:guid}/link-customer")]
    public async Task<IActionResult> LinkCustomer(Guid id, [FromBody] LinkCustomerRequest request, CancellationToken cancellationToken)
    {
        var result = await _supplierService.LinkCustomerAsync(id, request, cancellationToken);
        return Ok(ApiResponse<SupplierDetailDto>.Ok(result, "Liên kết hồ sơ khách hàng thành công."));
    }

    /// <summary>
    /// Lấy danh mục sản phẩm được gán cho Nhà cung cấp có phân trang và bộ lọc (REQ-SP05, REQ-SP06)
    /// </summary>
    [HttpGet("{id:guid}/products")]
    public async Task<IActionResult> GetSupplierProducts(
        Guid id,
        [FromQuery] SupplierProductQueryFilter filter,
        CancellationToken cancellationToken)
    {
        var result = await _supplierService.GetSupplierProductsAsync(id, filter, cancellationToken);
        return Ok(ApiResponse<PagedResult<SupplierProductDto>>.Ok(result));
    }

    /// <summary>
    /// Gán sản phẩm mới cho Nhà cung cấp kèm đơn vị giao hàng, giá sỉ và MOQ (REQ-SP01, BR-SP01, BR-SP02)
    /// </summary>
    [HttpPost("{id:guid}/products")]
    public async Task<IActionResult> AssignProduct(
        Guid id,
        [FromBody] AssignSupplierProductCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _supplierService.AssignSupplierProductAsync(id, command, cancellationToken);
        return CreatedAtAction(nameof(GetSupplierProducts), new { id = result.SupplierId }, ApiResponse<SupplierProductDto>.Ok(result, "Gán sản phẩm cho nhà cung cấp thành công."));
    }

    /// <summary>
    /// Cập nhật giá sỉ thỏa thuận, đơn vị giao hàng và MOQ của sản phẩm (REQ-SP02)
    /// </summary>
    [HttpPut("{id:guid}/products/{productId:guid}")]
    public async Task<IActionResult> UpdateSupplierProduct(
        Guid id,
        Guid productId,
        [FromBody] UpdateSupplierProductCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _supplierService.UpdateSupplierProductAsync(id, productId, command, cancellationToken);
        return Ok(ApiResponse<SupplierProductDto>.Ok(result, "Cập nhật sản phẩm nhà cung cấp thành công."));
    }

    /// <summary>
    /// Chuyển đổi trạng thái hoạt động / tạm ngưng của sản phẩm theo NCC (REQ-SP03)
    /// </summary>
    [HttpPatch("{id:guid}/products/{productId:guid}/status")]
    public async Task<IActionResult> ToggleProductStatus(
        Guid id,
        Guid productId,
        [FromBody] ToggleSupplierProductStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _supplierService.ToggleSupplierProductStatusAsync(id, productId, request, cancellationToken);
        return Ok(ApiResponse<SupplierProductDto>.Ok(result, "Cập nhật trạng thái sản phẩm thành công."));
    }

    /// <summary>
    /// Đặt hoặc hủy quyền Nhà cung cấp chính (Primary Supplier) cho sản phẩm (REQ-SP04, BR-SP02)
    /// </summary>
    [HttpPatch("{id:guid}/products/{productId:guid}/primary")]
    public async Task<IActionResult> SetPrimarySupplier(
        Guid id,
        Guid productId,
        [FromBody] SetPrimarySupplierRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _supplierService.SetPrimarySupplierAsync(id, productId, request, cancellationToken);
        return Ok(ApiResponse<SupplierProductDto>.Ok(result, "Cập nhật nhà cung cấp chính thành công."));
    }
}

