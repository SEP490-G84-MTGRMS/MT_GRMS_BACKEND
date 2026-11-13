using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using MT_GRMS.Application.PurchaseOrders.Dispatch;

namespace MT_GRMS.Infrastructure.Services;

public class SmtpSupplierOrderEmailSender : ISupplierOrderEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpSupplierOrderEmailSender> _logger;

    public SmtpSupplierOrderEmailSender(IConfiguration configuration, ILogger<SmtpSupplierOrderEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendSupplierOrderAsync(SupplierOrderEmailMessage message, CancellationToken cancellationToken = default)
    {
        var host = _configuration["SMTP_HOST"] ?? _configuration["Smtp:Host"];
        var portStr = _configuration["SMTP_PORT"] ?? _configuration["Smtp:Port"] ?? "587";
        var user = _configuration["SMTP_USER"] ?? _configuration["Smtp:User"];
        var pass = _configuration["SMTP_PASSWORD"] ?? _configuration["Smtp:Password"];
        var from = _configuration["SMTP_FROM"] ?? _configuration["Smtp:From"] ?? user;
        var fromName = _configuration["SMTP_FROM_NAME"] ?? _configuration["Smtp:FromName"] ?? "THViet";
        var useSsl = bool.TryParse(_configuration["SMTP_USE_SSL"] ?? _configuration["Smtp:UseSsl"], out var ssl) && ssl;

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
        {
            // Dev fallback: log-only sender để không chặn luồng khi chưa cấu hình SMTP
            _logger.LogWarning(
                "SMTP chưa cấu hình — giả lập gửi email To={To}, Cc={CcCount}, Subject={Subject}, Attachment={File} ({Bytes} bytes)",
                message.To,
                message.Cc.Count,
                message.Subject,
                message.AttachmentFileName,
                message.AttachmentBytes.Length);
            return;
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(fromName, from));
        mime.To.Add(MailboxAddress.Parse(message.To));
        foreach (var cc in message.Cc)
        {
            mime.Cc.Add(MailboxAddress.Parse(cc));
        }

        mime.Subject = message.Subject.Replace("\r", string.Empty).Replace("\n", string.Empty);

        var builder = new BodyBuilder { TextBody = message.BodyText };
        if (message.AttachmentBytes.Length > 0)
        {
            builder.Attachments.Add(
                message.AttachmentFileName,
                message.AttachmentBytes,
                new ContentType("application", "vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
        }

        mime.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        var port = int.Parse(portStr);
        // Gmail 587 requires STARTTLS; 465 uses SSL-on-connect.
        var secure = useSsl || port == 465
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

        await client.ConnectAsync(host, port, secure, cancellationToken);
        if (!string.IsNullOrWhiteSpace(user))
        {
            await client.AuthenticateAsync(user, pass, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);

        _logger.LogInformation("Đã gửi email đơn hàng NCC tới {To} (Cc={Cc}).", message.To, message.Cc.Count);
    }
}
