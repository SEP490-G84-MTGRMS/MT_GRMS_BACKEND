namespace MT_GRMS.Application.Common.Interfaces;

public interface IFileStorage
{
    /// <summary>
    /// Lưu file; trả về storage key (không phải absolute path).
    /// Không ghi đè nếu key đã tồn tại — ném exception.
    /// </summary>
    Task<StoredFileResult> SaveAsync(
        string storageKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default);
}

public sealed record StoredFileResult(
    string StorageKey,
    long SizeBytes,
    string Sha256Hex);
