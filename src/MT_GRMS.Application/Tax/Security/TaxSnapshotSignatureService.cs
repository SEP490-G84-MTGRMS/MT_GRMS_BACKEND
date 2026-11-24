using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
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
            ?? configuration?["TaxAudit:SecretKey"];

        if (string.IsNullOrWhiteSpace(primarySecret))
        {
            var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? string.Empty;
            var isProductionLike = string.Equals(envName, "Production", StringComparison.OrdinalIgnoreCase)
                || string.Equals(envName, "Staging", StringComparison.OrdinalIgnoreCase);
            if (isProductionLike)
            {
                throw new InvalidOperationException("TAX_AUDIT_SECRET_KEY (hoặc TaxAudit:SecretKey) bắt buộc phải được cấu hình trong môi trường Production/Staging.");
            }
            primarySecret = "MT_GRMS_DEFAULT_TAX_SNAPSHOT_AUDIT_HMAC_SECRET_KEY_2026";
        }

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
        var canonicalJson = BuildCanonicalJson(snapshot, CanonicalizePolicy(snapshot.TaxPolicyJson));
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

        var actualBytes = Encoding.UTF8.GetBytes(actualHash);
        using var hmac = new HMACSHA256(secret);
        bool Matches(string policy)
        {
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(BuildCanonicalJson(snapshot, policy)));
            var expectedBytes = Encoding.UTF8.GetBytes(Convert.ToHexString(hash).ToLowerInvariant());
            return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
        }

        try
        {
            var canonical = CanonicalizePolicy(snapshot.TaxPolicyJson);
            if (Matches(canonical)) return true;

            // Old snapshots signed the serialized policy rather than its canonical representation.
            if (string.IsNullOrWhiteSpace(snapshot.TaxPolicyJson) || canonical == "{}") return false;
            if (Matches(snapshot.TaxPolicyJson)) return true;

            // Recover the original serializer order only if every stored field is preserved.
            var legacyPolicy = JsonSerializer.Deserialize<TaxSnapshotPolicy>(snapshot.TaxPolicyJson);
            if (legacyPolicy?.Rules is null) return false;
            var serialized = JsonSerializer.Serialize(legacyPolicy);
            return CanonicalizePolicy(serialized) == canonical && Matches(serialized);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string CanonicalizePolicy(string? policy)
    {
        if (string.IsNullOrWhiteSpace(policy)) return "{}";
        using var document = JsonDocument.Parse(policy);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteCanonicalValue(writer, document.RootElement);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteCanonicalValue(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalValue(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var element in value.EnumerateArray())
                    WriteCanonicalValue(writer, element);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static string BuildCanonicalJson(HouseholdTaxSnapshot s, string policy)
    {
        // The PostgreSQL timestamp column stores UTC without a time zone.
        var utcTime = s.FinalizedAtUtc.Kind == DateTimeKind.Local
            ? s.FinalizedAtUtc.ToUniversalTime()
            : DateTime.SpecifyKind(s.FinalizedAtUtc, DateTimeKind.Utc);
        var bookDataBytes = Encoding.UTF8.GetBytes(s.BookDataJson ?? string.Empty);
        var bookDataHash = Convert.ToHexString(SHA256.HashData(bookDataBytes)).ToLowerInvariant();

        // Legacy snapshots retain their original signature format.
        var policyHash = policy == "{}"
            ? string.Empty
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(policy))).ToLowerInvariant();

        return string.Format(
            CultureInfo.InvariantCulture,
            "{{\"TenantId\":\"{0}\",\"Year\":{1},\"PeriodType\":{2},\"PeriodNumber\":{3},\"TaxableRevenue\":{4:F2},\"TotalTaxAmount\":{5:F2},\"BookDataHash\":\"{6}\",\"FinalizedAtUtc\":\"{7:yyyy-MM-ddTHH:mm:ssZ}\",\"FinalizedBy\":\"{8}\"{9}}}",
            s.TenantId ?? string.Empty,
            s.Year,
            (int)s.PeriodType,
            s.PeriodNumber,
            s.TaxableRevenueInPeriod,
            s.TotalTaxAmount,
            bookDataHash,
            utcTime,
            s.FinalizedBy,
            policyHash.Length == 0 ? string.Empty : $",\"TaxPolicyHash\":\"{policyHash}\""
        );
    }
}
