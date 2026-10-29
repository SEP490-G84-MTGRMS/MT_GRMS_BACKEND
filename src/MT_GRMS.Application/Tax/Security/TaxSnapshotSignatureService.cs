using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Application.Tax.Security;

/// <summary>
/// Service implementation for HMAC-SHA256 snapshot signing and integrity verification using Canonical JSON.
/// Binds both core financial figures and the cryptographic SHA-256 digest of BookDataJson (including metadata & books).
/// </summary>
public class TaxSnapshotSignatureService : ITaxSnapshotSignatureService
{
    private readonly byte[] _secretKey;

    public TaxSnapshotSignatureService(IConfiguration? configuration = null)
    {
        var secret = Environment.GetEnvironmentVariable("TAX_AUDIT_SECRET_KEY")
            ?? configuration?["TaxAudit:SecretKey"]
            ?? "MT_GRMS_DEFAULT_TAX_SNAPSHOT_AUDIT_HMAC_SECRET_KEY_2026";

        _secretKey = Encoding.UTF8.GetBytes(secret);
    }

    public string ComputeSignature(HouseholdTaxSnapshot snapshot)
    {
        var canonicalJson = BuildCanonicalJson(snapshot);
        using var hmac = new HMACSHA256(_secretKey);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalJson));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public bool VerifySignature(HouseholdTaxSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.AuditSignatureHash))
        {
            return false;
        }

        var expectedSignature = ComputeSignature(snapshot);
        var actualBytes = Encoding.UTF8.GetBytes(snapshot.AuditSignatureHash.ToLowerInvariant());
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);

        return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }

    private static string BuildCanonicalJson(HouseholdTaxSnapshot s)
    {
        var bookDataBytes = Encoding.UTF8.GetBytes(s.BookDataJson ?? string.Empty);
        var bookDataHash = Convert.ToHexString(SHA256.HashData(bookDataBytes)).ToLowerInvariant();

        // Canonical string format with deterministic property ordering, culture invariant formatting,
        // and cryptographic digest of BookDataJson (including audit metadata and books).
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{{\"Year\":{0},\"PeriodType\":{1},\"PeriodNumber\":{2},\"TaxableRevenue\":{3:F2},\"TotalTaxAmount\":{4:F2},\"BookDataHash\":\"{5}\",\"FinalizedAtUtc\":\"{6:O}\",\"FinalizedBy\":\"{7}\"}}",
            s.Year,
            (int)s.PeriodType,
            s.PeriodNumber,
            s.TaxableRevenueInPeriod,
            s.TotalTaxAmount,
            bookDataHash,
            s.FinalizedAtUtc,
            s.FinalizedBy
        );
    }
}