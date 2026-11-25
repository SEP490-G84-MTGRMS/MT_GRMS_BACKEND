using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Api.Filters;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Debt;
using MT_GRMS.Application.Debt.Dtos;
using MT_GRMS.Domain.Constants;

namespace MT_GRMS.Api.Controllers;

[Route("api/customers/debt")]
[Authorize(Roles = SystemRoles.StoreOwner)]
public class CustomerDebtController : BaseApiController
{
    private readonly ICustomerDebtService _debtService;

    public CustomerDebtController(ICustomerDebtService debtService)
    {
        _debtService = debtService;
    }

    [HttpPost("repay")]
    [RequireIdempotency(transactional: true)]
    public async Task<ActionResult<ApiResponse<RepaymentResultResponse>>> RepayDebt([FromBody] RepayDebtRequest request, CancellationToken ct)
    {
        var result = await _debtService.RepayDebtAsync(request, ct);
        return Ok(ApiResponse<RepaymentResultResponse>.Ok(result, "Thu nợ khách hàng thành công."));
    }

    [HttpGet("profile/{customerId:guid}")]
    public async Task<ActionResult<ApiResponse<CustomerDebtProfileResponse>>> GetProfile(Guid customerId, CancellationToken ct)
    {
        var result = await _debtService.GetProfileAsync(customerId, ct);
        return Ok(ApiResponse<CustomerDebtProfileResponse>.Ok(result));
    }

    [HttpGet("ledger/{customerId:guid}")]
    public async Task<ActionResult<ApiResponse<List<CustomerDebtLedgerItemResponse>>>> GetLedger(Guid customerId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var result = await _debtService.GetLedgerAsync(customerId, page, pageSize, ct);
        return Ok(ApiResponse<List<CustomerDebtLedgerItemResponse>>.Ok(result));
    }
}
