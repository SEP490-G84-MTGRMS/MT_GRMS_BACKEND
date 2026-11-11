using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Auth.Dtos;

public record LoginRequest(
    string UsernameOrEmail,
    string Password,
    string? TenantCode
);

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    UserInfo User
);

public record UserInfo(
    string Id,
    string Username,
    string FullName,
    string Email,
    string RoleName,
    Guid? BranchId,
    string TenantId,
    bool IsAdmin = false
)
{
    public string Role => RoleName;
    public UserRole UserRole => UserRoleNames.FromString(RoleName);
}

public record RefreshTokenRequest(
    string RefreshToken
);

public record RegisterTenantRequest(
    string FullName,
    string StoreName,
    string Phone,
    string? Email,
    string Password,
    bool AgreeTerms = true,
    string? OtpCode = null
);

public record SendRegistrationOtpRequest(
    string Phone,
    string? StoreName = null,
    string? FullName = null,
    string? Email = null
);

public record SendRegistrationOtpResponse(
    bool Success,
    string Message,
    string MaskedPhone,
    int ExpiresInSeconds,
    bool IsRealSmsSent,
    string? DevOtpCode = null
);

public record VerifyRegistrationOtpRequest(
    string Phone,
    string OtpCode
);

public record ForgotPasswordRequest(
    string Email
);

public record VerifyOtpRequest(
    string Email,
    string OtpCode
);

public record ResetPasswordRequest(
    string Email,
    string OtpCode,
    string NewPassword
);
