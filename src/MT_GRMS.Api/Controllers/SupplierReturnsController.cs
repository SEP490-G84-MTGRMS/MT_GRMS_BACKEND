using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.SupplierReturns;
using MT_GRMS.Application.SupplierReturns.Dtos;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/v1/supplier-returns")]
public class SupplierReturnsController : BaseApiController
{
    private readonly ISupplierReturnService _service;

    public SupplierReturnsController(ISupplierReturnService service)
    {
        _service = service;
    }

    /// <summary>
    /// Lấy danh sách phiếu trả hàng nhà cung cấp có phân trang & bộ lọc (REQ-01)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] SupplierReturnFilterQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetPagedAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<SupplierReturnListItemDto>>.Ok(result));
    }

    /// <summary>
    /// Lấy chi tiết một phiếu trả hàng theo ID (REQ-03)
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.GetByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<SupplierReturnDetailDto>.Ok(result));
    }

    /// <summary>
    /// Lấy danh sách các phiếu nhập kho đã hoàn tất (COMPLETED) đủ điều kiện tạo phiếu trả (REQ-02)
    /// </summary>
    [HttpGet("eligible-grns")]
    public async Task<IActionResult> GetEligibleGrns(
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? supplierId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetEligibleGrnsAsync(branchId, supplierId, cancellationToken);
        return Ok(ApiResponse<System.Collections.Generic.List<EligibleGrnLookupDto>>.Ok(result));
    }

    /// <summary>
    /// Lấy danh sách mặt hàng và số lượng còn có thể trả từ phiếu nhập gốc (REQ-02)
    /// </summary>
    [HttpGet("grn-lines/{grnId:guid}")]
    public async Task<IActionResult> GetGrnLines(Guid grnId, CancellationToken cancellationToken)
    {
        var result = await _service.GetGrnReturnableLinesAsync(grnId, cancellationToken);
        return Ok(ApiResponse<System.Collections.Generic.List<GrnReturnableLineDto>>.Ok(result));
    }

    /// <summary>
    /// Tạo mới một phiếu yêu cầu trả hàng nhà cung cấp (REQ-02)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateSupplierReturnCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<SupplierReturnDetailDto>.Ok(result, "Tạo phiếu yêu cầu trả hàng thành công!"));
    }

    /// <summary>
    /// Gửi yêu cầu trả hàng sang nhà cung cấp (chuyển hàng sang PENDING_RETURN) (REQ-02)
    /// </summary>
    [HttpPost("{id:guid}/submit")]
    public async Task<IActionResult> Submit(
        Guid id,
        [FromQuery] string? notes,
        CancellationToken cancellationToken)
    {
        var result = await _service.SubmitAsync(id, notes, cancellationToken);
        return Ok(ApiResponse<SupplierReturnDetailDto>.Ok(result, "Đã gửi yêu cầu trả hàng sang nhà cung cấp."));
    }

    /// <summary>
    /// Ghi nhận kết quả phản hồi từ nhà cung cấp (APPROVED hoặc REJECTED) (REQ-03)
    /// </summary>
    [HttpPost("{id:guid}/supplier-response")]
    public async Task<IActionResult> RecordSupplierResponse(
        Guid id,
        [FromBody] RecordSupplierResponseCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _service.RecordSupplierResponseAsync(id, command, cancellationToken);
        return Ok(ApiResponse<SupplierReturnDetailDto>.Ok(result, "Đã ghi nhận kết quả phản hồi của nhà cung cấp."));
    }

    /// <summary>
    /// Hoàn tất bàn giao hàng thực tế cho nhà cung cấp (Trừ tồn kho chính thức) (REQ-03, REQ-04)
    /// </summary>
    [HttpPost("{id:guid}/complete-handover")]
    public async Task<IActionResult> CompleteHandover(
        Guid id,
        [FromBody] CompleteHandoverCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _service.CompleteHandoverAsync(id, command, cancellationToken);
        return Ok(ApiResponse<SupplierReturnDetailDto>.Ok(result, "Đã hoàn tất bàn giao hàng và giảm tồn kho chi nhánh thành công."));
    }

    /// <summary>
    /// Hủy phiếu trả hàng (giải phóng số lượng chờ trả) (REQ-03)
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(
        Guid id,
        [FromBody] CancelSupplierReturnCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _service.CancelAsync(id, command, cancellationToken);
        return Ok(ApiResponse<SupplierReturnDetailDto>.Ok(result, "Đã hủy phiếu trả hàng thành công."));
    }
}
