using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Tax.Strategies;

/// <summary>
/// Strategy for calculating household taxes using the Revenue Percentage (Tỷ lệ %) method.
/// Pure logic according to Decree 141/2026/ND-CP and Resolution 204/2025/QH15.
/// Supports configurable VAT reduction policies with custom reduction percentages, enable/disable toggles,
/// and mixed product catalogs (0.8% for standard items, 1.0% for special consumption items like alcohol/tobacco).
/// </summary>
public class RevenuePercentageTaxStrategy : ITaxCalculationStrategy
{
    public const decimal TaxExemptionThreshold = 1_000_000_000m; // 1 billion VND threshold
    public const decimal RetailVatRate = 1.0m; // 1.0% VAT for retail commerce
    public const decimal RetailPitRate = 0.5m; // 0.5% PIT for retail commerce (portion exceeding 1 billion)

    public TaxCalculationMethod Method => TaxCalculationMethod.RevenuePercentage;

    public TaxCalculationResult Calculate(TaxCalculationContext context)
    {
        if (context.IsExempt)
        {
            return new TaxCalculationResult
            {
                Method = Method,
                OriginalVatRate = RetailVatRate,
                VatReductionPercentage = 0,
                VatTaxableRevenue = 0,
                VatRate = 0,
                VatAmount = 0,
                IsVatReduced = false,
                VatBreakdown = new VatBreakdown(),
                PitTaxableRevenue = 0,
                PitRate = 0,
                PitAmount = 0,
                TotalTaxAmount = 0,
                CogsAmount = 0,
                OpexAmount = 0,
                IsCogsEstimated = false,
                YtdRevenue = context.AnnualAccumulatedRevenue
            };
        }

        // 1. VAT Calculation with configurable reduction policy & mixed items support
        var baseVatRate = context.CustomVatRate ?? RetailVatRate;
        var isVatReduced = context.ApplyVatReduction2026 || (context.VatReductionPercentage.HasValue && context.VatReductionPercentage.Value > 0);
        var reductionPct = isVatReduced ? (context.VatReductionPercentage ?? 20.0m) : 0m;
        var discountMultiplier = Math.Max(0m, 1.0m - (reductionPct / 100m));
        var reducedVatRate = isVatReduced ? Math.Round(baseVatRate * discountMultiplier, 4, MidpointRounding.AwayFromZero) : baseVatRate;
        var vatTaxableRevenue = context.TaxableRevenueInPeriod;

        decimal standardRevenue;
        decimal reducedRevenue;

        if (context.ReducedVatRevenueInPeriod.HasValue || context.StandardVatRevenueInPeriod.HasValue)
        {
            reducedRevenue = context.ReducedVatRevenueInPeriod ?? 0m;
            standardRevenue = context.StandardVatRevenueInPeriod ?? Math.Max(0, vatTaxableRevenue - reducedRevenue);
        }
        else
        {
            // Split data not available -> treat as standard/unreduced to avoid underestimating taxes
            reducedRevenue = 0m;
            standardRevenue = vatTaxableRevenue;
        }

        var vatAmountStandard = Math.Round(standardRevenue * (baseVatRate / 100m), 0, MidpointRounding.AwayFromZero);
        var vatAmountReduced = Math.Round(reducedRevenue * (reducedVatRate / 100m), 0, MidpointRounding.AwayFromZero);
        var vatAmount = vatAmountStandard + vatAmountReduced;

        var effectiveVatRate = vatTaxableRevenue > 0
            ? Math.Round((vatAmount / vatTaxableRevenue) * 100m, 4, MidpointRounding.AwayFromZero)
            : (isVatReduced ? reducedVatRate : baseVatRate);

        var vatBreakdown = new VatBreakdown
        {
            RevenueStandardRate10 = standardRevenue,
            TaxAmountStandardRate10 = vatAmountStandard,
            RevenueReducedRate08 = reducedRevenue,
            TaxAmountReducedRate08 = vatAmountReduced,
            EffectiveVatRate = effectiveVatRate
        };

        // 2. PIT Calculation (YTD Cumulative Model + 30% PIT Reduction per Res. 43/2026 & Decree 398/2026)
        var ytdRevenue = context.AnnualAccumulatedRevenue;
        var currentExcess = Math.Max(0, ytdRevenue - context.ExemptionThreshold);
        var pitRate = context.CustomPitRate ?? RetailPitRate;
        var taxDueYtdBeforeReduction = Math.Round(currentExcess * (pitRate / 100m), 0, MidpointRounding.AwayFromZero);

        var isPitReduced = context.ApplyPitReduction
            && ytdRevenue <= context.PitReductionMaxRevenue
            && context.PitReductionPercentage > 0m;
        var pitReductionPct = isPitReduced ? context.PitReductionPercentage : 0m;
        var pitReductionYtd = isPitReduced
            ? Math.Round(taxDueYtdBeforeReduction * (pitReductionPct / 100m), 0, MidpointRounding.AwayFromZero)
            : 0m;
        var taxDueYtd = taxDueYtdBeforeReduction - pitReductionYtd;

        var pitPayable = Math.Max(0, taxDueYtd - context.PriorDeclaredPitTax);
        var overpaidTaxCarry = Math.Max(0, context.PriorDeclaredPitTax - taxDueYtd);

        decimal pitBeforeReduction;
        if (!isPitReduced)
        {
            pitBeforeReduction = pitPayable;
        }
        else if (context.PriorDeclaredPitTax == 0m)
        {
            pitBeforeReduction = taxDueYtdBeforeReduction;
        }
        else
        {
            var netFactor = Math.Max(0.0001m, 1m - (pitReductionPct / 100m));
            pitBeforeReduction = Math.Round(pitPayable / netFactor, 0, MidpointRounding.AwayFromZero);
        }
        var pitReductionAmount = Math.Max(0m, pitBeforeReduction - pitPayable);

        return new TaxCalculationResult
        {
            Method = Method,
            OriginalVatRate = baseVatRate,
            VatReductionPercentage = reductionPct,
            VatTaxableRevenue = vatTaxableRevenue,
            VatRate = effectiveVatRate,
            VatAmount = vatAmount,
            IsVatReduced = isVatReduced,
            VatBreakdown = vatBreakdown,
            PitTaxableRevenue = currentExcess,
            PitRate = pitRate,
            PitBeforeReduction = pitBeforeReduction,
            PitReductionPercentage = pitReductionPct,
            PitReductionAmount = pitReductionAmount,
            IsPitReduced = isPitReduced,
            PitAmount = pitPayable,
            TotalTaxAmount = vatAmount + pitPayable,
            CogsAmount = 0,
            OpexAmount = 0,
            IsCogsEstimated = false,
            DeductibleExpenses = 0,
            YtdExpenses = 0,
            YtdRevenue = ytdRevenue,
            TaxableIncomeYtd = 0,
            TaxDueYtdBeforeReduction = taxDueYtdBeforeReduction,
            PitReductionYtd = pitReductionYtd,
            TaxDueYtd = taxDueYtd,
            PriorDeclaredPitTax = context.PriorDeclaredPitTax,
            OverpaidTaxCarry = overpaidTaxCarry,
            TaxableIncomeDeclaration = 0,
            DeclarationPitAmount = 0,
            IsProvisionalPitPeriod = false,
            AnnualSettlementPitRate = pitRate,
            EstimatedAnnualSettlementPit = taxDueYtd
        };
    }
}
