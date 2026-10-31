using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.DTOs;
using MT_GRMS.Application.Common.Interfaces;

namespace MT_GRMS.Api.Controllers;

public class ImageUploadRequest
{
    public IFormFile? File { get; set; }
    public string? Folder { get; set; }
}

[ApiController]
[Route("api/v1/media")]
public class MediaController : BaseApiController
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    private readonly ICloudinaryService _cloudinaryService;
    private readonly ILogger<MediaController> _logger;

    public MediaController(
        ICloudinaryService cloudinaryService,
        ILogger<MediaController> logger)
    {
        _cloudinaryService = cloudinaryService;
        _logger = logger;
    }

    /// <summary>
    /// Tải tệp hình ảnh lên Cloudinary qua luồng backend an toàn
    /// </summary>
    /// <param name="file">Tệp hình ảnh cần tải lên (.jpg, .jpeg, .png, .webp, tối đa 5MB)</param>
    /// <param name="folder">Thư mục lưu trữ đích trên Cloudinary (tùy chọn)</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadImage(
        IFormFile? file,
        [FromForm] string? folder,
        CancellationToken cancellationToken)
    {
        // 1. Kiểm tra sự tồn tại của file
        if (file == null || file.Length == 0)
        {
            return BadRequest(ApiResponse<object>.Fail(
                "INVALID_FILE",
                "File rỗng hoặc không được cung cấp."));
        }

        // 2. Kiểm tra kích thước file (tối đa 5MB)
        if (file.Length > MaxFileSizeBytes)
        {
            return BadRequest(ApiResponse<object>.Fail(
                "FILE_TOO_LARGE",
                "Kích thước ảnh vượt quá giới hạn tối đa 5MB."));
        }

        // 3. Kiểm tra định dạng phần mở rộng file
        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            return BadRequest(ApiResponse<object>.Fail(
                "INVALID_FORMAT",
                "Định dạng tệp không được hỗ trợ. Chỉ chấp nhận các định dạng: .jpg, .jpeg, .png, .webp."));
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _cloudinaryService.UploadImageAsync(
                stream,
                file.FileName,
                folder,
                cancellationToken);

            return Ok(ApiResponse<ImageUploadResponseDto>.Ok(result, "Tải ảnh lên Cloudinary thành công."));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Lỗi nghiệp vụ khi tải ảnh lên Cloudinary");
            return StatusCode(500, ApiResponse<object>.Fail("UPLOAD_FAILED", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không xác định khi tải ảnh lên Cloudinary");
            return StatusCode(500, ApiResponse<object>.Fail("SERVER_ERROR", "Đã xảy ra lỗi khi xử lý tải ảnh lên máy chủ."));
        }
    }

    [NonAction]
    public Task<IActionResult> UploadImage(ImageUploadRequest request, CancellationToken cancellationToken)
    {
        return UploadImage(request?.File, request?.Folder, cancellationToken);
    }
}
