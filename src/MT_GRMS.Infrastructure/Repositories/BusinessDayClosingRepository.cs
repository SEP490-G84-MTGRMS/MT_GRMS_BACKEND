using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.BusinessDays;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class BusinessDayClosingRepository : IBusinessDayClosingRepository
{
    private readonly AppDbContext _context;
    public BusinessDayClosingRepository(AppDbContext context) => _context = context;

    public Task<bool> IsBranchAccessibleAsync(string tenantId, Guid branchId, CancellationToken cancellationToken = default) =>
        _context.Branches.AnyAsync(b => b.TenantId == tenantId && b.Id == branchId, cancellationToken);

    public async Task<TimeSpan> GetBusinessDayStartTimeAsync(string tenantId, Guid branchId, CancellationToken cancellationToken = default) =>
        await _context.Branches.Where(b => b.TenantId == tenantId && b.Id == branchId).Select(b => b.BusinessDayStartTime).FirstOrDefaultAsync(cancellationToken) is var value && value != default ? value : new TimeSpan(6, 0, 0);

    public Task<BusinessDayClosing?> GetAsync(string tenantId, Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default) =>
        _context.BusinessDayClosings.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.BranchId == branchId && x.BusinessDate == businessDate && x.IsActive, cancellationToken);

    public Task<BusinessDayClosing?> GetLatestAsync(string tenantId, Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default) =>
        _context.BusinessDayClosings.Where(x => x.TenantId == tenantId && x.BranchId == branchId && x.BusinessDate == businessDate)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken);

    public Task<BusinessDayClosing?> GetByIdAsync(string tenantId, Guid id, CancellationToken cancellationToken = default) =>
        _context.BusinessDayClosings.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<BusinessDayClosing>> GetHistoryAsync(string tenantId, Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default) =>
        await _context.BusinessDayClosings.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.BranchId == branchId && x.BusinessDate == businessDate)
            .OrderByDescending(x => x.Version).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ShiftZReport>> GetShiftsAsync(string tenantId, Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default) =>
        await _context.ShiftZReports.Include(x => x.CashMovements)
            .Where(x => x.TenantId == tenantId && x.BranchId == branchId && x.BusinessDate == businessDate)
            .OrderBy(x => x.OpenedAtUtc).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Order>> GetShiftlessOrdersAsync(string tenantId, Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default) =>
        await _context.Orders.Where(x => x.TenantId == tenantId && x.BranchId == branchId && x.ShiftId == null && x.BusinessDate == businessDate)
            .ToListAsync(cancellationToken);

    public Task<bool> HasPendingPaymentOrdersAsync(string tenantId, Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default) =>
        _context.Orders.AnyAsync(x => x.TenantId == tenantId && x.BranchId == branchId && x.BusinessDate == businessDate &&
            (x.Status == "PendingPayment" || x.PaymentStatus == "PENDING_PAYMENT"), cancellationToken);

    public async Task<IReadOnlyList<FinanceAccount>> GetFundsAsync(string tenantId, Guid branchId, CancellationToken cancellationToken = default) =>
        await _context.FinanceAccounts
            .Where(x => x.TenantId == tenantId && x.BranchId == branchId && x.IsActive)
            .OrderBy(x => x.AccountType).ThenBy(x => x.AccountName)
            .ToListAsync(cancellationToken);

    public Task AddAsync(BusinessDayClosing closing, CancellationToken cancellationToken = default) =>
        _context.BusinessDayClosings.AddAsync(closing, cancellationToken).AsTask();
}
