using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.DTOs;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Api.Controllers;

public class ImageUploadRequest
{
    public IFormFile? File { get; set; }
    public string? Folder { get; set; }
}

[ApiController]
[Route("api/media")]
[Authorize]
public class MediaController : BaseApiController
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    private readonly ICloudinaryService _cloudinaryService;
    private readonly ILogger<MediaController> _logger;
    private readonly AppDbContext _context;

    public MediaController(
        ICloudinaryService cloudinaryService,
        ILogger<MediaController> logger,
        AppDbContext context)
    {
        _cloudinaryService = cloudinaryService;
        _logger = logger;
        _context = context;
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
            var tenantId = User.FindFirst("tenant_id")?.Value;
            if (string.IsNullOrWhiteSpace(tenantId)) return Unauthorized();
            await using var transaction = _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync(cancellationToken)
                : null;
            var tenant = _context.Database.IsRelational()
                ? await _context.Tenants.FromSqlInterpolated($"SELECT * FROM \"Tenants\" WHERE \"Id\" = {tenantId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
                : await _context.Tenants.SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
            if (tenant == null) return NotFound();
            var extraGb = tenant.StorageAddonExpiresAt > DateTime.UtcNow ? tenant.ExtraStorageGb : 0;
            var limit = (2L + extraGb) * 1024 * 1024 * 1024;
            if (tenant.StorageUsedBytes + file.Length > limit)
                return StatusCode(413, ApiResponse<object>.Fail("STORAGE_QUOTA_EXCEEDED", "Dung lượng đã đạt hạn mức. Vui lòng mua thêm GB hoặc xóa tệp cũ."));
            await using var stream = file.OpenReadStream();
            var result = await _cloudinaryService.UploadImageAsync(
                stream,
                file.FileName,
                $"mtgrms/tenants/{tenant.Id}",
                cancellationToken);
            try
            {
                if (tenant.StorageUsedBytes + result.Bytes > limit) throw new InvalidOperationException("Dung lượng ảnh sau tải lên vượt hạn mức.");
                tenant.StorageUsedBytes += result.Bytes;
                _context.TenantMedia.Add(new TenantMedia { TenantId = tenant.Id, PublicId = result.PublicId, Url = result.Url, Bytes = result.Bytes });
                await _context.SaveChangesAsync(cancellationToken);
                if (transaction != null) await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await _cloudinaryService.DeleteImageAsync(result.PublicId, cancellationToken);
                throw;
            }
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

    [HttpDelete("{mediaId}")]
    public async Task<IActionResult> Delete(string mediaId, CancellationToken cancellationToken)
    {
        var tenantId = User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrWhiteSpace(tenantId)) return Unauthorized();
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var tenant = await _context.Tenants.FromSqlInterpolated($"SELECT * FROM \"Tenants\" WHERE \"Id\" = {tenantId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
        var media = await _context.TenantMedia.SingleOrDefaultAsync(m => m.Id == mediaId && m.TenantId == tenantId, cancellationToken);
        if (tenant == null || media == null) return NotFound();
        if (!await _cloudinaryService.DeleteImageAsync(media.PublicId, cancellationToken)) return StatusCode(503);
        tenant.StorageUsedBytes = Math.Max(0, tenant.StorageUsedBytes - media.Bytes);
        _context.TenantMedia.Remove(media);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [NonAction]
    public Task<IActionResult> UploadImage(ImageUploadRequest request, CancellationToken cancellationToken)
    {
        return UploadImage(request?.File, request?.Folder, cancellationToken);
    }
}

