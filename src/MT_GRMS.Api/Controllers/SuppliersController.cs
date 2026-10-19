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
}
