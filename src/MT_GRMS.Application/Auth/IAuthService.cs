using MT_GRMS.Application.Auth.Dtos;

namespace MT_GRMS.Application.Auth;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<SendLoginOtpResponse> SendLoginOtpAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<LoginResponse> VerifyLoginOtpAsync(VerifyLoginOtpRequest request, CancellationToken cancellationToken = default);
    Task<LoginResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
    Task<LoginResponse> RegisterTenantAsync(RegisterTenantRequest request, CancellationToken cancellationToken = default);
    Task<SendRegistrationOtpResponse> SendRegistrationOtpAsync(SendRegistrationOtpRequest request, CancellationToken cancellationToken = default);
    Task<bool> VerifyRegistrationOtpAsync(VerifyRegistrationOtpRequest request, CancellationToken cancellationToken = default);
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task<bool> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
    Task<UserInfo?> GetCurrentUserAsync(string userId, CancellationToken cancellationToken = default);
    Task RevokeRefreshTokenAsync(string userId, CancellationToken cancellationToken = default);
    Task<TwoFactorStatusResponse> GetTwoFactorStatusAsync(string userId, CancellationToken cancellationToken = default);
    Task<TwoFactorChallengeResponse> CreateTwoFactorChallengeAsync(string userId, TwoFactorChallengeRequest request, CancellationToken cancellationToken = default);
    Task<TwoFactorStatusResponse> ConfirmTwoFactorChallengeAsync(string userId, TwoFactorConfirmRequest request, CancellationToken cancellationToken = default);
}
