using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MT_GRMS.Application.Common.DTOs;
using MT_GRMS.Application.Common.Interfaces;

namespace MT_GRMS.Infrastructure.Services;

public class CloudinaryService : ICloudinaryService
{
    private readonly Cloudinary? _cloudinary;
    private readonly CloudinarySettings _settings;
    private readonly ILogger<CloudinaryService> _logger;

    public CloudinaryService(
        IOptions<CloudinarySettings> options,
        ILogger<CloudinaryService> logger)
    {
        _settings = options.Value;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_settings.CloudName) &&
            !string.IsNullOrWhiteSpace(_settings.ApiKey) &&
            !string.IsNullOrWhiteSpace(_settings.ApiSecret))
        {
            var account = new Account(
                _settings.CloudName,
                _settings.ApiKey,
                _settings.ApiSecret);

            _cloudinary = new Cloudinary(account);
            _cloudinary.Api.Secure = true;
        }
        else
        {
            _logger.LogWarning("Cloudinary credentials are not configured or incomplete in appsettings.");
        }
    }

    public async Task<ImageUploadResponseDto> UploadImageAsync(
        Stream fileStream,
        string fileName,
        string? folder = null,
        CancellationToken cancellationToken = default)
    {
        if (_cloudinary == null)
        {
            throw new InvalidOperationException("Dịch vụ lưu trữ Cloudinary chưa được cấu hình (thiếu CloudName, ApiKey hoặc ApiSecret). Vui lòng liên hệ quản trị viên.");
        }

        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(fileName, fileStream),
            Folder = string.IsNullOrWhiteSpace(folder) ? "mtgrms/products" : folder,
            UseFilename = true,
            UniqueFilename = true,
            Overwrite = false
        };

        var uploadResult = await _cloudinary.UploadAsync(uploadParams, cancellationToken);

        if (uploadResult.Error != null)
        {
            _logger.LogError("Lỗi tải ảnh lên Cloudinary: {ErrorMessage}", uploadResult.Error.Message);
            throw new InvalidOperationException($"Tải ảnh lên Cloudinary thất bại: {uploadResult.Error.Message}");
        }

        return new ImageUploadResponseDto
        {
            Url = uploadResult.SecureUrl?.ToString() ?? uploadResult.Url?.ToString() ?? string.Empty,
            PublicId = uploadResult.PublicId ?? string.Empty,
            Format = uploadResult.Format ?? string.Empty,
            Bytes = uploadResult.Bytes,
            Width = uploadResult.Width,
            Height = uploadResult.Height
        };
    }

    public async Task<bool> DeleteImageAsync(
        string publicId,
        CancellationToken cancellationToken = default)
    {
        if (_cloudinary == null || string.IsNullOrWhiteSpace(publicId))
        {
            return false;
        }

        var deletionParams = new DeletionParams(publicId);
        var result = await _cloudinary.DestroyAsync(deletionParams);

        return result.Result?.Equals("ok", StringComparison.OrdinalIgnoreCase) == true;
    }
}

