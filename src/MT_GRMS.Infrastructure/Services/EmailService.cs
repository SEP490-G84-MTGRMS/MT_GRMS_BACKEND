using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using MT_GRMS.Application.Common.Interfaces;

namespace MT_GRMS.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;
    private readonly IHostEnvironment _environment;

    public EmailService(IOptions<EmailSettings> options, ILogger<EmailService> logger, IHostEnvironment environment)
    {
        _settings = options.Value;
        _logger = logger;
        _environment = environment;
    }

    public async Task SendPasswordResetOtpAsync(
        string toEmail,
        string fullName,
        string otpCode,
        CancellationToken cancellationToken = default)
    {
        await SendOtpEmailInternalAsync(
            toEmail: toEmail,
            fullName: fullName,
            subject: "[MT-GRMS] Mã xác thực OTP khôi phục mật khẩu tài khoản",
            description: "Hệ thống nhận được yêu cầu khôi phục mật khẩu cho tài khoản liên kết với địa chỉ email này. Vui lòng sử dụng mã xác thực OTP dưới đây để hoàn tất:",
            otpCode: otpCode,
            validityMinutes: 10,
            cancellationToken: cancellationToken);
    }

    public Task<bool> SendLoginOtpAsync(
        string toEmail,
        string fullName,
        string otpCode,
        CancellationToken cancellationToken = default)
    {
        return SendOtpEmailInternalAsync(
            toEmail: toEmail,
            fullName: fullName,
            subject: $"[MT-GRMS] Mã OTP đăng nhập 2 lớp: {otpCode}",
            description: "Hệ thống vừa ghi nhận yêu cầu đăng nhập vào tài khoản quản lý của bạn. Vui lòng nhập mã xác thực 2 bước (2FA) dưới đây để truy cập hệ thống:",
            otpCode: otpCode,
            validityMinutes: 5,
            cancellationToken: cancellationToken);
    }

    public Task<bool> SendRegistrationOtpAsync(
        string toEmail,
        string fullName,
        string storeName,
        string otpCode,
        CancellationToken cancellationToken = default)
    {
        var storeDisplay = string.IsNullOrWhiteSpace(storeName) ? "MT-GRMS" : storeName.Trim();
        return SendOtpEmailInternalAsync(
            toEmail: toEmail,
            fullName: fullName,
            subject: $"[MT-GRMS] Mã OTP kích hoạt cửa hàng {storeDisplay}: {otpCode}",
            description: $"Chào mừng bạn đăng ký mở cửa hàng <strong>{WebUtility.HtmlEncode(storeDisplay)}</strong> trên nền tảng MT-GRMS. Vui lòng nhập mã xác thực OTP dưới đây để hoàn tất kích hoạt gian hàng:",
            otpCode: otpCode,
            validityMinutes: 5,
            cancellationToken: cancellationToken);
    }

    private async Task<bool> SendOtpEmailInternalAsync(
        string toEmail,
        string fullName,
        string subject,
        string description,
        string otpCode,
        int validityMinutes,
        CancellationToken cancellationToken)
    {
        var greetingName = string.IsNullOrWhiteSpace(fullName) ? "Quý khách" : fullName.Trim();

        // Nếu chưa cấu hình thông tin SMTP (chạy local/dev hoặc kiểm thử chấm đồ án)
        if (string.IsNullOrWhiteSpace(_settings.SmtpUser) || string.IsNullOrWhiteSpace(_settings.SmtpPass))
        {
            if (!_environment.IsDevelopment()) return false;
            _logger.LogInformation(
                "[EMAIL-OTP DEV LOG] Chưa cấu hình SMTP. Gửi OTP mô phỏng tới: {Email}. Mã OTP: {OtpCode}",
                toEmail,
                otpCode);

            Console.WriteLine(
                $"\n======================================================================\n" +
                $"[MT-GRMS EMAIL-OTP DEV LOG]\n" +
                $"Tiêu đề: {subject}\n" +
                $"Người nhận: {greetingName} <{toEmail}>\n" +
                $"MÃ XÁC THỰC OTP: >>> {otpCode} <<<\n" +
                $"Thời hạn: {validityMinutes} phút (kể từ {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC)\n" +
                $"======================================================================\n");

            return false;
        }

        try
        {
            using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
            {
                EnableSsl = _settings.EnableSsl,
                Credentials = new NetworkCredential(_settings.SmtpUser, _settings.SmtpPass),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 5000
            };

            var senderEmail = string.IsNullOrWhiteSpace(_settings.SenderEmail)
                ? _settings.SmtpUser
                : _settings.SenderEmail;

            using var mail = new MailMessage
            {
                From = new MailAddress(senderEmail, _settings.SenderName),
                Subject = subject,
                IsBodyHtml = true,
                Body = BuildOtpHtmlBody(greetingName, description, otpCode, validityMinutes)
            };

            mail.To.Add(toEmail);

            using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            sendTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            await client.SendMailAsync(mail, sendTimeout.Token);
            _logger.LogInformation("Đã gửi email OTP thành công tới: {Email}.", toEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi gửi email OTP qua SMTP tới {Email}. Fallback in ra console.", toEmail);

            if (!_environment.IsDevelopment()) return false;

            Console.WriteLine(
                $"\n======================================================================\n" +
                $"[MT-GRMS EMAIL-OTP FALLBACK LOG]\n" +
                $"Tiêu đề: {subject}\n" +
                $"Người nhận: {greetingName} <{toEmail}>\n" +
                $"MÃ XÁC THỰC OTP: >>> {otpCode} <<<\n" +
                $"======================================================================\n");

            return false;
        }
    }

    private static string BuildOtpHtmlBody(string fullName, string description, string otpCode, int validityMinutes)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Xác thực OTP MT-GRMS</title>
</head>
<body style='margin:0;padding:24px;font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Helvetica,Arial,sans-serif;background-color:#f4f6f8;'>
    <div style='max-width:540px;margin:0 auto;background:#ffffff;border-radius:16px;overflow:hidden;box-shadow:0 4px 12px rgba(0,0,0,0.08);border:1px solid #e5e7eb;'>
        <div style='background:linear-gradient(135deg,#0066FF,#0052CC);padding:28px 24px;text-align:center;'>
            <h1 style='color:#ffffff;margin:0;font-size:22px;letter-spacing:0.5px;'>HỆ THỐNG QUẢN LÝ BÁN LẺ MT-GRMS</h1>
            <p style='color:#e0f2fe;margin:6px 0 0;font-size:13px;'>Multi-Tenant Grocery Retail Management System</p>
        </div>
        <div style='padding:32px 28px;color:#1f2937;line-height:1.6;'>
            <p style='font-size:15px;margin-top:0;'>Xin chào <strong>{WebUtility.HtmlEncode(fullName)}</strong>,</p>
            <p style='font-size:14px;color:#4b5563;'>{description}</p>
            <div style='margin:28px 0;text-align:center;'>
                <div style='display:inline-block;padding:14px 36px;background:#f0f6ff;border:2px dashed #0066FF;border-radius:12px;font-size:32px;font-weight:700;letter-spacing:8px;color:#0052CC;'>
                    {otpCode}
                </div>
                <p style='margin:10px 0 0;font-size:12px;color:#6b7280;'>Mã OTP có hiệu lực trong vòng <strong>{validityMinutes} phút</strong>.</p>
            </div>
            <p style='font-size:13px;color:#dc2626;background:#fef2f2;padding:12px;border-radius:8px;border-left:4px solid #ef4444;'>
                <strong>Lưu ý bảo mật:</strong> Tuyệt đối không chia sẻ mã OTP này cho bất kỳ ai, kể cả nhân viên hỗ trợ hệ thống.
            </p>
            <p style='font-size:13px;color:#6b7280;margin-bottom:0;'>Nếu bạn không thực hiện yêu cầu này, vui lòng bỏ qua email hoặc liên hệ quản trị viên để kiểm tra bảo mật.</p>
        </div>
        <div style='background:#f9fafb;padding:16px 24px;text-align:center;font-size:12px;color:#9ca3af;border-top:1px solid #f3f4f6;'>
            © {DateTime.UtcNow.Year} MT-GRMS Platform · Hệ thống Bán lẻ Đa chi nhánh
        </div>
    </div>
</body>
</html>";
    }
}
