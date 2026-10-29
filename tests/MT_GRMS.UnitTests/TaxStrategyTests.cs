using MT_GRMS.Application.Tax.Strategies;
using MT_GRMS.Domain.Enums;
using Xunit;

namespace MT_GRMS.UnitTests;

public class TaxStrategyTests
{
    private readonly RevenuePercentageTaxStrategy _revenuePercentageStrategy = new();
    private readonly DeclarationBasedTaxStrategy _declarationStrategy = new();

    [Fact]
    public void RevenuePercentage_WhenExempt_ReturnsZeroTax()
    {
        var context = new TaxCalculationContext
        {
            Group = HouseholdTaxGroup.Group1_Under1B,
            IsExempt = true,
            AnnualAccumulatedRevenue = 800_000_000m,
            TaxableRevenueInPeriod = 200_000_000m
        };

        var result = _revenuePercentageStrategy.Calculate(context);

        Assert.Equal(0m, result.VatAmount);
        Assert.Equal(0m, result.PitAmount);
        Assert.Equal(0m, result.TotalTaxAmount);
    }

    [Fact]
    public void RevenuePercentage_StandardQ3_CalculatesExactVatAndPit()
    {
        // Doanh thu phát sinh Q3 = 467.500.000đ, Lũy kế năm = 1.417.500.000đ
        // Phần vượt 1 tỷ = 417.500.000đ
        // VAT = 467.500.000 * 1% = 4.675.000đ
        // PIT = 417.500.000 * 0.5% = 2.087.500đ
        // Tổng = 6.762.500đ
        var context = new TaxCalculationContext
        {
            Group = HouseholdTaxGroup.Group2_1BTo3B,
            IsExempt = false,
            AnnualAccumulatedRevenue = 1_417_500_000m,
            TaxableRevenueInPeriod = 467_500_000m,
            ApplyVatReduction2026 = false,
            PriorDeclaredPitTax = 0m
        };

        var result = _revenuePercentageStrategy.Calculate(context);

        Assert.Equal(1.0m, result.VatRate);
        Assert.Equal(4_675_000m, result.VatAmount);
        Assert.Equal(0.5m, result.PitRate);
        Assert.Equal(2_087_500m, result.PitAmount);
        Assert.Equal(6_762_500m, result.TotalTaxAmount);
        Assert.False(result.IsVatReduced);
    }

    [Fact]
    public void RevenuePercentage_WithVatReduction2026_AppliesTwentyPercentDiscount()
    {
        // VAT rate giảm 20%: 1.0% * 0.8 = 0.8%
        // 467.500.000 * 0.8% = 3.740.000đ
        var context = new TaxCalculationContext
        {
            Group = HouseholdTaxGroup.Group2_1BTo3B,
            IsExempt = false,
            AnnualAccumulatedRevenue = 1_417_500_000m,
            TaxableRevenueInPeriod = 467_500_000m,
            ApplyVatReduction2026 = true,
            PriorDeclaredPitTax = 0m
        };

        var result = _revenuePercentageStrategy.Calculate(context);

        Assert.Equal(0.8m, result.VatRate);
        Assert.Equal(3_740_000m, result.VatAmount);
        Assert.True(result.IsVatReduced);
    }

    [Fact]
    public void RevenuePercentage_DeductsPriorDeclaredPitTax()
    {
        // Thuế TNCN phát sinh lũy kế là 2.087.500đ, nhưng kỳ trước đã nộp 1.000.000đ
        // Số phải nộp kỳ này = 1.087.500đ
        var context = new TaxCalculationContext
        {
            Group = HouseholdTaxGroup.Group2_1BTo3B,
            IsExempt = false,
            AnnualAccumulatedRevenue = 1_417_500_000m,
            TaxableRevenueInPeriod = 467_500_000m,
            PriorDeclaredPitTax = 1_000_000m
        };

        var result = _revenuePercentageStrategy.Calculate(context);

        Assert.Equal(1_087_500m, result.PitAmount);
        Assert.Equal(0m, result.OverpaidTaxCarry);
    }

