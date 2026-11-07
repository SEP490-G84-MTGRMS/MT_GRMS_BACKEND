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
    bool AgreeTerms = true
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
