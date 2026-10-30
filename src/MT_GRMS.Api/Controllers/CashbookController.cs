using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Api.Filters;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Cashbook.Dtos;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Api.Controllers;

public class CashbookController : BaseApiController
{
    private readonly ICashbookService _cashbookService;

    public CashbookController(ICashbookService cashbookService)
    {
        _cashbookService = cashbookService;
    }

    [HttpPost("shifts/open")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<ShiftResponse>>> OpenShift([FromBody] OpenShiftRequest request, CancellationToken ct)
    {
        var result = await _cashbookService.OpenShiftAsync(request, ct);
        return Ok(ApiResponse<ShiftResponse>.Ok(result, "Mở ca thu ngân thành công."));
    }

    [HttpPost("shifts/close")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<ShiftResponse>>> CloseShift([FromBody] CloseShiftRequest request, CancellationToken ct)
    {
        var result = await _cashbookService.CloseShiftZReportAsync(request, ct);
        return Ok(ApiResponse<ShiftResponse>.Ok(result, "Chốt ca Z-Report thành công."));
    }

    [HttpPost("cash-drop")]
    public async Task<ActionResult<ApiResponse<bool>>> CashDrop([FromBody] CashDropRequest request, CancellationToken ct)
    {
        var result = await _cashbookService.ProcessCashDropAsync(request, ct);
        return Ok(ApiResponse<bool>.Ok(result, "Thực hiện cash drop bàn giao tiền sang két sắt thành công."));
    }

    [HttpPost("deposits/bank")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<CashInTransitResponse>>> CreateDepositToBank([FromBody] DepositToBankRequest request, CancellationToken ct)
    {
        var result = await _cashbookService.CreateDepositToBankAsync(request, ct);
        return Ok(ApiResponse<CashInTransitResponse>.Ok(result, "Tạo lệnh nộp tiền vào ngân hàng thành công."));
    }

    [HttpPost("deposits/confirm")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<CashInTransitResponse>>> ConfirmBankDeposit([FromBody] ConfirmBankDepositRequest request, CancellationToken ct)
    {
        var result = await _cashbookService.ConfirmBankDepositAsync(request, ct);
        return Ok(ApiResponse<CashInTransitResponse>.Ok(result, "Xác nhận tiền nộp đã vào tài khoản ngân hàng thành công."));
    }

    [HttpGet("entries")]
    public async Task<ActionResult<ApiResponse<List<CashbookEntryResponse>>>> GetEntries(
        [FromQuery] Guid? accountId,
        [FromQuery] Guid? branchId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await _cashbookService.GetEntriesAsync(accountId, branchId, fromDate, toDate, page, pageSize, ct);
        return Ok(ApiResponse<List<CashbookEntryResponse>>.Ok(result));
    }

    [HttpPost("tills/init")]
    public async Task<ActionResult<ApiResponse<Guid>>> GetOrCreateTill([FromQuery] Guid branchId, [FromQuery] string tillName = "Két Thu Ngân #1", CancellationToken ct = default)
    {
        var till = await _cashbookService.GetOrCreateTillAsync(branchId, tillName, ct);
        return Ok(ApiResponse<Guid>.Ok(till.Id, "Khởi tạo/lấy két quầy thành công."));
    }
}