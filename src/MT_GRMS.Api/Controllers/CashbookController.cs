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

    [HttpGet("accounts")]
    public async Task<ActionResult<ApiResponse<List<CashAccountResponse>>>> GetAccounts(
        [FromQuery] Guid? branchId,
        CancellationToken ct = default)
    {
        var result = await _cashbookService.GetAccountsAsync(branchId, ct);
        return Ok(ApiResponse<List<CashAccountResponse>>.Ok(result));
    }

    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<CashbookSummaryResponse>>> GetSummary(
        [FromQuery] Guid? branchId,
        CancellationToken ct = default)
    {
        var result = await _cashbookService.GetSummaryAsync(branchId, ct);
        return Ok(ApiResponse<CashbookSummaryResponse>.Ok(result));
    }

    [HttpPost("receipts")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<CashbookEntryResponse>>> CreateReceipt(
        [FromBody] CreateReceiptRequest request,
        CancellationToken ct)
    {
        var result = await _cashbookService.CreateReceiptAsync(request, ct);
        return Ok(ApiResponse<CashbookEntryResponse>.Ok(result, "Ghi nhận phiếu thu thành công."));
    }

    [HttpPost("payments")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<CashbookEntryResponse>>> CreatePayment(
        [FromBody] CreatePaymentRequest request,
        CancellationToken ct)
    {
        var result = await _cashbookService.CreatePaymentAsync(request, ct);
        return Ok(ApiResponse<CashbookEntryResponse>.Ok(result, "Ghi nhận phiếu chi thành công."));
    }

    [HttpPost("transfers")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<TransferFundResponse>>> TransferFunds(
        [FromBody] TransferFundRequest request,
        CancellationToken ct)
    {
        var result = await _cashbookService.TransferFundsAsync(request, ct);
        return Ok(ApiResponse<TransferFundResponse>.Ok(result, "Điều chuyển tiền nội bộ thành công."));
    }

    [HttpPost("reconcile")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<CashbookEntryResponse?>>> ReconcileCashAccount(
        [FromBody] ReconcileAccountRequest request,
        CancellationToken ct)
    {
        var result = await _cashbookService.ReconcileCashAccountAsync(request, ct);
        return Ok(ApiResponse<CashbookEntryResponse?>.Ok(result, "Hoàn tất kiểm kê và đối soát két quỹ."));
    }

    [HttpPost("entries/{id:guid}/void")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<CashbookEntryResponse>>> VoidTransaction(
        [FromRoute] Guid id,
        [FromBody] VoidEntryRequest request,
        CancellationToken ct)
    {
        var result = await _cashbookService.VoidTransactionAsync(id, request, ct);
        return Ok(ApiResponse<CashbookEntryResponse>.Ok(result, "Đã tạo bút toán bù trừ gạch bỏ phiếu thành công."));
    }
}
