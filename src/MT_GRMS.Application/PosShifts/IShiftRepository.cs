using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Application.PosShifts;

public interface IShiftRepository
{
    Task<ShiftZReport?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ShiftZReport?> GetActiveShiftAsync(Guid branchId, Guid? tillId = null, CancellationToken cancellationToken = default);
    Task<ShiftZReport?> GetLatestClosedShiftAsync(Guid branchId, Guid? tillId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShiftZReport>> GetHistoryAsync(Guid branchId, DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default);
    Task AddShiftAsync(ShiftZReport shift, CancellationToken cancellationToken = default);
    Task AddCashMovementAsync(ShiftCashMovement movement, CancellationToken cancellationToken = default);
    Task<(decimal CashSales, decimal BankRevenue, int OrdersCount)> GetShiftSalesMetricsAsync(Guid shiftId, CancellationToken cancellationToken = default);
    Task<string> GenerateNextShiftCodeAsync(Guid branchId, CancellationToken cancellationToken = default);
}

