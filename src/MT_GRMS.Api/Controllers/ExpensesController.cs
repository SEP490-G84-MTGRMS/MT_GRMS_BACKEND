using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Api.Filters;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Expenses;
using MT_GRMS.Application.Expenses.Dtos;
using MT_GRMS.Domain.Constants;

namespace MT_GRMS.Api.Controllers;

[Route("api/[controller]")]
[Authorize(Roles = SystemRoles.StoreOwner)]
public class ExpensesController : BaseApiController
{
    private readonly IExpenseService _expenseService;

    public ExpensesController(IExpenseService expenseService)
    {
        _expenseService = expenseService;
    }

    [HttpPost]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<ExpenseVoucherResponse>>> CreateExpense([FromBody] CreateExpenseRequest request, CancellationToken ct)
    {
        var result = await _expenseService.CreateExpenseAsync(request, ct);
        return Ok(ApiResponse<ExpenseVoucherResponse>.Ok(result, "Tạo phiếu chi thành công."));
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = SystemRoles.StoreOwner)]
    public async Task<ActionResult<ApiResponse<ExpenseVoucherResponse>>> ApproveExpense([FromRoute] Guid id, [FromBody] ApproveExpenseRequest request, CancellationToken ct)
    {
        var result = await _expenseService.ApproveExpenseAsync(request with { ExpenseId = id }, ct);
        return Ok(ApiResponse<ExpenseVoucherResponse>.Ok(result, "Phê duyệt phiếu chi thành công."));
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = SystemRoles.StoreOwner)]
    public async Task<ActionResult<ApiResponse<ExpenseVoucherResponse>>> RejectExpense([FromRoute] Guid id, [FromBody] RejectExpenseRequest request, CancellationToken ct)
    {
        var result = await _expenseService.RejectExpenseAsync(request with { ExpenseId = id }, ct);
        return Ok(ApiResponse<ExpenseVoucherResponse>.Ok(result, "Từ chối phê duyệt phiếu chi thành công."));
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = SystemRoles.StoreOwner)]
    public async Task<ActionResult<ApiResponse<ExpenseVoucherResponse>>> CancelExpense([FromRoute] Guid id, [FromBody] CancelExpenseRequest request, CancellationToken ct)
    {
        var result = await _expenseService.CancelExpenseAsync(request with { ExpenseId = id }, ct);
        return Ok(ApiResponse<ExpenseVoucherResponse>.Ok(result, "Hủy phiếu chi thành công."));
    }

    [HttpPost("{id:guid}/disburse")]
    [RequireIdempotency]
    [Authorize(Roles = SystemRoles.StoreOwner)]
    public async Task<ActionResult<ApiResponse<ExpenseVoucherResponse>>> DisburseExpense([FromRoute] Guid id, [FromBody] DisburseExpenseRequest request, CancellationToken ct)
    {
        var result = await _expenseService.DisburseExpenseAsync(request with { ExpenseId = id }, ct);
        return Ok(ApiResponse<ExpenseVoucherResponse>.Ok(result, "Xuất quỹ chi tiền thành công."));
    }

    [HttpGet("summary")]
    [Authorize(Roles = SystemRoles.StoreOwner)]
    public async Task<ActionResult<ApiResponse<ExpenseSummaryResponse>>> GetExpenseSummary(
        [FromQuery] Guid? branchId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken ct = default)
    {
        var result = await _expenseService.GetExpenseSummaryAsync(branchId, fromDate, toDate, ct);
        return Ok(ApiResponse<ExpenseSummaryResponse>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ExpenseVoucherResponse>>> GetExpenseById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _expenseService.GetExpenseByIdAsync(id, ct);
        return Ok(ApiResponse<ExpenseVoucherResponse>.Ok(result));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ExpenseVoucherResponse>>>> GetExpenses(
        [FromQuery] Guid? branchId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? keyword = null,
        [FromQuery] string? category = null,
        [FromQuery] string? status = null,
        [FromQuery] string? quickStatus = null,
        CancellationToken ct = default)
    {
        var paged = await _expenseService.GetExpensesPageAsync(
            branchId, fromDate, toDate, page, pageSize, keyword, category, status, quickStatus, ct);
        return Ok(new ExpensesPagedResponse
        {
            Success = true,
            Data = paged.Items.ToList(),
            TotalItems = paged.TotalItems,
        });
    }
}

public class ExpensesPagedResponse : ApiResponse<List<ExpenseVoucherResponse>>
{
    public int TotalItems { get; set; }
}
