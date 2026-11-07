using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Payslips;
using MT_GRMS.Application.HR.Payslips.Dtos;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class PayslipRepository : IPayslipRepository
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;

    public PayslipRepository(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<Payslip?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Payslips
            .Include(p => p.Employee)
            .Include(p => p.Branch)
            .Include(p => p.CashbookTransaction)
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenantContext.CurrentTenantId, cancellationToken);
    }

    public async Task<Payslip?> GetByEmployeeAndPeriodAsync(Guid employeeId, string periodMonth, CancellationToken cancellationToken = default)
    {
        return await _context.Payslips
            .Include(p => p.Employee)
            .Include(p => p.Branch)
            .Include(p => p.CashbookTransaction)
            .FirstOrDefaultAsync(p => p.EmployeeId == employeeId && p.PeriodMonth == periodMonth && p.TenantId == _tenantContext.CurrentTenantId, cancellationToken);
    }

    public async Task<(IReadOnlyList<Payslip> Items, int TotalCount)> GetPagedListAsync(
        PayslipQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Payslips
            .AsNoTracking()
            .Include(p => p.Employee)
            .Include(p => p.Branch)
            .Include(p => p.CashbookTransaction)
            .Where(p => p.TenantId == _tenantContext.CurrentTenantId);

        if (!string.IsNullOrWhiteSpace(filter.PeriodMonth))
        {
            query = query.Where(p => p.PeriodMonth == filter.PeriodMonth);
        }

        if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
        {
            query = query.Where(p => p.BranchId == filter.BranchId.Value);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(p => p.Status == filter.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var keyword = filter.Keyword.Trim().ToLower();
            query = query.Where(p =>
                p.PayslipCode.ToLower().Contains(keyword) ||
                (p.Employee != null && (p.Employee.FullName.ToLower().Contains(keyword) || p.Employee.EmployeeCode.ToLower().Contains(keyword)))
            );
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var page = filter.Page > 0 ? filter.Page : 1;
        var pageSize = filter.PageSize > 0 ? filter.PageSize : 20;

        var items = await query
            .OrderByDescending(p => p.PeriodMonth)
            .ThenBy(p => p.Employee != null ? p.Employee.EmployeeCode : string.Empty)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<Payslip>> GetByPeriodAndBranchAsync(
        string periodMonth,
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Payslips
            .Include(p => p.Employee)
            .Include(p => p.Branch)
            .Where(p => p.TenantId == _tenantContext.CurrentTenantId && p.PeriodMonth == periodMonth);

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            query = query.Where(p => p.BranchId == branchId.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Payslip payslip, CancellationToken cancellationToken = default)
    {
        await _context.Payslips.AddAsync(payslip, cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<Payslip> payslips, CancellationToken cancellationToken = default)
    {
        await _context.Payslips.AddRangeAsync(payslips, cancellationToken);
    }

    public void Update(Payslip payslip)
    {
        _context.Payslips.Update(payslip);
    }

    public void Delete(Payslip payslip)
    {
        _context.Payslips.Remove(payslip);
    }

    public async Task<int> GetCountByPeriodMonthAsync(string periodMonth, CancellationToken cancellationToken = default)
    {
        return await _context.Payslips
            .Where(p => p.TenantId == _tenantContext.CurrentTenantId && p.PeriodMonth == periodMonth)
            .CountAsync(cancellationToken);
    }

    public async Task AddCashbookTransactionAsync(Domain.Entities.Finance.CashbookTransaction transaction, CancellationToken cancellationToken = default)
    {
        await _context.CashbookTransactions.AddAsync(transaction, cancellationToken);
    }
}
