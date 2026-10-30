namespace MT_GRMS.Application.Common.DTOs;

public class ImageUploadResponseDto
{
    public string Url { get; set; } = string.Empty;
    public string PublicId { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

