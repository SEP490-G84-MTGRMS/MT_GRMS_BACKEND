namespace MT_GRMS.Application.Common;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    bool HasActiveTransaction => false;

    /// <summary>
    /// Chạy toàn bộ thao tác trong 1 transaction tương thích NpgsqlRetryingExecutionStrategy.
    /// </summary>
    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
        => operation(cancellationToken);
}