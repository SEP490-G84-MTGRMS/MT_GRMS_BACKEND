using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Tax.Strategies;

/// <summary>
/// Strategy for calculating household taxes using the Declaration-Based (Kê khai) method.
/// Pure logic according to Circular 152/2025/TT-BTC, Decree 141/2026/ND-CP and Resolution 204/2025/QH15.
/// Supports configurable VAT reduction policies with custom reduction percentages, enable/disable toggles,
/// and mixed product catalogs (0.8% for standard items, 1.0% for special consumption items like alcohol/tobacco).
/// </summary>
public class DeclarationBasedTaxStrategy : ITaxCalculationStrategy
{
    public const decimal RetailVatRate = 1.0m;
    public const decimal DeclarationDefaultPitRate = 15.0m; // Default PIT rate for declaration method (15% for Group 2)

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
            if (isVatReduced)
            {
                reducedRevenue = vatTaxableRevenue;
                standardRevenue = 0m;
            }
            else
            {
                reducedRevenue = 0m;
                standardRevenue = vatTaxableRevenue;
            }
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

        // 2. PIT Calculation (YTD Profit Model: Revenue - Expenses (COGS + OPEX))
        var ytdExpenses = context.YtdDeductibleExpenses;
        var ytdRevenue = context.AnnualAccumulatedRevenue;
        var taxableIncomeYtd = Math.Max(0, ytdRevenue - ytdExpenses);

        var pitRate = context.CustomPitRate ?? (context.Group == HouseholdTaxGroup.Group4_Over50B ? 20.0m
            : (context.Group == HouseholdTaxGroup.Group3_3BTo50B ? 17.0m : DeclarationDefaultPitRate));

        var taxDueYtd = Math.Round(taxableIncomeYtd * (pitRate / 100m), 0, MidpointRounding.AwayFromZero);
        var pitPayable = Math.Max(0, taxDueYtd - context.PriorDeclaredPitTax);
        var overpaidTaxCarry = Math.Max(0, context.PriorDeclaredPitTax - taxDueYtd);

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
            PitTaxableRevenue = taxableIncomeYtd,
            PitRate = pitRate,
            PitAmount = pitPayable,
            TotalTaxAmount = vatAmount + pitPayable,
            CogsAmount = context.PeriodCogs,
            OpexAmount = context.PeriodOpex,
            IsCogsEstimated = context.IsCogsEstimated,
            DeductibleExpenses = context.PeriodDeductibleExpenses,
            YtdExpenses = ytdExpenses,
            YtdRevenue = ytdRevenue,
            TaxableIncomeYtd = taxableIncomeYtd,
            TaxDueYtd = taxDueYtd,
            PriorDeclaredPitTax = context.PriorDeclaredPitTax,
            OverpaidTaxCarry = overpaidTaxCarry,
            TaxableIncomeDeclaration = taxableIncomeYtd,
            DeclarationPitAmount = pitPayable
        };
    }
}