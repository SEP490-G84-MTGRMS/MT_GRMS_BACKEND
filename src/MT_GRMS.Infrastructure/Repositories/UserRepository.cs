using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Auth;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .Include(u => u.TenantUsers)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var clean = username.Trim().ToLower();
        var phoneClean = clean.Replace(" ", "").Replace(".", "").Replace("-", "");

        return await _context.Users
            .Include(u => u.TenantUsers)
            .FirstOrDefaultAsync(u =>
                u.UserName.ToLower() == clean
                || u.Email.ToLower() == clean
                || (u.Phone != null && (u.Phone.ToLower() == clean || u.Phone.Replace(" ", "").Replace(".", "").Replace("-", "") == phoneClean)),
                cancellationToken);
    }

    public async Task<IReadOnlyList<User>> FindAsync(Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .Include(u => u.TenantUsers)
            .Where(predicate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .Include(u => u.TenantUsers)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        await _context.Users.AddAsync(user, cancellationToken);
    }

    public void Update(User user)
    {
        _context.Users.Update(user);
    }

    public void Delete(User user)
    {
        _context.Users.Remove(user);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ExistsByPhoneOrEmailAsync(string phone, string? email, CancellationToken cancellationToken = default)
    {
        var cleanPhone = System.Text.RegularExpressions.Regex.Replace(phone.Trim(), @"\D", "");
        if (cleanPhone.StartsWith("84") && cleanPhone.Length >= 10)
            cleanPhone = "0" + cleanPhone[2..];
        var cleanEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

        return await _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u =>
                u.UserName.ToLower() == cleanPhone.ToLower()
                || u.NormalizedPhone == cleanPhone
                || (u.Phone != null && u.Phone.Replace(" ", "").Replace(".", "").Replace("-", "") == cleanPhone)
                || (cleanEmail != null && (u.NormalizedEmail == cleanEmail || u.Email.ToLower() == cleanEmail)),
                cancellationToken);
    }

    public Task<User> RegisterStoreOwnerAsync(User user, MT_GRMS.Domain.Entities.SaaS.Tenant tenant, MT_GRMS.Domain.Entities.SaaS.Branch branch, CancellationToken cancellationToken = default)
        => RegisterStoreOwnerAsync(user, tenant, branch, null, cancellationToken);

    public async Task<User> RegisterStoreOwnerAsync(
        User user,
        MT_GRMS.Domain.Entities.SaaS.Tenant tenant,
        MT_GRMS.Domain.Entities.SaaS.Branch branch,
        MT_GRMS.Domain.Entities.System.AuditLog? auditLog,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = _context.Database.IsRelational() && _context.Database.CurrentTransaction == null
            ? await _context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        await _context.Tenants.AddAsync(tenant, cancellationToken);
        await _context.Users.AddAsync(user, cancellationToken);

        var tenantUser = new TenantUser
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Role = UserRoleNames.StoreOwner,
            IsActive = true
        };
        await _context.TenantUsers.AddAsync(tenantUser, cancellationToken);

        await _context.Branches.AddAsync(branch, cancellationToken);

        if (auditLog != null)
        {
            await _context.AuditLogs.AddAsync(auditLog, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);

        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return user;
    }

    public async Task<Guid?> GetEmployeeBranchIdAsync(string userId, string tenantId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(tenantId))
            return null;

        return await _context.Employees
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => !e.IsDeleted
                && e.TenantId == tenantId
                && e.UserId == userId
                && e.Status == EmployeeStatus.Active)
            .Select(e => (Guid?)e.BranchId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
