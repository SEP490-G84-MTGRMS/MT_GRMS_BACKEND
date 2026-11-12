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

    public async Task<TimeSpan> GetBusinessDayStartTimeAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        await _context.Branches.Where(b => b.Id == branchId).Select(b => b.BusinessDayStartTime).FirstOrDefaultAsync(cancellationToken) is var value && value != default ? value : new TimeSpan(6, 0, 0);

    public Task<BusinessDayClosing?> GetAsync(Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default) =>
        _context.BusinessDayClosings.FirstOrDefaultAsync(x => x.BranchId == branchId && x.BusinessDate == businessDate, cancellationToken);

    public async Task<IReadOnlyList<ShiftZReport>> GetShiftsAsync(Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default) =>
        await _context.ShiftZReports.Include(x => x.CashMovements)
            .Where(x => x.BranchId == branchId && x.BusinessDate == businessDate)
            .OrderBy(x => x.OpenedAtUtc).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Order>> GetShiftlessOrdersAsync(Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default) =>
        await _context.Orders.Where(x => x.BranchId == branchId && x.ShiftId == null && x.BusinessDate == businessDate && x.BusinessDayClosingId == null)
            .ToListAsync(cancellationToken);

    public Task<bool> HasPendingPaymentOrdersAsync(Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default) =>
        _context.Orders.AnyAsync(x => x.BranchId == branchId && x.BusinessDate == businessDate &&
            (x.Status == "PendingPayment" || x.PaymentStatus == "PENDING_PAYMENT"), cancellationToken);

    public Task AddAsync(BusinessDayClosing closing, CancellationToken cancellationToken = default) =>
        _context.BusinessDayClosings.AddAsync(closing, cancellationToken).AsTask();
}
