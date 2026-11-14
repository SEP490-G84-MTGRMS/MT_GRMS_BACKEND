using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
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
            subject: "[THViet] Mã xác thực OTP khôi phục mật khẩu tài khoản",
            description: "Hệ thống nhận được yêu cầu khôi phục mật khẩu cho tài khoản liên kết với địa chỉ email này trên nền tảng THViet. Vui lòng sử dụng mã xác thực OTP dưới đây để hoàn tất:",
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
            subject: $"[THViet] Mã OTP đăng nhập 2 lớp: {otpCode}",
            description: "Hệ thống vừa ghi nhận yêu cầu đăng nhập vào tài khoản quản lý của bạn trên nền tảng THViet. Vui lòng nhập mã xác thực 2 bước (2FA) dưới đây để truy cập hệ thống:",
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
        var storeDisplay = string.IsNullOrWhiteSpace(storeName) ? "THViet" : storeName.Trim();
        return SendOtpEmailInternalAsync(
            toEmail: toEmail,
            fullName: fullName,
            subject: $"[THViet] Mã OTP kích hoạt cửa hàng {storeDisplay}: {otpCode}",
            description: $"Chào mừng bạn đăng ký mở cửa hàng <strong>{WebUtility.HtmlEncode(storeDisplay)}</strong> trên nền tảng THViet. Vui lòng nhập mã xác thực OTP dưới đây để hoàn tất kích hoạt gian hàng:",
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
                    $"[THViet EMAIL-OTP DEV LOG]\n" +
                    $"Tiêu đề: {subject}\n" +
                    $"Người nhận: {greetingName} <{toEmail}>\n" +
                    $"MÃ XÁC THỰC OTP: >>> {otpCode} <<<\n" +
                    $"Thời hạn: {validityMinutes} phút (kể từ {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC)\n" +
                    $"======================================================================\n");
            }

            return Task.FromResult(false);
        }

        var host = _settings.SmtpHost?.Trim().Trim('"', '\'') ?? "mail.oxmail.vn";
        var port = _settings.SmtpPort;
        var ssl = _settings.EnableSsl;
        var user = _settings.SmtpUser?.Trim().Trim('"', '\'') ?? string.Empty;
        var pass = _settings.SmtpPass?.Trim().Trim('"', '\'') ?? string.Empty;
        var senderEmail = (string.IsNullOrWhiteSpace(_settings.SenderEmail)
            ? user
            : _settings.SenderEmail)?.Trim().Trim('"', '\'') ?? user;
        var senderName = _settings.SenderName?.Trim() ?? "THViet";
        var htmlBody = BuildOtpHtmlBody(greetingName, description, otpCode, validityMinutes);

        // Gửi email OTP qua SMTP ở chế độ chạy nền (non-blocking background task)
        // API phản hồi tức thì (<15ms) để giao diện hiển thị ngay popup OTP cho người dùng mà không bị delay mạng SMTP
        _ = Task.Run(async () =>
        {
            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(senderName, senderEmail));
                message.To.Add(MailboxAddress.Parse(toEmail));
                message.Subject = subject.Replace("\r", string.Empty).Replace("\n", string.Empty);
                var builder = new BodyBuilder { HtmlBody = htmlBody };
                message.Body = builder.ToMessageBody();

                using var client = new SmtpClient();
                var secure = port == 465
                    ? SecureSocketOptions.SslOnConnect
                    : (ssl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await client.ConnectAsync(host, port, secure, cts.Token);
                if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
                {
                    _logger.LogInformation("[EmailService Debug] User: '{User}', PassLength: {Len}, PassFirst: '{First}', PassLast: '{Last}'", user, pass.Length, pass[0], pass[^1]);
                    await client.AuthenticateAsync(user, pass, cts.Token);
                }
                await client.SendAsync(message, cts.Token);
                await client.DisconnectAsync(true, cts.Token);
                _logger.LogInformation("[Background SMTP] Đã gửi email OTP thành công tới: {Email}.", toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Background SMTP] Lỗi khi gửi email OTP qua SMTP tới {Email}. Fallback in ra console.", toEmail);

                if (_environment.IsDevelopment())
                {
                    Console.WriteLine(
                        $"\n======================================================================\n" +
                        $"[THViet EMAIL-OTP FALLBACK LOG]\n" +
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
                $"[THViet EMAIL-OTP DEV LOG (Đang gửi nền qua SMTP)]\n" +
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
    <title>Xác thực OTP THViet</title>
</head>
<body style='margin:0;padding:40px 24px;background-color:#ffffff;font-family:-apple-system,BlinkMacSystemFont,\'Segoe UI\',Roboto,Helvetica,Arial,sans-serif;color:#111827;line-height:1.6;'>
    <div style='max-width:540px;margin:0 auto;text-align:left;'>
        <!-- Brand Header (THViet - Quản Lý Tạp Hóa Việt) -->
        <div style='margin-bottom:32px;'>
            <span style='font-size:30px;font-weight:900;letter-spacing:-0.5px;color:#0F766E;'>TH</span><span style='font-size:30px;font-weight:900;color:#14B8A6;'>Viet</span>
            <div style='font-size:12px;font-weight:700;color:#0F172A;letter-spacing:2px;margin-top:2px;text-transform:uppercase;'>QUẢN LÝ TẠP HÓA VIỆT</div>
        </div>

        <!-- Lời chào & Nội dung -->
        <p style='font-size:16px;margin:0 0 16px 0;color:#111827;'>Hi <strong>{WebUtility.HtmlEncode(fullName)}</strong>,</p>
        <p style='font-size:14px;margin:0 0 24px 0;color:#374151;'>{description}</p>

        <!-- Khối mã OTP nổi bật cỡ lớn -->
        <div style='margin:28px 0 14px 0;'>
            <div style='display:inline-block;background-color:#0F766E;color:#ffffff;font-size:38px;font-weight:800;letter-spacing:10px;padding:14px 36px;border-radius:8px;font-family:-apple-system,BlinkMacSystemFont,\'Segoe UI\',Roboto,Consolas,monospace;box-shadow:0 4px 14px rgba(15,118,110,0.3);'>
                {otpCode}
            </div>
        </div>
        <p style='font-size:14px;color:#6b7280;margin:0 0 28px 0;'>
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
            <p style='margin:0;'>© {DateTime.UtcNow.Year} THViet Corporation. All rights reserved.</p>
            <p style='margin:4px 0 0 0;color:#94a3b8;font-size:11px;'>Hệ thống Quản lý Tạp hóa Việt - THViet</p>
        </div>
    </div>
</body>
</html>";
    }
}
