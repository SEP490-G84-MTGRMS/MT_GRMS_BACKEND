using MT_GRMS.Application.HR.Payslips.Dtos;
using MT_GRMS.Domain.Entities.HR;

namespace MT_GRMS.Application.HR.Payslips;

public interface IPayslipRepository
{
    Task<Payslip?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Payslip?> GetByEmployeeAndPeriodAsync(Guid employeeId, string periodMonth, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Payslip> Items, int TotalCount)> GetPagedListAsync(PayslipQueryFilter filter, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Payslip>> GetByPeriodAndBranchAsync(string periodMonth, Guid? branchId, CancellationToken cancellationToken = default);
    Task AddAsync(Payslip payslip, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<Payslip> payslips, CancellationToken cancellationToken = default);
    void Update(Payslip payslip);
    void Delete(Payslip payslip);
    Task<int> GetCountByPeriodMonthAsync(string periodMonth, CancellationToken cancellationToken = default);
    Task AddCashbookTransactionAsync(Domain.Entities.Finance.CashbookTransaction transaction, CancellationToken cancellationToken = default);
}
