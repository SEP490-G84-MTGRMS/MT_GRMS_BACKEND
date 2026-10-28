using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Tax.Strategies;

/// <summary>
/// Strategy for calculating household taxes using the Declaration-Based (Kê khai) method.
/// Pure logic according to Circular 152/2025/TT-BTC and related regulations.
/// </summary>
public class DeclarationBasedTaxStrategy : ITaxCalculationStrategy
{
    public const decimal RetailVatRate = 1.0m;
    public const decimal DeclarationDefaultPitRate = 15.0m; // Default PIT rate for declaration method

    public TaxCalculationMethod Method => TaxCalculationMethod.DeclarationBased;

    public TaxCalculationResult Calculate(TaxCalculationContext context)
    {
        if (context.IsExempt)
        {
            return new TaxCalculationResult
            {
                Method = Method,
                VatTaxableRevenue = 0,
                VatRate = 0,
                VatAmount = 0,
                IsVatReduced = false,
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

        // 1. VAT Calculation
        var baseVatRate = context.CustomVatRate ?? RetailVatRate;
        var isVatReduced = context.ApplyVatReduction2026;
        var vatRate = isVatReduced ? (baseVatRate * 0.8m) : baseVatRate;
        var vatTaxableRevenue = context.TaxableRevenueInPeriod;
        var vatAmount = Math.Round(vatTaxableRevenue * (vatRate / 100m), 0, MidpointRounding.AwayFromZero);

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
            VatTaxableRevenue = vatTaxableRevenue,
            VatRate = vatRate,
            VatAmount = vatAmount,
            IsVatReduced = isVatReduced,
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
