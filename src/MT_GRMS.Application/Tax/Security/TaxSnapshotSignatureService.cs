using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Application.Tax.Security;

/// <summary>
/// Service implementation for HMAC-SHA256 snapshot integrity seal (Dấu toàn vẹn) using Canonical JSON.
/// Binds both core financial figures and cryptographic SHA-256 digest of BookDataJson.
/// Supports Key Rotation with KeyId to ensure historic snapshots remain verifiable when keys are rotated.
/// </summary>
public class TaxSnapshotSignatureService : ITaxSnapshotSignatureService
{
    private const string DefaultKeyId = "k-2026-v1";
    private readonly Dictionary<string, byte[]> _keyStore = new();

    public TaxSnapshotSignatureService(IConfiguration? configuration = null)
    {
        var primarySecret = Environment.GetEnvironmentVariable("TAX_AUDIT_SECRET_KEY")
            ?? configuration?["TaxAudit:SecretKey"]
            ?? "MT_GRMS_DEFAULT_TAX_SNAPSHOT_AUDIT_HMAC_SECRET_KEY_2026";

        _keyStore[DefaultKeyId] = Encoding.UTF8.GetBytes(primarySecret);

        // Load additional historic keys for rotation if present
        var v2 = Environment.GetEnvironmentVariable("TAX_AUDIT_SECRET_KEY_V2");
        if (!string.IsNullOrWhiteSpace(v2))
        {
            _keyStore["k-2027-v2"] = Encoding.UTF8.GetBytes(v2);
        }
    }

    public string ComputeSignature(HouseholdTaxSnapshot snapshot)
    {
        var keyId = DefaultKeyId;
        var canonicalJson = BuildCanonicalJson(snapshot);
        var secret = _keyStore[keyId];

        using var hmac = new HMACSHA256(secret);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalJson));
        var hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

        // Encode KeyId into the audit seal: keyId:hashHex
        return $"{keyId}:{hashHex}";
    }

    public bool VerifySignature(HouseholdTaxSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.AuditSignatureHash))
        {
            return false;
        }

        var parts = snapshot.AuditSignatureHash.Split(':');
        string keyId;
        string actualHash;

        if (parts.Length == 2)
        {
            keyId = parts[0];
            actualHash = parts[1].ToLowerInvariant();
        }
        else
        {
            // Backward compatibility for legacy signatures without keyId prefix
            keyId = DefaultKeyId;
            actualHash = snapshot.AuditSignatureHash.ToLowerInvariant();
        }

        if (!_keyStore.TryGetValue(keyId, out var secret))
        {
            return false; // Unknown KeyId cannot be verified
        }

        var canonicalJson = BuildCanonicalJson(snapshot);
        using var hmac = new HMACSHA256(secret);
        var expectedHashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalJson));
        var expectedHash = Convert.ToHexString(expectedHashBytes).ToLowerInvariant();

        var actualBytes = Encoding.UTF8.GetBytes(actualHash);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedHash);

        return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }

    private static string BuildCanonicalJson(HouseholdTaxSnapshot s)
    {
        var utcTime = s.FinalizedAtUtc.Kind == DateTimeKind.Utc 
            ? s.FinalizedAtUtc 
            : s.FinalizedAtUtc.ToUniversalTime();
        var bookDataBytes = Encoding.UTF8.GetBytes(s.BookDataJson ?? string.Empty);
        var bookDataHash = Convert.ToHexString(SHA256.HashData(bookDataBytes)).ToLowerInvariant();

        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{{\"Year\":{0},\"PeriodType\":{1},\"PeriodNumber\":{2},\"TaxableRevenue\":{3:F2},\"TotalTaxAmount\":{4:F2},\"BookDataHash\":\"{5}\",\"FinalizedAtUtc\":\"{6:yyyy-MM-ddTHH:mm:ssZ}\",\"FinalizedBy\":\"{7}\"}}",
            s.Year,
            (int)s.PeriodType,
            s.PeriodNumber,
            s.TaxableRevenueInPeriod,
            s.TotalTaxAmount,
            bookDataHash,
            utcTime,
            s.FinalizedBy
        );
    }
}