    [Fact]
    public void DeclarationBased_CalculatesPitOnNetProfit_WithGroupRates()
    {
        // Nhóm 3 (3B - 50B): Thuế suất mặc định 17%
        // YtdRevenue = 5.000.000.000đ, YtdExpenses = 4.000.000.000đ => Lợi nhuận tính thuế = 1.000.000.000đ
        // PIT = 1.000.000.000 * 17% = 170.000.000đ
        var context = new TaxCalculationContext
        {
            Group = HouseholdTaxGroup.Group3_3BTo50B,
            IsExempt = false,
            AnnualAccumulatedRevenue = 5_000_000_000m,
            TaxableRevenueInPeriod = 1_500_000_000m,
            PeriodDeductibleExpenses = 1_200_000_000m,
            YtdDeductibleExpenses = 4_000_000_000m,
            PriorDeclaredPitTax = 50_000_000m
        };

        var result = _declarationStrategy.Calculate(context);

        Assert.Equal(17.0m, result.PitRate);
        Assert.Equal(1_000_000_000m, result.TaxableIncomeYtd);
        Assert.Equal(170_000_000m, result.TaxDueYtd);
        Assert.Equal(120_000_000m, result.PitAmount); // 170tr - 50tr kỳ trước = 120tr
        Assert.Equal(0m, result.OverpaidTaxCarry);
    }

    [Fact]
    public void StrategyFactory_ResolvesRegisteredStrategiesCorrectly()
    {
        var factory = new TaxCalculationStrategyFactory(new ITaxCalculationStrategy[]
        {
            _revenuePercentageStrategy,
            _declarationStrategy
        });

        var strategy1 = factory.Resolve(TaxCalculationMethod.RevenuePercentage);
        var strategy2 = factory.Resolve(TaxCalculationMethod.DeclarationBased);

        Assert.IsType<RevenuePercentageTaxStrategy>(strategy1);
        Assert.IsType<DeclarationBasedTaxStrategy>(strategy2);
    }

    [Fact]
    public void RevenuePercentage_WithCustomVatReductionPercentage_AppliesConfiguredDiscount()
    {
        // Base rate 1.0%, cấu hình giảm 30% -> Thuế suất sau giảm = 0.70%
        // Doanh thu kỳ 200.000.000đ -> VAT = 1.400.000đ
        var context = new TaxCalculationContext
        {
            Group = HouseholdTaxGroup.Group2_1BTo3B,
            IsExempt = false,
            AnnualAccumulatedRevenue = 1_200_000_000m,
            TaxableRevenueInPeriod = 200_000_000m,
            ApplyVatReduction2026 = true,
            VatReductionPercentage = 30.0m // Giảm 30%
        };

        var result = _revenuePercentageStrategy.Calculate(context);

        Assert.True(result.IsVatReduced);
        Assert.Equal(1.0m, result.OriginalVatRate);
        Assert.Equal(30.0m, result.VatReductionPercentage);
        Assert.Equal(0.7m, result.VatRate);
        Assert.Equal(1_400_000m, result.VatAmount);
    }

    [Fact]
    public void RevenuePercentage_WithoutVatReduction_KeepsStandardRate()
    {
        var context = new TaxCalculationContext
        {
            Group = HouseholdTaxGroup.Group2_1BTo3B,
            IsExempt = false,
            AnnualAccumulatedRevenue = 1_200_000_000m,
            TaxableRevenueInPeriod = 200_000_000m,
            ApplyVatReduction2026 = false,
            VatReductionPercentage = null
        };

        var result = _revenuePercentageStrategy.Calculate(context);

        Assert.False(result.IsVatReduced);
        Assert.Equal(1.0m, result.OriginalVatRate);
        Assert.Equal(0m, result.VatReductionPercentage);
        Assert.Equal(1.0m, result.VatRate);
        Assert.Equal(2_000_000m, result.VatAmount);
    }
}

