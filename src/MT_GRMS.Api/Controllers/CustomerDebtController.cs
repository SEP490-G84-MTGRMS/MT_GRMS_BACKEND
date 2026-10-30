using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Api.Filters;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Debt;
using MT_GRMS.Application.Debt.Dtos;

namespace MT_GRMS.Api.Controllers;

[Route("api/customers/debt")]
public class CustomerDebtController : BaseApiController
{
    private readonly ICustomerDebtService _debtService;

    public CustomerDebtController(ICustomerDebtService debtService)
    {
        _debtService = debtService;
    }

    [HttpPost("record-sale-debt")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<CustomerDebtInvoiceResponse>>> RecordSaleDebt([FromBody] RecordSaleDebtRequest request, CancellationToken ct)
    {
        var result = await _debtService.RecordSaleDebtAsync(request, ct);
        return Ok(ApiResponse<CustomerDebtInvoiceResponse>.Ok(result, "Ghi nợ đơn hàng thành công."));
    }

    [HttpPost("repay")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<RepaymentResultResponse>>> RepayDebt([FromBody] RepayDebtRequest request, CancellationToken ct)
    {
        var result = await _debtService.RepayDebtAsync(request, ct);
        return Ok(ApiResponse<RepaymentResultResponse>.Ok(result, "Thu nợ khách hàng thành công."));
    }

    [HttpPost("goods-return-offset")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<CustomerDebtEntryResponse>>> OffsetGoodsReturnDebt([FromBody] OffsetGoodsReturnDebtRequest request, CancellationToken ct)
    {
        var result = await _debtService.OffsetGoodsReturnDebtAsync(request, ct);
        return Ok(ApiResponse<CustomerDebtEntryResponse>.Ok(result, "Cấn trừ nợ trả hàng thành công."));
    }

    [HttpPost("adjust")]
    public async Task<ActionResult<ApiResponse<CustomerDebtEntryResponse>>> AdjustDebt([FromBody] AdjustDebtRequest request, CancellationToken ct)
    {
        var result = await _debtService.AdjustDebtAsync(request, ct);
        return Ok(ApiResponse<CustomerDebtEntryResponse>.Ok(result, "Điều chỉnh nợ thành công."));
    }

    [HttpPost("write-off")]
    public async Task<ActionResult<ApiResponse<CustomerDebtEntryResponse>>> WriteOffBadDebt([FromBody] WriteOffBadDebtRequest request, CancellationToken ct)
    {
        var result = await _debtService.WriteOffBadDebtAsync(request, ct);
        return Ok(ApiResponse<CustomerDebtEntryResponse>.Ok(result, "Xóa nợ xấu thành công."));
    }

    [HttpGet("profile/{customerId:guid}")]
    public async Task<ActionResult<ApiResponse<CustomerDebtProfileResponse>>> GetProfile(Guid customerId, CancellationToken ct)
    {
        var result = await _debtService.GetProfileAsync(customerId, ct);
        return Ok(ApiResponse<CustomerDebtProfileResponse>.Ok(result));
    }

    [HttpGet("ledger/{customerId:guid}")]
    public async Task<ActionResult<ApiResponse<List<CustomerDebtEntryResponse>>>> GetLedger(Guid customerId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var result = await _debtService.GetLedgerAsync(customerId, page, pageSize, ct);
        return Ok(ApiResponse<List<CustomerDebtEntryResponse>>.Ok(result));
    }
}
