using MT_GRMS.Domain.Enums;
using System.Text.Json.Serialization;

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

public record SendLoginOtpResponse(
    bool RequiresOtp,
    string TempToken,
    string MaskedEmail,
    string TargetEmail,
    int ExpiresInSeconds,
    bool IsRealEmailSent,
    string Message,
    string? DevOtpCode = null,
    UserInfo? User = null,
    [property: JsonIgnore] LoginResponse? Session = null
);

public record VerifyLoginOtpRequest(
    string TempToken,
    string OtpCode
);

public record TwoFactorStatusResponse(bool Enabled, string MaskedEmail, bool IsEmailVerified);
public record TwoFactorChallengeRequest(bool Enabled, string CurrentPassword);
public record TwoFactorChallengeResponse(string ChallengeToken, string MaskedEmail, int ExpiresInSeconds, string? DevOtpCode = null);
public record TwoFactorConfirmRequest(string ChallengeToken, string OtpCode);

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
    string? OtpCode = null,
    bool StartTrial = false,
    string? IdempotencyKey = null,
    string? ChallengeId = null
);

public record SendRegistrationOtpRequest(
    string? Phone = null,
    string? StoreName = null,
    string? FullName = null,
    string? Email = null,
    bool? StartTrial = null
);

public record SendRegistrationOtpResponse(
    bool Success,
    string Message,
    string MaskedEmail,
    int ExpiresInSeconds,
    bool IsRealEmailSent,
    string? DevOtpCode = null,
    string? ChallengeId = null
);

public record VerifyRegistrationOtpRequest(
    string EmailOrPhone,
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
