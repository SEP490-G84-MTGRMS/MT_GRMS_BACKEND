using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.PosShifts;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class ShiftRepository : GenericRepository<ShiftZReport>, IShiftRepository
{
    public ShiftRepository(AppDbContext context) : base(context)
    {
    }

    public override async Task<ShiftZReport?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ShiftZReports
            .Include(s => s.CashMovements)
            .Include(s => s.Branch)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<ShiftZReport?> GetActiveShiftAsync(Guid branchId, Guid? tillId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.ShiftZReports
            .Include(s => s.CashMovements)
            .Include(s => s.Branch)
            .Where(s => s.BranchId == branchId && s.Status == ShiftState.Open);

        if (tillId.HasValue && tillId.Value != Guid.Empty)
        {
            query = query.Where(s => s.TillId == tillId.Value);
        }

        return await query.OrderByDescending(s => s.OpenedAtUtc).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ShiftZReport?> GetLatestClosedShiftAsync(Guid branchId, Guid? tillId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.ShiftZReports
            .Where(s => s.BranchId == branchId && s.Status == ShiftState.Closed);

        if (tillId.HasValue && tillId.Value != Guid.Empty)
        {
            query = query.Where(s => s.TillId == tillId.Value);
        }

        return await query.OrderByDescending(s => s.ClosedAtUtc ?? s.OpenedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShiftZReport>> GetHistoryAsync(
        Guid branchId,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ShiftZReports
            .Include(s => s.CashMovements)
            .Include(s => s.Branch)
            .Where(s => s.BranchId == branchId);

        if (fromUtc.HasValue)
        {
            query = query.Where(s => s.OpenedAtUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(s => s.OpenedAtUtc <= toUtc.Value);
        }

        return await query.OrderByDescending(s => s.OpenedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task AddShiftAsync(ShiftZReport shift, CancellationToken cancellationToken = default)
    {
        await _context.ShiftZReports.AddAsync(shift, cancellationToken);
    }

    public async Task AddCashMovementAsync(ShiftCashMovement movement, CancellationToken cancellationToken = default)
    {
        await _context.ShiftCashMovements.AddAsync(movement, cancellationToken);
    }

    public async Task<(decimal CashSales, decimal BankRevenue, int OrdersCount)> GetShiftSalesMetricsAsync(
        Guid shiftId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _context.Orders
            .Where(o => o.ShiftId == shiftId && o.Status != "Cancelled")
            .ToListAsync(cancellationToken);

        decimal cashSales = orders
            .Where(o => string.Equals(o.PaymentMethod, "CASH", StringComparison.OrdinalIgnoreCase))
            .Sum(o => o.FinalAmount);

        decimal bankRevenue = orders
            .Where(o => !string.Equals(o.PaymentMethod, "CASH", StringComparison.OrdinalIgnoreCase))
            .Sum(o => o.FinalAmount);

        int count = orders.Count;

        return (cashSales, bankRevenue, count);
    }

    public async Task<string> GenerateNextShiftCodeAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var datePrefix = today.ToString("yyyyMMdd");

        var countToday = await _context.ShiftZReports
            .CountAsync(s => s.OpenedAtUtc >= today && s.OpenedAtUtc < tomorrow, cancellationToken);

        return $"CA-{datePrefix}-{(countToday + 1):D2}";
    }

    public async Task<ShiftCashMovement?> GetCashMovementByIdAsync(Guid movementId, CancellationToken cancellationToken = default)
    {
        return await _context.ShiftCashMovements
            .Include(m => m.Shift)
            .FirstOrDefaultAsync(m => m.Id == movementId, cancellationToken);
    }

    public async Task<IReadOnlyList<ShiftCashMovement>> GetPendingMovementsAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        return await _context.ShiftCashMovements
            .Include(m => m.Shift)
            .Where(m => m.Shift != null && m.Shift.BranchId == branchId &&
                        (m.Status == CashMovementStatus.PendingApproval ||
                         (m.Category == CashMovementCategory.CASH_DRAW && !m.IsConfirmedByOwner)))
            .OrderByDescending(m => m.OccurredAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<StorePaymentConfig?> GetPaymentConfigAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        return await _context.StorePaymentConfigs
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && !c.IsDeleted, cancellationToken);
    }

    public async Task SavePaymentConfigAsync(StorePaymentConfig config, CancellationToken cancellationToken = default)
    {
        var existing = await _context.StorePaymentConfigs
            .FirstOrDefaultAsync(c => c.TenantId == config.TenantId && !c.IsDeleted, cancellationToken);
        if (existing == null)
        {
            await _context.StorePaymentConfigs.AddAsync(config, cancellationToken);
        }
        else
        {
            existing.CashOutApprovalThreshold = config.CashOutApprovalThreshold;
            existing.CashOutShiftTotalLimit = config.CashOutShiftTotalLimit;
            existing.UpdatedAt = DateTime.UtcNow;
        }
    }
}
