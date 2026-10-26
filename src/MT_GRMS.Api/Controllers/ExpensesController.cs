using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Api.Filters;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Expenses;
using MT_GRMS.Application.Expenses.Dtos;

namespace MT_GRMS.Api.Controllers;

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
    public async Task<ActionResult<ApiResponse<ExpenseVoucherResponse>>> ApproveExpense([FromRoute] Guid id, [FromBody] ApproveExpenseRequest request, CancellationToken ct)
    {
        var result = await _expenseService.ApproveExpenseAsync(request with { ExpenseId = id }, ct);
        return Ok(ApiResponse<ExpenseVoucherResponse>.Ok(result, "Phê duyệt phiếu chi thành công."));
    }

    [HttpPost("{id:guid}/disburse")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<ExpenseVoucherResponse>>> DisburseExpense([FromRoute] Guid id, [FromBody] DisburseExpenseRequest request, CancellationToken ct)
    {
        var result = await _expenseService.DisburseExpenseAsync(request with { ExpenseId = id }, ct);
        return Ok(ApiResponse<ExpenseVoucherResponse>.Ok(result, "Xuất quỹ chi tiền thành công."));
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
        CancellationToken ct = default)
    {
        var result = await _expenseService.GetExpensesAsync(branchId, fromDate, toDate, page, pageSize, ct);
        return Ok(ApiResponse<List<ExpenseVoucherResponse>>.Ok(result));
    }
}