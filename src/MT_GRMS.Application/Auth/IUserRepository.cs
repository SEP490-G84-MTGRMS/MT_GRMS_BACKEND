using System.Linq.Expressions;
using MT_GRMS.Domain.Entities.Identity;

namespace MT_GRMS.Application.Auth;

/// <summary>
/// Repository quản lý dữ liệu tài khoản User tối giản (Id string Uid)
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<User>> FindAsync(Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(User user, CancellationToken cancellationToken = default);
    void Update(User user);
    void Delete(User user);
}
