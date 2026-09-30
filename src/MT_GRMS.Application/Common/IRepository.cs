using System.Linq.Expressions;
using MT_GRMS.Domain.Common;

namespace MT_GRMS.Application.Common;

/// <summary>
/// Interface Generic Repository chuẩn cho toàn bộ Entity kế thừa BaseTenantEntity
/// </summary>
public interface IRepository<T> where T : BaseTenantEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    Task AddAsync(T entity, CancellationToken cancellationToken = default);
    void Update(T entity);
    void Delete(T entity);
}
