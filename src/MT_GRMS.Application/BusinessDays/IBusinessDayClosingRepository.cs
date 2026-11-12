using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Sales;

namespace MT_GRMS.Application.BusinessDays;

public interface IBusinessDayClosingRepository
{
    Task<TimeSpan> GetBusinessDayStartTimeAsync(Guid branchId, CancellationToken cancellationToken = default);
    Task<BusinessDayClosing?> GetAsync(Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShiftZReport>> GetShiftsAsync(Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetShiftlessOrdersAsync(Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default);
    Task<bool> HasPendingPaymentOrdersAsync(Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default);
    Task AddAsync(BusinessDayClosing closing, CancellationToken cancellationToken = default);
}
