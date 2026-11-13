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

    private Task<bool> SendOtpEmailInternalAsync(
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
            _logger.LogInformation(
                "[EMAIL-OTP DEV LOG] Chưa cấu hình SMTP. Gửi OTP mô phỏng tới: {Email}. Mã OTP: {OtpCode}",
                toEmail,
                otpCode);

            if (_environment.IsDevelopment())
            {
                Console.WriteLine(
                    $"\n======================================================================\n" +
                    $"[MT-GRMS EMAIL-OTP DEV LOG]\n" +
                    $"Tiêu đề: {subject}\n" +
                    $"Người nhận: {greetingName} <{toEmail}>\n" +
                    $"MÃ XÁC THỰC OTP: >>> {otpCode} <<<\n" +
                    $"Thời hạn: {validityMinutes} phút (kể từ {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC)\n" +
                    $"======================================================================\n");
            }

            return Task.FromResult(false);
        }

        var senderEmail = string.IsNullOrWhiteSpace(_settings.SenderEmail)
            ? _settings.SmtpUser
            : _settings.SenderEmail;
        var senderName = _settings.SenderName;
        var host = _settings.SmtpHost;
        var port = _settings.SmtpPort;
        var ssl = _settings.EnableSsl;
        var user = _settings.SmtpUser;
        var pass = _settings.SmtpPass;
        var htmlBody = BuildOtpHtmlBody(greetingName, description, otpCode, validityMinutes);

        // Gửi email OTP qua SMTP ở chế độ chạy nền (non-blocking background task)
        // API phản hồi tức thì (<15ms) để giao diện hiển thị ngay popup OTP cho người dùng mà không bị delay mạng SMTP
        _ = Task.Run(async () =>
        {
            try
            {
                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = ssl,
                    Credentials = new NetworkCredential(user, pass),
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 15000
                };

                using var mail = new MailMessage
                {
                    From = new MailAddress(senderEmail, senderName),
                    Subject = subject,
                    IsBodyHtml = true,
                    Body = htmlBody
                };
                mail.To.Add(toEmail);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await client.SendMailAsync(mail, cts.Token);
                _logger.LogInformation("[Background SMTP] Đã gửi email OTP thành công tới: {Email}.", toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Background SMTP] Lỗi khi gửi email OTP qua SMTP tới {Email}. Fallback in ra console.", toEmail);

                if (_environment.IsDevelopment())
                {
                    Console.WriteLine(
                        $"\n======================================================================\n" +
                        $"[MT-GRMS EMAIL-OTP FALLBACK LOG]\n" +
                        $"Tiêu đề: {subject}\n" +
                        $"Người nhận: {greetingName} <{toEmail}>\n" +
                        $"MÃ XÁC THỰC OTP: >>> {otpCode} <<<\n" +
                        $"======================================================================\n");
                }
            }
        });

        if (_environment.IsDevelopment())
        {
            Console.WriteLine(
                $"\n======================================================================\n" +
                $"[MT-GRMS EMAIL-OTP DEV LOG (Đang gửi nền qua SMTP)]\n" +
                $"Tiêu đề: {subject}\n" +
                $"Người nhận: {greetingName} <{toEmail}>\n" +
                $"MÃ XÁC THỰC OTP: >>> {otpCode} <<<\n" +
                $"Thời hạn: {validityMinutes} phút (kể từ {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC)\n" +
                $"======================================================================\n");
        }

        return Task.FromResult(true);
    }

    private static string BuildOtpHtmlBody(string fullName, string description, string otpCode, int validityMinutes)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Xác thực OTP MT-GRMS</title>
</head>
<body style='margin:0;padding:40px 24px;background-color:#ffffff;font-family:-apple-system,BlinkMacSystemFont,\'Segoe UI\',Roboto,Helvetica,Arial,sans-serif;color:#111827;line-height:1.6;'>
    <div style='max-width:540px;margin:0 auto;text-align:left;'>
        <!-- Brand Header (Phong cách tối giản giống NVIDIA) -->
        <div style='margin-bottom:32px;'>
            <span style='font-size:22px;font-weight:800;letter-spacing:-0.5px;color:#0f172a;'>MT-GRMS</span>
            <span style='font-size:22px;font-weight:800;color:#0f766e;'>.</span>
        </div>

        <!-- Lời chào & Nội dung -->
        <p style='font-size:16px;margin:0 0 16px 0;color:#111827;'>Hi <strong>{WebUtility.HtmlEncode(fullName)}</strong>,</p>
        <p style='font-size:14px;margin:0 0 24px 0;color:#374151;'>{description}</p>

        <!-- Khối mã OTP nổi bật tối giản -->
        <div style='margin:28px 0 12px 0;'>
            <div style='display:inline-block;background-color:#0f766e;color:#ffffff;font-size:28px;font-weight:700;letter-spacing:6px;padding:12px 28px;border-radius:6px;font-family:-apple-system,BlinkMacSystemFont,\'Segoe UI\',Roboto,Consolas,monospace;'>
                {otpCode}
            </div>
        </div>
        <p style='font-size:13px;color:#6b7280;margin:0 0 28px 0;'>
            Mã OTP có hiệu lực trong vòng <strong>{validityMinutes} phút</strong>.
        </p>

        <!-- Lưu ý bảo mật & quyền riêng tư -->
        <div style='margin-top:32px;padding-top:20px;border-top:1px solid #f3f4f6;font-size:13px;color:#6b7280;line-height:1.6;'>
            <p style='margin:0 0 8px 0;'>Để bảo mật tài khoản, tuyệt đối không chia sẻ mã này cho bất kỳ ai.</p>
            <p style='margin:0;'>Nếu bạn không thực hiện yêu cầu này, vui lòng bỏ qua email và mật khẩu tài khoản của bạn sẽ không bị thay đổi.</p>
        </div>

        <!-- Chân trang tối giản -->
        <div style='margin-top:40px;font-size:12px;color:#9ca3af;'>
            <p style='margin:0 0 6px 0;'>
                <a href='#' style='color:#6b7280;text-decoration:underline;'>Chính sách bảo mật</a> &nbsp;|&nbsp; 
                <a href='#' style='color:#6b7280;text-decoration:underline;'>Hỗ trợ</a> &nbsp;|&nbsp; 
                <a href='#' style='color:#6b7280;text-decoration:underline;'>Điều khoản dịch vụ</a>
            </p>
            <p style='margin:0;'>© {DateTime.UtcNow.Year} MT-GRMS Corporation. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";
    }
}
