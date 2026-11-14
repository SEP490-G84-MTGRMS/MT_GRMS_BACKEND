using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using System.Data;

namespace MT_GRMS.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public bool HasActiveTransaction => _context.Database.IsRelational() && _context.Database.CurrentTransaction != null;

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsRelational() && _context.Database.CurrentTransaction == null)
        {
            await _context.Database.BeginTransactionAsync(cancellationToken);
        }
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsRelational() && _context.Database.CurrentTransaction != null)
        {
            await _context.Database.CommitTransactionAsync(cancellationToken);
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsRelational() && _context.Database.CurrentTransaction != null)
        {
            await _context.Database.RollbackTransactionAsync(cancellationToken);
        }
    }

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
    {
        await ExecuteInTransactionAsync(async token =>
        {
            await operation(token);
            return true;
        }, cancellationToken, isolationLevel);
    }

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
    {
        if (!_context.Database.IsRelational())
        {
            return await operation(cancellationToken);
        }

        if (_context.Database.CurrentTransaction != null)
        {
            return await operation(cancellationToken);
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
            try
            {
                var result = await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }
}
