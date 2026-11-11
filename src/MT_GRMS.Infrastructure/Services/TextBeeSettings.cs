namespace MT_GRMS.Infrastructure.Services;

public class TextBeeSettings
{
    public const string SectionName = "TextBeeSettings";

    public string ApiKey { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
}
