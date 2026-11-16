using MT_GRMS.Application.Common.Interfaces;

namespace MT_GRMS.Application.Common;

/// <summary>
/// Helper chuyển đổi ảnh dạng data URL (base64) sang URL lưu trữ trên Cloudinary khi lưu nghiệp vụ.
/// </summary>
public static class CloudinaryDataUrlHelper
{
    public static bool IsDataImageUrl(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.TrimStart().StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<string?> ResolveImageUrlAsync(
        ICloudinaryService? cloudinaryService,
        string? rawUrl,
        string folder,
        string filePrefix,
        CancellationToken cancellationToken = default,
        IDictionary<string, string>? uploadedCache = null)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return rawUrl;
        }

        var trimmed = rawUrl.Trim();
        if (!IsDataImageUrl(trimmed) || cloudinaryService == null)
        {
            return trimmed;
        }

        if (uploadedCache != null && uploadedCache.TryGetValue(trimmed, out var cachedUrl))
        {
            return cachedUrl;
        }

        var commaIndex = trimmed.IndexOf(',');
        if (commaIndex <= 0 || commaIndex >= trimmed.Length - 1)
        {
            throw new ArgumentException("Dữ liệu ảnh base64 không hợp lệ.");
        }

        var header = trimmed[..commaIndex];
        var base64Data = trimmed[(commaIndex + 1)..];
        var extension = header.Contains("image/png", StringComparison.OrdinalIgnoreCase)
            ? ".png"
            : header.Contains("image/webp", StringComparison.OrdinalIgnoreCase)
                ? ".webp"
                : ".jpg";

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64Data);
        }
        catch (FormatException)
        {
            throw new ArgumentException("Dữ liệu ảnh base64 không hợp lệ.");
        }

        await using var stream = new MemoryStream(bytes);
        var fileName = $"{filePrefix}-{Guid.NewGuid():N}{extension}";
        var uploadResult = await cloudinaryService.UploadImageAsync(stream, fileName, folder, cancellationToken);

        if (uploadedCache != null)
        {
            uploadedCache[trimmed] = uploadResult.Url;
        }

        return uploadResult.Url;
    }

    public static async Task<List<string>> ResolveImageUrlsAsync(
        ICloudinaryService? cloudinaryService,
        IEnumerable<string>? rawUrls,
        string folder,
        string filePrefix,
        CancellationToken cancellationToken = default,
        IDictionary<string, string>? uploadedCache = null)
    {
        if (rawUrls == null)
        {
            return new List<string>();
        }

        var cache = uploadedCache ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var resolved = new List<string>();
        var index = 0;

        foreach (var raw in rawUrls)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            index++;
            var url = await ResolveImageUrlAsync(
                cloudinaryService,
                raw,
                folder,
                $"{filePrefix}-{index}",
                cancellationToken,
                cache);

            if (!string.IsNullOrWhiteSpace(url))
            {
                resolved.Add(url);
            }
        }

        return resolved;
    }
}
