using MT_GRMS.Application.Expenses.Dtos;

namespace MT_GRMS.Application.Expenses;

public interface IExpenseService
{
    Task<ExpenseVoucherResponse> CreateExpenseAsync(CreateExpenseRequest request, CancellationToken ct = default);
    Task<ExpenseVoucherResponse> ApproveExpenseAsync(ApproveExpenseRequest request, CancellationToken ct = default);
    Task<ExpenseVoucherResponse> DisburseExpenseAsync(DisburseExpenseRequest request, CancellationToken ct = default);
    Task<ExpenseVoucherResponse> GetExpenseByIdAsync(Guid expenseId, CancellationToken ct = default);
    Task<List<ExpenseVoucherResponse>> GetExpensesAsync(Guid? branchId, DateTime? fromDate, DateTime? toDate, int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task<ExpenseSummaryResponse> GetExpenseSummaryAsync(Guid? branchId, DateTime? fromDate, DateTime? toDate, CancellationToken ct = default);
}