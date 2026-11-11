namespace MT_GRMS.Application.Common.Interfaces;

public interface IEmailService
{
    Task SendPasswordResetOtpAsync(string toEmail, string fullName, string otpCode, CancellationToken cancellationToken = default);
    Task<bool> SendLoginOtpAsync(string toEmail, string fullName, string otpCode, CancellationToken cancellationToken = default);
    Task<bool> SendTwoFactorSettingsOtpAsync(string toEmail, string fullName, string otpCode, CancellationToken cancellationToken = default)
        => SendLoginOtpAsync(toEmail, fullName, otpCode, cancellationToken);
    Task<bool> SendRegistrationOtpAsync(string toEmail, string fullName, string storeName, string otpCode, CancellationToken cancellationToken = default);
}
