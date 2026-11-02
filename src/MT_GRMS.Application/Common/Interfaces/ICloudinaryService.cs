using MT_GRMS.Application.Common.DTOs;

namespace MT_GRMS.Application.Common.Interfaces;

public interface ICloudinaryService
{
    Task<ImageUploadResponseDto> UploadImageAsync(
        Stream fileStream,
        string fileName,
        string? folder = null,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteImageAsync(
        string publicId,
        CancellationToken cancellationToken = default);
}

