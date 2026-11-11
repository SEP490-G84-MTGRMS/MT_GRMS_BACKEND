namespace MT_GRMS.Application.Common.Interfaces;

public record SmsSendResult(
    bool IsRealSmsSent,
    string MaskedPhone,
    string? DevFallbackOtp = null
);

public interface ISmsService
{
    Task<SmsSendResult> SendRegistrationOtpAsync(
        string phoneNumber,
        string storeName,
        string otpCode,
        CancellationToken cancellationToken = default);
}
