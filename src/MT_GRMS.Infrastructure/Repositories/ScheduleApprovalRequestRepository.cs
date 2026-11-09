using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.HR.Schedules;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class ScheduleApprovalRequestRepository : GenericRepository<ScheduleApprovalRequest>, IScheduleApprovalRequestRepository
{
    public ScheduleApprovalRequestRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<ScheduleApprovalRequest?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(r => r.Schedule)
                .ThenInclude(s => s.Branch)
            .Include(r => r.Employee)
            .Include(r => r.TargetEmployee)
            .Include(r => r.TargetSchedule)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ScheduleApprovalRequest>> GetByScheduleIdAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(r => r.Schedule)
            .Include(r => r.Employee)
            .Include(r => r.TargetEmployee)
            .Where(r => r.ScheduleId == scheduleId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScheduleApprovalRequest>> GetPendingAsync(Guid? branchId, ScheduleRequestType? requestType, CancellationToken cancellationToken = default)
    {
        return await GetRequestsAsync(branchId, requestType, ScheduleApprovalStatus.Pending, cancellationToken);
    }

    public async Task<IReadOnlyList<ScheduleApprovalRequest>> GetRequestsAsync(Guid? branchId, ScheduleRequestType? requestType, ScheduleApprovalStatus? status, CancellationToken cancellationToken = default)
    {
        var query = _dbSet
            .Include(r => r.Schedule)
                .ThenInclude(s => s.Branch)
            .Include(r => r.Employee)
            .Include(r => r.TargetEmployee)
            .Include(r => r.TargetSchedule)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(r => r.Status == status.Value);
        }

        if (branchId.HasValue)
        {
            query = query.Where(r => r.BranchId == branchId.Value);
        }

        if (requestType.HasValue)
        {
            query = query.Where(r => r.RequestType == requestType.Value);
        }

        return await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasPendingRequestOfTypeAsync(Guid scheduleId, ScheduleRequestType requestType, CancellationToken cancellationToken = default)
    {
        return await _dbSet.AnyAsync(r => r.ScheduleId == scheduleId && r.RequestType == requestType && r.Status == ScheduleApprovalStatus.Pending, cancellationToken);
    }
}

