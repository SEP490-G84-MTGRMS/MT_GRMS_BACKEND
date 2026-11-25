using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Sales;

namespace MT_GRMS.Application.BusinessDays;

public interface IBusinessDayClosingRepository
{
    Task<bool> IsBranchAccessibleAsync(string tenantId, Guid branchId, CancellationToken cancellationToken = default);
    Task<TimeSpan> GetBusinessDayStartTimeAsync(string tenantId, Guid branchId, CancellationToken cancellationToken = default);
    Task<BusinessDayClosing?> GetAsync(string tenantId, Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default);
    Task<BusinessDayClosing?> GetLatestAsync(string tenantId, Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default);
    Task<BusinessDayClosing?> GetByIdAsync(string tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BusinessDayClosing>> GetHistoryAsync(string tenantId, Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShiftZReport>> GetShiftsAsync(string tenantId, Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetShiftlessOrdersAsync(string tenantId, Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetPostedPosSaleOrderIdsAsync(string tenantId, Guid branchId, Guid[] orderIds, CancellationToken cancellationToken = default);
    Task<bool> HasPendingPaymentOrdersAsync(string tenantId, Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FinanceAccount>> GetFundsAsync(string tenantId, Guid branchId, CancellationToken cancellationToken = default);
    Task<decimal> GetSafeRefundsAsync(string tenantId, Guid branchId, DateTime periodStartUtc, DateTime periodEndUtc, CancellationToken cancellationToken = default)
        => Task.FromResult(0m);
    Task AddAsync(BusinessDayClosing closing, CancellationToken cancellationToken = default);
}
