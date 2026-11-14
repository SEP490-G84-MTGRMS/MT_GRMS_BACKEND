using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Transfers;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Api.Controllers;

[Route("api/transfers")]
[Authorize]
public sealed class TransfersController(ITransferService service) : BaseApiController
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<TransferDto>>>> Get([FromQuery] string? search, [FromQuery] Guid? fromBranchId, [FromQuery] Guid? toBranchId, [FromQuery] TransferStatus? status, [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] List<TransferStatus>? statuses = null, [FromQuery] string? dateField = null, [FromQuery] string? receiveStatus = null, CancellationToken ct = default)
        => Ok(ApiResponse<PagedResult<TransferDto>>.Ok(await service.GetTransfersAsync(new TransferFilterParams(search, fromBranchId, toBranchId, status, fromDate, toDate, page, pageSize, statuses, dateField, receiveStatus), ct)));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<TransferDto>>> GetById(Guid id, CancellationToken ct)
        => Ok(ApiResponse<TransferDto>.Ok(await service.GetTransferAsync(id, ct)));

    [HttpGet("stock-batches")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TransferStockBatchDto>>>> GetStockBatches([FromQuery] Guid branchId, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<TransferStockBatchDto>>.Ok(await service.GetStockBatchesAsync(branchId, ct)));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<TransferDto>>> Create([FromBody] CreateTransferRequest request, CancellationToken ct)
        => Ok(ApiResponse<TransferDto>.Ok(await service.CreateTransferAsync(request, ct), "Tạo phiếu điều chuyển thành công."));

    [HttpPut("{id:guid}/receive")]
    public async Task<ActionResult<ApiResponse<TransferDto>>> Receive(Guid id, [FromBody] ReceiveTransferRequest request, CancellationToken ct)
        => Ok(ApiResponse<TransferDto>.Ok(await service.ReceiveTransferAsync(id, request, ct), "Xác nhận nhận hàng thành công."));

    [HttpPost("{id:guid}/dispatch")]
    public async Task<ActionResult<ApiResponse<TransferDto>>> Dispatch(Guid id, CancellationToken ct)
        => Ok(ApiResponse<TransferDto>.Ok(await service.DispatchTransferAsync(id, ct), "Xuất chuyển hàng thành công."));

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<ApiResponse<TransferDto>>> Cancel(Guid id, [FromBody] CancelTransferRequest request, CancellationToken ct)
        => Ok(ApiResponse<TransferDto>.Ok(await service.CancelTransferAsync(id, request, ct), "Hủy phiếu điều chuyển thành công."));
}