using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.PosShifts;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.HR;
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
            .Where(s => s.BranchId == branchId && (s.Status == ShiftState.Open || s.Status == ShiftState.Closing));

        if (tillId.HasValue && tillId.Value != Guid.Empty)
        {
            query = query.Where(s => s.TillId == tillId.Value);
        }

        return await query.OrderByDescending(s => s.OpenedAtUtc).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<TimeSpan> GetBusinessDayStartTimeAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var value = await _context.Branches.Where(b => b.Id == branchId)
            .Select(b => b.BusinessDayStartTime).FirstOrDefaultAsync(cancellationToken);
        return value == default ? new TimeSpan(6, 0, 0) : value;
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
            .Where(o => o.ShiftId == shiftId && o.Status != "Cancelled" && o.Status != "PendingPayment" && o.PaymentStatus != "PENDING_PAYMENT")
            .ToListAsync(cancellationToken);

        decimal cashSales = orders
            .Where(o => string.Equals(o.PaymentMethod, "CASH", StringComparison.OrdinalIgnoreCase))
            .Sum(o => o.Status == "PendingPickup" ? o.DepositAmount : o.FinalAmount);

        decimal bankRevenue = orders
            .Where(o => !string.Equals(o.PaymentMethod, "CASH", StringComparison.OrdinalIgnoreCase))
            .Sum(o => o.Status == "PendingPickup" ? o.DepositAmount : o.FinalAmount);

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

    public async Task<IReadOnlyList<ShiftCashMovement>> GetPendingMovementsAsync(Guid? branchId, CancellationToken cancellationToken = default)
    {
        var query = _context.ShiftCashMovements
            .Include(m => m.Shift)
            .Where(m => m.Shift != null &&
                        (m.Status == CashMovementStatus.PendingApproval ||
                         (m.Category == CashMovementCategory.CASH_DRAW && !m.IsConfirmedByOwner)));

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            query = query.Where(m => m.Shift != null && m.Shift.BranchId == branchId.Value);
        }

        return await query
            .OrderByDescending(m => m.OccurredAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<MT_GRMS.Domain.Entities.SaaS.Tenant?> GetPaymentConfigAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        return await _context.Tenants.FirstOrDefaultAsync(c => c.Id == tenantId && !c.IsDeleted, cancellationToken);
    }

    public Task SavePaymentConfigAsync(MT_GRMS.Domain.Entities.SaaS.Tenant config, CancellationToken cancellationToken = default)
    {
        config.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public async Task<Guid?> ResolveBranchIdAsync(string? branchIdOrCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(branchIdOrCode))
        {
            var defaultBranch = await _context.Branches.OrderBy(b => b.Id).FirstOrDefaultAsync(cancellationToken);
            return defaultBranch?.Id;
        }

        if (Guid.TryParse(branchIdOrCode, out var guid) && guid != Guid.Empty)
        {
            return guid;
        }

        var branch = await _context.Branches
            .FirstOrDefaultAsync(b => b.BranchCode == branchIdOrCode || b.Name.Contains(branchIdOrCode), cancellationToken);

        if (branch != null) return branch.Id;

        var fallback = await _context.Branches.OrderBy(b => b.Id).FirstOrDefaultAsync(cancellationToken);
        return fallback?.Id;
    }

    public async Task<Employee?> GetEmployeeByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var employee = await _context.Employees
            .FirstOrDefaultAsync(e => e.UserId == userId, cancellationToken);

        if (employee == null && Guid.TryParse(userId, out var empId))
        {
            employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.Id == empId, cancellationToken);
        }

        return employee;
    }

    public async Task<IReadOnlyList<EmployeeSchedule>> GetCashierSchedulesAsync(Guid employeeId, Guid branchId, DateTime workDate, CancellationToken cancellationToken = default)
    {
        return await _context.EmployeeSchedules
            .Where(s => s.EmployeeId == employeeId && s.BranchId == branchId && s.WorkDate.Date == workDate.Date && s.Status != ScheduleStatus.Cancelled)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EmployeeSchedule>> GetBranchCashierSchedulesAsync(Guid branchId, DateTime workDate, CancellationToken cancellationToken = default)
    {
        return await _context.EmployeeSchedules
            .Include(s => s.Employee)
            .Where(s => s.BranchId == branchId && s.WorkDate.Date == workDate.Date && s.Status != ScheduleStatus.Cancelled)
            .ToListAsync(cancellationToken);
    }
}
