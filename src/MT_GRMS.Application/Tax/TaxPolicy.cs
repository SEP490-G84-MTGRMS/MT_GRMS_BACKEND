using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Tax;

public sealed record TaxPolicy(
    string Version,
    decimal ExemptionThreshold,
    decimal MethodSwitchRevenueThreshold,
    int MethodLockYears,
    DateTime VatReductionDeadlineUtc)
{
    public static TaxPolicy Load(IConfiguration? configuration)
    {
        var json = configuration?["TaxSettings:PolicyJson"];
        var policy = string.IsNullOrWhiteSpace(json)
            ? Default
            : JsonSerializer.Deserialize<TaxPolicy>(json) ?? throw new InvalidOperationException("TaxSettings:PolicyJson is empty.");
        if (string.IsNullOrWhiteSpace(policy.Version) || policy.ExemptionThreshold <= 0 ||
            policy.MethodSwitchRevenueThreshold <= policy.ExemptionThreshold || policy.MethodLockYears < 1 ||
            policy.VatReductionDeadlineUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("TaxSettings:PolicyJson is invalid.");
        return policy;
    }

    public static TaxPolicy Default { get; } = new(
        "2026-nd68", 1_000_000_000m, 3_000_000_000m, 2,
        new DateTime(2026, 12, 31, 17, 0, 0, DateTimeKind.Utc));
}

public sealed record TaxMethodLock(int StartYear, int EndYear, TaxCalculationMethod Method, string Reason);

public sealed record TaxSnapshotPolicy(TaxPolicy Rules, TaxMethodLock? MethodLock);
