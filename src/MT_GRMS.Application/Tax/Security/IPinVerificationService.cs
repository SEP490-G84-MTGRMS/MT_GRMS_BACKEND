namespace MT_GRMS.Application.Tax.Security;

/// <summary>
/// Result of store owner PIN verification attempt.
/// </summary>
public record PinVerificationResult(
    bool IsSuccess,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    int RemainingAttempts = 5,
    DateTime? LockedUntilUtc = null
);

/// <summary>
/// Service contract for verifying and managing store owner security PIN.
/// </summary>
public interface IPinVerificationService
{
    /// <summary>
    /// Verifies store owner security PIN using constant-time comparison and auto-lock policy.
    /// </summary>
    Task<PinVerificationResult> VerifyStoreOwnerPinAsync(string userId, string inputPin, CancellationToken ct = default);

    /// <summary>
    /// Sets or updates store owner security PIN using PBKDF2 with >= 600.000 iterations.
    /// </summary>
    Task SetStoreOwnerPinAsync(string userId, string newPin, CancellationToken ct = default);
}
