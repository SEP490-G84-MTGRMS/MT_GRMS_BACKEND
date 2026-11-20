using MT_GRMS.Application.Tax.Dtos;

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

    /// <summary>
    /// Đổi mã PIN bảo mật: xác thực qua mã PIN hiện tại hoặc mật khẩu đăng nhập, sau đó đặt PIN mới.
    /// </summary>
    Task ChangeSecurityPinAsync(string userId, ChangeSecurityPinRequest request, CancellationToken ct = default);

    /// <summary>
    /// Khôi phục mã PIN bảo mật khi quên: xác thực danh tính chủ hộ qua mật khẩu đăng nhập, đặt lại PIN mới và mở khóa tài khoản.
    /// </summary>
    Task ResetSecurityPinAsync(string userId, ResetSecurityPinRequest request, CancellationToken ct = default);
}
