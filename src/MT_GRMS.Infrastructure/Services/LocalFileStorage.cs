using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MT_GRMS.Application.Common.Interfaces;

namespace MT_GRMS.Infrastructure.Services;

public class LocalFileStorage : IFileStorage
{
    private readonly string _root;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(IConfiguration configuration, ILogger<LocalFileStorage> logger)
    {
        _logger = logger;
        var provider = configuration["SUPPLIER_ORDER_STORAGE_PROVIDER"]
            ?? configuration["SupplierOrderStorage:Provider"]
            ?? "LOCAL";
        if (!string.Equals(provider, "LOCAL", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Storage provider '{Provider}' chưa hỗ trợ — fallback LOCAL.", provider);
        }

        _root = configuration["SUPPLIER_ORDER_STORAGE_PATH"]
            ?? configuration["SupplierOrderStorage:Path"]
            ?? Path.Combine(AppContext.BaseDirectory, "storage", "supplier-orders");

        Directory.CreateDirectory(_root);
    }

    public async Task<StoredFileResult> SaveAsync(
        string storageKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveSafePath(storageKey);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Create (overwrite) so retries after a mid-write failure can reuse the same storage key.
        await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var sha = SHA256.Create();
        // Dispose CryptoStream before reading Hash so FlushFinalBlock runs.
        await using (var crypto = new CryptoStream(fs, sha, CryptoStreamMode.Write, leaveOpen: true))
        {
            await content.CopyToAsync(crypto, cancellationToken);
            await crypto.FlushAsync(cancellationToken);
        }

        await fs.FlushAsync(cancellationToken);

        if (sha.Hash is null)
        {
            throw new InvalidOperationException("SHA256 hash was not finalized after writing file.");
        }

        var hash = Convert.ToHexString(sha.Hash).ToLowerInvariant();
        var size = new FileInfo(fullPath).Length;
        return new StoredFileResult(storageKey, size, hash);
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveSafePath(storageKey);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Không tìm thấy file.", storageKey);
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveSafePath(storageKey);
        return Task.FromResult(File.Exists(fullPath));
    }

    private string ResolveSafePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException("Storage key rỗng.");
        }

        var normalized = storageKey.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(storageKey))
        {
            throw new InvalidOperationException("Storage key không hợp lệ (path traversal).");
        }

        var full = Path.GetFullPath(Path.Combine(_root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var rootFull = Path.GetFullPath(_root);
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage key nằm ngoài thư mục gốc.");
        }

        return full;
    }
}
