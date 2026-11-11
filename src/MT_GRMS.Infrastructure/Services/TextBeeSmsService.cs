using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MT_GRMS.Application.Common.Interfaces;

namespace MT_GRMS.Infrastructure.Services;

public class TextBeeSmsService : ISmsService
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly TextBeeSettings _settings;
    private readonly ILogger<TextBeeSmsService> _logger;

    public TextBeeSmsService(IOptions<TextBeeSettings> options, ILogger<TextBeeSmsService> logger)
    {
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<SmsSendResult> SendRegistrationOtpAsync(
        string phoneNumber,
        string storeName,
        string otpCode,
        CancellationToken cancellationToken = default)
    {
        var e164Phone = NormalizeToE164(phoneNumber);
        var maskedPhone = MaskPhoneNumber(phoneNumber);
        var displayStore = string.IsNullOrWhiteSpace(storeName) ? "MT-GRMS" : storeName.Trim();

        var hasTextBeeConfig =
            !string.IsNullOrWhiteSpace(_settings.ApiKey) &&
            !string.IsNullOrWhiteSpace(_settings.DeviceId) &&
            !_settings.ApiKey.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) &&
            !_settings.DeviceId.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);

        if (!hasTextBeeConfig)
        {
            _logger.LogInformation(
                "[TEXTBEE-SMS DEV LOG] Chưa cấu hình TextBeeSettings. Gửi OTP mô phỏng tới {Phone}. Mã OTP: {OtpCode}",
                phoneNumber,
                otpCode);

            Console.WriteLine(
                $"\n======================================================================\n" +
                $"[MT-GRMS TEXTBEE SMS-OTP DEV LOG]\n" +
                $"Số điện thoại nhận: {phoneNumber} ({e164Phone})\n" +
                $"Cửa hàng đăng ký: {displayStore}\n" +
                $"MÃ XÁC THỰC SMS OTP: >>> {otpCode} <<<\n" +
                $"======================================================================\n");

            return new SmsSendResult(
                IsRealSmsSent: false,
                MaskedPhone: maskedPhone,
                DevFallbackOtp: otpCode);
        }

        try
        {
            var deviceId = _settings.DeviceId.Trim();
            var apiKey = _settings.ApiKey.Trim();
            var requestUrl = $"https://api.textbee.dev/api/v1/gateway/devices/{deviceId}/send-sms";
            var smsMessage = $"[MT-GRMS] Ma xac thuc OTP mo cua hang {displayStore} cua ban la: {otpCode}. Ma co hieu luc trong 5 phut.";

            var payload = new
            {
                recipients = new[] { e164Phone },
                message = smsMessage
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Add("x-api-key", apiKey);

            using var response = await SharedHttpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Đã gửi SMS OTP qua TextBee Android Gateway tới số: {Phone}", e164Phone);
                return new SmsSendResult(
                    IsRealSmsSent: true,
                    MaskedPhone: maskedPhone,
                    DevFallbackOtp: null);
            }

            _logger.LogWarning(
                "TextBee API trả về HTTP {StatusCode} khi gửi SMS tới {Phone}: {ResponseBody}. Chuyển sang fallback Dev OTP.",
                (int)response.StatusCode,
                e164Phone,
                responseBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi kết nối TextBee API khi gửi SMS tới {Phone}. Chuyển sang fallback Dev OTP.", e164Phone);
        }

        return new SmsSendResult(
            IsRealSmsSent: false,
            MaskedPhone: maskedPhone,
            DevFallbackOtp: otpCode);
    }

    public static string NormalizeToE164(string rawPhone)
    {
        if (string.IsNullOrWhiteSpace(rawPhone)) return string.Empty;
        var trimmed = rawPhone.Trim();
        var digits = Regex.Replace(trimmed, @"[^\d]", "");

        if (trimmed.StartsWith("+")) return "+" + digits;
        if (digits.StartsWith("84") && digits.Length >= 10) return "+" + digits;
        if (digits.StartsWith("0") && digits.Length >= 9) return "+84" + digits[1..];
        return "+84" + digits;
    }

    public static string MaskPhoneNumber(string rawPhone)
    {
        var digits = Regex.Replace(rawPhone ?? string.Empty, @"[^\d]", "");
        if (digits.Length < 7) return rawPhone ?? string.Empty;
        return $"{digits[..3]}***{digits[^3..]}";
    }
}
