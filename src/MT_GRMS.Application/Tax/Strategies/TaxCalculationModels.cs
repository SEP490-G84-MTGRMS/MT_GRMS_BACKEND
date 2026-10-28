using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Tax.Strategies;

/// <summary>
/// Immutable input context for tax calculation strategies (pure logic).
/// </summary>
public record TaxCalculationContext
{
    public HouseholdTaxGroup Group { get; init; }
    public bool IsExempt { get; init; }
    public decimal AnnualAccumulatedRevenue { get; init; }
    public decimal TaxableRevenueInPeriod { get; init; }
    public decimal? CustomVatRate { get; init; }
    public bool ApplyVatReduction2026 { get; init; }
    public decimal? CustomPitRate { get; init; }
    public decimal PriorDeclaredPitTax { get; init; }
    public decimal PeriodDeductibleExpenses { get; init; }
    public decimal YtdDeductibleExpenses { get; init; }
}

/// <summary>
/// Output result produced by a tax calculation strategy.
/// </summary>
public record TaxCalculationResult
{
    public TaxCalculationMethod Method { get; init; }
    public decimal VatTaxableRevenue { get; init; }
    public decimal VatRate { get; init; }
    public decimal VatAmount { get; init; }
    public bool IsVatReduced { get; init; }
    public decimal PitTaxableRevenue { get; init; }
    public decimal PitRate { get; init; }
    public decimal PitAmount { get; init; }
    public decimal TotalTaxAmount { get; init; }

    // Breakdown for declaration-based reporting and YTD accounting
    public decimal DeductibleExpenses { get; init; }
    public decimal YtdExpenses { get; init; }
    public decimal YtdRevenue { get; init; }
    public decimal TaxableIncomeYtd { get; init; }
    public decimal TaxDueYtd { get; init; }
    public decimal PriorDeclaredPitTax { get; init; }
    public decimal OverpaidTaxCarry { get; init; }
    public decimal TaxableIncomeDeclaration { get; init; }
    public decimal DeclarationPitAmount { get; init; }
}
