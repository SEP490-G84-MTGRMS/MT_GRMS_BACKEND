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
    Guid Id,
    string Username,
    string FullName,
    string Email,
    string RoleName,
    Guid? BranchId,
    string TenantId
);

public record RefreshTokenRequest(
    string RefreshToken
);
