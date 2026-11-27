using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Tax.Strategies;

/// <summary>
/// Strategy for calculating household taxes using the Declaration-Based (Kê khai) method.
/// Pure logic according to Circular 152/2025/TT-BTC, Decree 68/2026/ND-CP (Art. 10 Clause 2),
/// Decree 141/2026/ND-CP, Resolution 204/2025/QH15, and Decree 398/2026/ND-CP (30% PIT reduction).
/// </summary>
public class DeclarationBasedTaxStrategy : ITaxCalculationStrategy
{
    public const decimal RetailVatRate = 1.0m;
    public const decimal DeclarationDefaultPitRate = 15.0m; // Default annual settlement PIT rate for Group 2 (15%)

    public TaxCalculationMethod Method => TaxCalculationMethod.DeclarationBased;

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

        // 2. PIT Calculation (Art. 10 Clause 2 Decree 68/2026 + 30% PIT Reduction per Res. 43/2026 & Decree 398/2026)
        // Monthly/Quarterly periods use provisional PIT on excess revenue over 1B (0.5% retail);
        // Annual period settles on actual valid profit (Revenue - Documented COGS - Art. 6 OPEX) * 15%/17%/20%.
        var ytdExpenses = context.YtdDeductibleExpenses;
        var ytdRevenue = context.AnnualAccumulatedRevenue;
        var taxableIncomeYtd = Math.Max(0, ytdRevenue - ytdExpenses);

        var settlementPitRate = context.CustomPitRate ?? (context.Group == HouseholdTaxGroup.Group4_Over50B ? 20.0m
            : (context.Group == HouseholdTaxGroup.Group3_3BTo50B || ytdRevenue > 3_000_000_000m ? 17.0m : DeclarationDefaultPitRate));

        var isPitReduced = context.ApplyPitReduction
            && ytdRevenue <= context.PitReductionMaxRevenue
            && context.PitReductionPercentage > 0m;
        var pitReductionPct = isPitReduced ? context.PitReductionPercentage : 0m;

        var settlementTaxBeforeReduction = Math.Round(taxableIncomeYtd * (settlementPitRate / 100m), 0, MidpointRounding.AwayFromZero);
        var settlementReduction = isPitReduced
            ? Math.Round(settlementTaxBeforeReduction * (pitReductionPct / 100m), 0, MidpointRounding.AwayFromZero)
            : 0m;
        var estimatedAnnualSettlementPit = settlementTaxBeforeReduction - settlementReduction;

        var isProvisionalPeriod = context.PeriodType is TaxPeriodType.Month or TaxPeriodType.Quarter;

        decimal pitRate;
        decimal pitTaxableRevenue;
        decimal taxDueYtdBeforeReduction;
        decimal pitReductionYtd;
        decimal taxDueYtd;

        if (isProvisionalPeriod)
        {
            pitRate = RevenuePercentageTaxStrategy.RetailPitRate;
            pitTaxableRevenue = Math.Max(0m, ytdRevenue - context.ExemptionThreshold);
            taxDueYtdBeforeReduction = Math.Round(pitTaxableRevenue * (pitRate / 100m), 0, MidpointRounding.AwayFromZero);
            pitReductionYtd = isPitReduced
                ? Math.Round(taxDueYtdBeforeReduction * (pitReductionPct / 100m), 0, MidpointRounding.AwayFromZero)
                : 0m;
            taxDueYtd = taxDueYtdBeforeReduction - pitReductionYtd;
        }
        else
        {
            pitRate = settlementPitRate;
            pitTaxableRevenue = taxableIncomeYtd;
            taxDueYtdBeforeReduction = settlementTaxBeforeReduction;
            pitReductionYtd = settlementReduction;
            taxDueYtd = estimatedAnnualSettlementPit;
        }

        var pitPayable = Math.Max(0, taxDueYtd - context.PriorDeclaredPitTax);
        var overpaidTaxCarry = Math.Max(0, context.PriorDeclaredPitTax - taxDueYtd);

        decimal pitBeforeReduction;
        decimal pitReductionAmount;
        if (!isPitReduced)
        {
            pitBeforeReduction = pitPayable;
            pitReductionAmount = 0m;
        }
        else if (!isProvisionalPeriod || context.PriorDeclaredPitTax == 0m)
        {
            pitBeforeReduction = taxDueYtdBeforeReduction;
            pitReductionAmount = pitReductionYtd;
        }
        else
        {
            var netFactor = Math.Max(0.0001m, 1m - (pitReductionPct / 100m));
            pitBeforeReduction = Math.Round(pitPayable / netFactor, 0, MidpointRounding.AwayFromZero);
            pitReductionAmount = Math.Max(0m, pitBeforeReduction - pitPayable);
        }

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
            PitTaxableRevenue = pitTaxableRevenue,
            PitRate = pitRate,
            PitBeforeReduction = pitBeforeReduction,
            PitReductionPercentage = pitReductionPct,
            PitReductionAmount = pitReductionAmount,
            IsPitReduced = isPitReduced,
            PitAmount = pitPayable,
            TotalTaxAmount = vatAmount + pitPayable,
            CogsAmount = context.PeriodCogs,
            OpexAmount = context.PeriodOpex,
            IsCogsEstimated = context.IsCogsEstimated,
            DeductibleExpenses = context.PeriodDeductibleExpenses,
            YtdExpenses = ytdExpenses,
            YtdRevenue = ytdRevenue,
            TaxableIncomeYtd = taxableIncomeYtd,
            TaxDueYtdBeforeReduction = taxDueYtdBeforeReduction,
            PitReductionYtd = pitReductionYtd,
            TaxDueYtd = taxDueYtd,
            PriorDeclaredPitTax = context.PriorDeclaredPitTax,
            OverpaidTaxCarry = overpaidTaxCarry,
            TaxableIncomeDeclaration = taxableIncomeYtd,
            DeclarationPitAmount = isProvisionalPeriod ? estimatedAnnualSettlementPit : pitPayable,
            IsProvisionalPitPeriod = isProvisionalPeriod,
            AnnualSettlementPitRate = settlementPitRate,
            EstimatedAnnualSettlementPit = estimatedAnnualSettlementPit
        };
    }
}
