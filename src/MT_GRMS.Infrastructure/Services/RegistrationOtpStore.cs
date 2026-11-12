using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Auth;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Services;

public sealed class RegistrationOtpChallenge
{
    public string Key { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string OtpHash { get; set; } = string.Empty;
    public string Intent { get; set; } = "ANY";
    public string RequestFingerprint { get; set; } = string.Empty;
    public string State { get; set; } = RegistrationOtpStates.Pending;
    public string? ReservedByIdempotencyKey { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RegistrationOtpStore(AppDbContext db) : IRegistrationOtpStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InMemoryLocks = new();

    public async Task CreateAsync(RegistrationOtpState state, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (db.Database.IsRelational())
        {
            await db.Set<RegistrationOtpChallenge>()
                .Where(x => x.NormalizedEmail == state.NormalizedEmail && x.State != RegistrationOtpStates.Consumed)
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            var existing = await db.Set<RegistrationOtpChallenge>()
                .Where(x => x.NormalizedEmail == state.NormalizedEmail && x.State != RegistrationOtpStates.Consumed)
                .ToListAsync(cancellationToken);
            if (existing.Count > 0)
            {
                db.RemoveRange(existing);
            }
        }

        db.Add(new RegistrationOtpChallenge
        {
            Key = state.ChallengeId,
            NormalizedEmail = state.NormalizedEmail,
            OtpHash = state.OtpHash,
            Intent = state.Intent,
            RequestFingerprint = state.RequestFingerprint,
            State = state.State,
            ReservedByIdempotencyKey = state.ReservedByIdempotencyKey,
            ExpiresAtUtc = state.ExpiresAtUtc,
            FailedAttempts = state.FailedAttempts,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<RegistrationOtpState?> GetAsync(string? challengeId, string normalizedEmail, CancellationToken cancellationToken)
    {
        RegistrationOtpChallenge? row;
        if (!string.IsNullOrWhiteSpace(challengeId))
        {
            row = await db.Set<RegistrationOtpChallenge>()
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Key == challengeId, cancellationToken);
        }
        else
        {
            row = await db.Set<RegistrationOtpChallenge>()
                .AsNoTracking()
                .Where(x => x.NormalizedEmail == normalizedEmail && x.State != RegistrationOtpStates.Consumed)
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return row == null ? null : ToState(row);
    }

    public async Task<bool> TryReserveAsync(string challengeId, string idempotencyKey, DateTime utcNow, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            var updated = await db.Set<RegistrationOtpChallenge>()
                .Where(x => x.Key == challengeId &&
                            x.ExpiresAtUtc > utcNow &&
                            (x.State == RegistrationOtpStates.Pending ||
                             (x.State == RegistrationOtpStates.Reserved && x.ReservedByIdempotencyKey == idempotencyKey)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.State, RegistrationOtpStates.Reserved)
                    .SetProperty(x => x.ReservedByIdempotencyKey, idempotencyKey)
                    .SetProperty(x => x.UpdatedAtUtc, utcNow), cancellationToken);

            return updated == 1;
        }

        var gate = InMemoryLocks.GetOrAdd(challengeId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var row = await db.Set<RegistrationOtpChallenge>().SingleOrDefaultAsync(x => x.Key == challengeId, cancellationToken);
            if (row == null || row.ExpiresAtUtc <= utcNow)
                return false;

            if (row.State == RegistrationOtpStates.Pending ||
                (row.State == RegistrationOtpStates.Reserved && row.ReservedByIdempotencyKey == idempotencyKey))
            {
                row.State = RegistrationOtpStates.Reserved;
                row.ReservedByIdempotencyKey = idempotencyKey;
                row.UpdatedAtUtc = utcNow;
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }

            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ReleaseReservationAsync(string challengeId, string idempotencyKey, DateTime utcNow, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            await db.Set<RegistrationOtpChallenge>()
                .Where(x => x.Key == challengeId &&
                            x.State == RegistrationOtpStates.Reserved &&
                            x.ReservedByIdempotencyKey == idempotencyKey &&
                            x.ExpiresAtUtc > utcNow)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.State, RegistrationOtpStates.Pending)
                    .SetProperty(x => x.ReservedByIdempotencyKey, (string?)null)
                    .SetProperty(x => x.UpdatedAtUtc, utcNow), cancellationToken);
            return;
        }

        var gate = InMemoryLocks.GetOrAdd(challengeId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var row = await db.Set<RegistrationOtpChallenge>().SingleOrDefaultAsync(x => x.Key == challengeId, cancellationToken);
            if (row != null &&
                row.State == RegistrationOtpStates.Reserved &&
                row.ReservedByIdempotencyKey == idempotencyKey &&
                row.ExpiresAtUtc > utcNow)
            {
                row.State = RegistrationOtpStates.Pending;
                row.ReservedByIdempotencyKey = null;
                row.UpdatedAtUtc = utcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ConsumeAsync(string challengeId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (db.Database.IsRelational())
        {
            await db.Set<RegistrationOtpChallenge>()
                .Where(x => x.Key == challengeId &&
                            x.State == RegistrationOtpStates.Reserved &&
                            x.ReservedByIdempotencyKey == idempotencyKey)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.State, RegistrationOtpStates.Consumed)
                    .SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);
            return;
        }

        var gate = InMemoryLocks.GetOrAdd(challengeId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var row = await db.Set<RegistrationOtpChallenge>().SingleOrDefaultAsync(x => x.Key == challengeId, cancellationToken);
            if (row != null && row.State == RegistrationOtpStates.Reserved && row.ReservedByIdempotencyKey == idempotencyKey)
            {
                row.State = RegistrationOtpStates.Consumed;
                row.UpdatedAtUtc = now;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<int> IncrementFailureOrRemoveAsync(string challengeId, int maxAttempts, CancellationToken cancellationToken)
    {
        var row = await db.Set<RegistrationOtpChallenge>().SingleOrDefaultAsync(x => x.Key == challengeId, cancellationToken);
        if (row == null) return maxAttempts;

        row.FailedAttempts += 1;
        row.UpdatedAtUtc = DateTime.UtcNow;
        if (row.FailedAttempts >= maxAttempts)
        {
            db.Remove(row);
        }

        await db.SaveChangesAsync(cancellationToken);
        return row.FailedAttempts;
    }

    public async Task RemoveAsync(string challengeId, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            await db.Set<RegistrationOtpChallenge>().Where(x => x.Key == challengeId).ExecuteDeleteAsync(cancellationToken);
            return;
        }

        var row = await db.Set<RegistrationOtpChallenge>().SingleOrDefaultAsync(x => x.Key == challengeId, cancellationToken);
        if (row != null)
        {
            db.Remove(row);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static RegistrationOtpState ToState(RegistrationOtpChallenge row)
        => new(
            row.Key,
            row.NormalizedEmail,
            row.OtpHash,
            row.Intent,
            row.RequestFingerprint,
            row.State,
            row.ReservedByIdempotencyKey,
            row.ExpiresAtUtc,
            row.FailedAttempts);
}
