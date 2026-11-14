namespace MT_GRMS.Infrastructure.Services;

public class EmailSettings
{
    public const string SectionName = "EmailSettings";

    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public string SenderEmail { get; set; } = "noreply.thviet@gmail.com";
    public string SenderName { get; set; } = "THViet - Quản Lý Tạp Hóa Việt";
    public string SmtpUser { get; set; } = string.Empty;
    public string SmtpPass { get; set; } = string.Empty;
    public bool EnableSsl { get; set; } = true;
}

