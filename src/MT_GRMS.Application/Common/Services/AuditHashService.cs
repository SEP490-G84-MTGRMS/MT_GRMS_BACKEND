using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Application.Common.Services;

public class AuditHashService : IAuditHashService
{
    private readonly byte[] _secretKey;

    public AuditHashService(IConfiguration configuration)
    {
        var secret = configuration["AuditHash:SecretKey"] ?? "MT_GRMS_FALLBACK_DEFAULT_AUDIT_HASH_SECRET_KEY_2026";
        _secretKey = Encoding.UTF8.GetBytes(secret);
    }

    public string ComputeEntryHash(string prevHash, CashbookEntry entry)
    {
        // Canonical string format ensuring field ordering and fixed culture
        var canonical = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0}|{1}|{2}|{3}|{4}|{5:F2}|{6:F2}|{7}|{8}|{9}|{10:O}",
            prevHash,
            entry.TenantId,
            entry.AccountId,
            (int)entry.AccountType,
            (int)entry.EntryType,
            entry.Amount,
            entry.BalanceAfter,
            entry.SourceType,
            entry.SourceId?.ToString() ?? string.Empty,
            entry.ReversedEntryId?.ToString() ?? string.Empty,
            entry.OccurredAtUtc
        );

        using var hmac = new HMACSHA256(_secretKey);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public bool VerifyEntryHash(string prevHash, CashbookEntry entry)
    {
        var expectedHash = ComputeEntryHash(prevHash, entry);
        return string.Equals(expectedHash, entry.AuditHash, StringComparison.OrdinalIgnoreCase);
    }
}