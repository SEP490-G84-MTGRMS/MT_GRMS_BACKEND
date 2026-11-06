using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MT_GRMS.Application.Common.Interfaces;

namespace MT_GRMS.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<EmailSettings> options, ILogger<EmailService> logger)
    {
        _settings = options.Value;
        _logger = logger;
    }

    public async Task SendPasswordResetOtpAsync(
        string toEmail,
        string fullName,
        string otpCode,
        CancellationToken cancellationToken = default)
    {
        var greetingName = string.IsNullOrWhiteSpace(fullName) ? "Quý khách" : fullName;

        // Nếu chưa cấu hình thông tin SMTP (chạy local/dev hoặc kiểm thử chấm đồ án)
        if (string.IsNullOrWhiteSpace(_settings.SmtpUser) || string.IsNullOrWhiteSpace(_settings.SmtpPass))
        {
            _logger.LogInformation(
                "[EMAIL-OTP DEV LOG] Chưa cấu hình SMTP. Gửi OTP mô phỏng tới: {Email}. Mã OTP: {OtpCode}",
                toEmail,
                otpCode);

            Console.WriteLine(
                $"\n======================================================================\n" +
                $"[MT-GRMS EMAIL-OTP DEV LOG]\n" +
                $"Người nhận: {greetingName} <{toEmail}>\n" +
                $"MÃ XÁC THỰC OTP: >>> {otpCode} <<<\n" +
                $"Thời hạn: 10 phút (kể từ {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC)\n" +
                $"======================================================================\n");

            return;
        }

        try
        {
            using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
            {
                EnableSsl = _settings.EnableSsl,
                Credentials = new NetworkCredential(_settings.SmtpUser, _settings.SmtpPass),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 15000
            };

            var mail = new MailMessage
            {
                From = new MailAddress(_settings.SenderEmail, _settings.SenderName),
                Subject = "[MT-GRMS] Mã xác thực OTP khôi phục mật khẩu tài khoản",
                IsBodyHtml = true,
                Body = BuildOtpHtmlBody(greetingName, otpCode)
            };

            mail.To.Add(toEmail);

            await client.SendMailAsync(mail, cancellationToken);
            _logger.LogInformation("Đã gửi email OTP khôi phục mật khẩu thành công tới: {Email}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi gửi email OTP qua SMTP tới {Email}. Fallback in ra console.", toEmail);

            // Fallback console log nếu gửi mail thất bại để dev/tester không bị chặn
            Console.WriteLine(
                $"\n======================================================================\n" +
                $"[MT-GRMS EMAIL-OTP FALLBACK LOG]\n" +
                $"Người nhận: {greetingName} <{toEmail}>\n" +
                $"MÃ XÁC THỰC OTP: >>> {otpCode} <<<\n" +
                $"======================================================================\n");
        }
    }

    private static string BuildOtpHtmlBody(string fullName, string otpCode)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Khôi phục mật khẩu MT-GRMS</title>
</head>
<body style='margin:0;padding:24px;font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Helvetica,Arial,sans-serif;background-color:#f4f6f8;'>
    <div style='max-width:540px;margin:0 auto;background:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 4px 12px rgba(0,0,0,0.08);border:1px solid #e5e7eb;'>
        <div style='background:linear-gradient(135deg,#00705a,#0f9d77);padding:28px 24px;text-align:center;'>
            <h1 style='color:#ffffff;margin:0;font-size:22px;letter-spacing:0.5px;'>HỆ THỐNG QUẢN LÝ SIÊU THỊ MT-GRMS</h1>
            <p style='color:#e6fffa;margin:6px 0 0;font-size:13px;'>Multi-Tenant Grocery Retail Management System</p>
        </div>
        <div style='padding:32px 28px;color:#1f2937;line-height:1.6;'>
            <p style='font-size:15px;margin-top:0;'>Xin chào <strong>{fullName}</strong>,</p>
            <p style='font-size:14px;color:#4b5563;'>Hệ thống nhận được yêu cầu khôi phục mật khẩu cho tài khoản liên kết với địa chỉ email này. Vui lòng sử dụng mã xác thực OTP dưới đây để hoàn tất:</p>
            <div style='margin:28px 0;text-align:center;'>
                <div style='display:inline-block;padding:14px 36px;background:#f0fdf4;border:2px dashed #059669;border-radius:8px;font-size:32px;font-weight:700;letter-spacing:8px;color:#047857;'>
                    {otpCode}
                </div>
                <p style='margin:10px 0 0;font-size:12px;color:#6b7280;'>Mã OTP có hiệu lực trong vòng <strong>10 phút</strong>.</p>
            </div>
            <p style='font-size:13px;color:#dc2626;background:#fef2f2;padding:12px;border-radius:6px;border-left:4px solid #ef4444;'>
                ⚠️ <strong>Lưu ý bảo mật:</strong> Tuyệt đối không cung cấp mã OTP này cho bất kỳ ai, kể cả nhân viên hỗ trợ hệ thống.
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

