namespace MT_GRMS.Application.Common;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}