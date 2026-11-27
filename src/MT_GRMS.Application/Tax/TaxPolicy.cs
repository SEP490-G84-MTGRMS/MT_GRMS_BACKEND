using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Tax;

/// <summary>
/// Versioned JSON tax policy for Vietnam Household Business retail rules (2026-2027).
/// Legacy 5-field snapshots remain deserializable and signature-verifiable via WhenWritingNull.
/// </summary>
public sealed record TaxPolicy(
    string Version,
    decimal ExemptionThreshold,
    decimal MethodSwitchRevenueThreshold,
    int MethodLockYears,
    DateTime VatReductionDeadlineUtc)
{
    /// <summary>
    /// Threshold for choosing Revenue % vs Profit method from tax year 2027 (10B VND per National Assembly Law passed 20/11/2026).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? MethodSwitchThresholdFrom2027 { get; init; }

    /// <summary>
    /// Large-scale household business threshold (50B VND/year — Group 4, 20% PIT, monthly filing).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? LargeScaleThreshold { get; init; }

    /// <summary>
    /// Minimum payment threshold requiring cashless bank transfer for deductible expenses (5M VND per Art. 6 Decree 68/2026).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? CashlessExpenseThreshold { get; init; }

    /// <summary>
    /// Tax year from which business license fee (Le phi mon bai) is abolished (2026 per Res. 198/2025 & Decree 362/2025).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LicenseFeeAbolishedFromYear { get; init; }

    /// <summary>
    /// PIT reduction percentage under Res. 43/2026/QH16 & Decree 398/2026/ND-CP (30%).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? PitReductionPercentage { get; init; }

    /// <summary>
    /// Maximum annual revenue eligible for 30% PIT reduction (10B VND per Decree 398/2026/ND-CP).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? PitReductionMaxRevenue { get; init; }

    /// <summary>
    /// First tax year eligible for 30% PIT reduction (2026).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PitReductionStartYear { get; init; }

    /// <summary>
    /// Last tax year eligible for 30% PIT reduction (2027).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PitReductionEndYear { get; init; }

    [JsonIgnore]
    public decimal EffectiveLargeScaleThreshold => LargeScaleThreshold ?? 50_000_000_000m;

    [JsonIgnore]
    public decimal EffectiveCashlessExpenseThreshold => CashlessExpenseThreshold ?? 5_000_000m;

    [JsonIgnore]
    public decimal EffectivePitReductionPercentage => PitReductionPercentage ?? 30m;

    [JsonIgnore]
    public decimal EffectivePitReductionMaxRevenue => PitReductionMaxRevenue ?? 10_000_000_000m;

    /// <summary>
    /// Returns the effective revenue threshold above which profit-based declaration becomes mandatory for a given tax year.
    /// 2026: 3B VND (Decree 68/2026). 2027+: 10B VND (Law passed 20/11/2026).
    /// </summary>
    public decimal GetMethodSwitchThreshold(int year)
        => year >= 2027 ? (MethodSwitchThresholdFrom2027 ?? 10_000_000_000m) : MethodSwitchRevenueThreshold;

    /// <summary>
    /// Returns true if business license fee (Le phi mon bai) is abolished for the given tax year.
    /// </summary>
    public bool IsLicenseFeeAbolished(int year) => year >= (LicenseFeeAbolishedFromYear ?? 2026);

    /// <summary>
    /// Returns true if the 30% PIT reduction policy (Res. 43/2026 & Decree 398/2026) is active for the given tax year.
    /// </summary>
    public bool IsPitReductionYear(int year)
        => year >= (PitReductionStartYear ?? 2026) && year <= (PitReductionEndYear ?? 2027);

    /// <summary>
    /// Returns true if the household qualifies for 30% PIT reduction for the given tax year and annual revenue.
    /// </summary>
    public bool IsPitReductionEligible(int year, decimal annualRevenue)
        => IsPitReductionYear(year) && annualRevenue <= EffectivePitReductionMaxRevenue;

    /// <summary>
    /// Returns true if the 20% VAT rate reduction (Res. 204/2025/QH15) is active at the given UTC timestamp.
    /// </summary>
    public bool IsVatReductionActive(DateTime utcTimestamp)
    {
        var utc = utcTimestamp.Kind == DateTimeKind.Local
            ? utcTimestamp.ToUniversalTime()
            : DateTime.SpecifyKind(utcTimestamp, DateTimeKind.Utc);
        return utc < VatReductionDeadlineUtc;
    }

    /// <summary>
    /// Loads TaxPolicy from configuration ("TaxSettings:PolicyJson" or structured "TaxSettings:Policy"),
    /// falling back to TaxPolicy.Default when unconfigured.
    /// </summary>
    public static TaxPolicy Load(IConfiguration? configuration)
    {
        TaxPolicy policy;
        var json = configuration?["TaxSettings:PolicyJson"];

        if (!string.IsNullOrWhiteSpace(json))
        {
            policy = JsonSerializer.Deserialize<TaxPolicy>(json)
                ?? throw new InvalidOperationException("TaxSettings:PolicyJson is empty.");
        }
        else
        {
            var section = configuration?.GetSection("TaxSettings:Policy");
            policy = section != null && section.Exists()
                ? ParseFromSection(section)
                : Default;
        }

        ValidatePolicy(policy);
        return policy;
    }

    private static TaxPolicy ParseFromSection(IConfigurationSection section)
    {
        var defaults = Default;
        var version = section["Version"] ?? defaults.Version;
        var exemption = ParseDecimal(section["ExemptionThreshold"], defaults.ExemptionThreshold);
        var switchThreshold = ParseDecimal(section["MethodSwitchRevenueThreshold"], defaults.MethodSwitchRevenueThreshold);
        var lockYears = ParseInt(section["MethodLockYears"], defaults.MethodLockYears);
        var vatDeadline = ParseUtcDate(section["VatReductionDeadlineUtc"], defaults.VatReductionDeadlineUtc);

        return new TaxPolicy(version, exemption, switchThreshold, lockYears, vatDeadline)
        {
            MethodSwitchThresholdFrom2027 = ParseDecimal(section["MethodSwitchThresholdFrom2027"], defaults.MethodSwitchThresholdFrom2027!.Value),
            LargeScaleThreshold = ParseDecimal(section["LargeScaleThreshold"], defaults.LargeScaleThreshold!.Value),
            CashlessExpenseThreshold = ParseDecimal(section["CashlessExpenseThreshold"], defaults.CashlessExpenseThreshold!.Value),
            LicenseFeeAbolishedFromYear = ParseInt(section["LicenseFeeAbolishedFromYear"], defaults.LicenseFeeAbolishedFromYear!.Value),
            PitReductionPercentage = ParseDecimal(section["PitReductionPercentage"], defaults.PitReductionPercentage!.Value),
            PitReductionMaxRevenue = ParseDecimal(section["PitReductionMaxRevenue"], defaults.PitReductionMaxRevenue!.Value),
            PitReductionStartYear = ParseInt(section["PitReductionStartYear"], defaults.PitReductionStartYear!.Value),
            PitReductionEndYear = ParseInt(section["PitReductionEndYear"], defaults.PitReductionEndYear!.Value)
        };
    }

    private static decimal ParseDecimal(string? raw, decimal fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            throw new InvalidOperationException($"Invalid decimal value in TaxSettings:Policy: '{raw}'.");
        return parsed;
    }

    private static int ParseInt(string? raw, int fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            throw new InvalidOperationException($"Invalid integer value in TaxSettings:Policy: '{raw}'.");
        return parsed;
    }

    private static DateTime ParseUtcDate(string? raw, DateTime fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            throw new InvalidOperationException($"Invalid UTC date in TaxSettings:Policy: '{raw}'.");
        return parsed;
    }

    private static void ValidatePolicy(TaxPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(policy.Version) ||
            policy.ExemptionThreshold <= 0 ||
            policy.MethodSwitchRevenueThreshold <= policy.ExemptionThreshold ||
            policy.MethodLockYears < 1 ||
            policy.VatReductionDeadlineUtc.Kind != DateTimeKind.Utc ||
            policy.EffectiveLargeScaleThreshold <= policy.MethodSwitchRevenueThreshold ||
            policy.EffectiveCashlessExpenseThreshold <= 0 ||
            policy.EffectivePitReductionPercentage < 0 ||
            policy.EffectivePitReductionPercentage > 100 ||
            policy.EffectivePitReductionMaxRevenue < policy.ExemptionThreshold)
        {
            throw new InvalidOperationException("TaxSettings policy configuration is invalid.");
        }
    }

    public static TaxPolicy Default { get; } = new(
        "2026-2027-nd68-nd398-nd254",
        1_000_000_000m,
        3_000_000_000m,
        2,
        new DateTime(2026, 12, 31, 17, 0, 0, DateTimeKind.Utc))
    {
        MethodSwitchThresholdFrom2027 = 10_000_000_000m,
        LargeScaleThreshold = 50_000_000_000m,
        CashlessExpenseThreshold = 5_000_000m,
        LicenseFeeAbolishedFromYear = 2026,
        PitReductionPercentage = 30m,
        PitReductionMaxRevenue = 10_000_000_000m,
        PitReductionStartYear = 2026,
        PitReductionEndYear = 2027
    };
}

public sealed record TaxMethodLock(int StartYear, int EndYear, TaxCalculationMethod Method, string Reason);

public sealed record TaxSnapshotPolicy(TaxPolicy Rules, TaxMethodLock? MethodLock);
