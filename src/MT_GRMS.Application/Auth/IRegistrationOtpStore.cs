namespace MT_GRMS.Application.Auth;

public static class RegistrationOtpStates
{
    public const string Pending = "Pending";
    public const string Reserved = "Reserved";
    public const string Consumed = "Consumed";
}

public sealed record RegistrationOtpState(
    string ChallengeId,
    string NormalizedEmail,
    string OtpHash,
    string Intent,
    string RequestFingerprint,
    string State,
    string? ReservedByIdempotencyKey,
    DateTime ExpiresAtUtc,
    int FailedAttempts);

public interface IRegistrationOtpStore
{
    Task CreateAsync(RegistrationOtpState state, CancellationToken cancellationToken);
    Task<RegistrationOtpState?> GetAsync(string? challengeId, string normalizedEmail, CancellationToken cancellationToken);
    Task<bool> TryReserveAsync(string challengeId, string idempotencyKey, DateTime utcNow, CancellationToken cancellationToken);
    Task ReleaseReservationAsync(string challengeId, string idempotencyKey, DateTime utcNow, CancellationToken cancellationToken);
    Task ConsumeAsync(string challengeId, string idempotencyKey, CancellationToken cancellationToken);
    Task<int> IncrementFailureOrRemoveAsync(string challengeId, int maxAttempts, CancellationToken cancellationToken);
    Task RemoveAsync(string challengeId, CancellationToken cancellationToken);
}
