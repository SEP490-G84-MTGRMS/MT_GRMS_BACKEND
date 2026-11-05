using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Auth;
using MT_GRMS.Domain.Entities.Identity;
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
        var cleanPhone = phone.Trim().Replace(" ", "").Replace(".", "").Replace("-", "");
        var cleanEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

        return await _context.Users.AnyAsync(u =>
            (u.Phone != null && u.Phone.Replace(" ", "").Replace(".", "").Replace("-", "") == cleanPhone)
            || (cleanEmail != null && u.Email.ToLower() == cleanEmail),
            cancellationToken);
    }

    public async Task<User> RegisterStoreOwnerAsync(User user, MT_GRMS.Domain.Entities.SaaS.Tenant tenant, MT_GRMS.Domain.Entities.SaaS.Branch branch, CancellationToken cancellationToken = default)
    {
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _context.Tenants.AddAsync(tenant, cancellationToken);
            await _context.Users.AddAsync(user, cancellationToken);

            var tenantUser = new TenantUser
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                Role = "STORE_OWNER",
                IsActive = true
            };
            await _context.TenantUsers.AddAsync(tenantUser, cancellationToken);

            await _context.Branches.AddAsync(branch, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return user;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
