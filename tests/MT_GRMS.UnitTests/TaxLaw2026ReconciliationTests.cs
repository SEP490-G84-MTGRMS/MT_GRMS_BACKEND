using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.Tax;
using MT_GRMS.Application.Tax.Dtos;
using MT_GRMS.Application.Tax.Security;
using MT_GRMS.Application.Tax.Strategies;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using MT_GRMS.Infrastructure.Services;
using Xunit;

namespace MT_GRMS.UnitTests;

/// <summary>
/// Golden 2026-2027 Vietnam Household Business Tax Law & Cross-Layer Reconciliation Test Suite.
/// Verifies GAP-01 through GAP-12 + National Assembly Law passed 20/11/2026 (10B threshold from 2027).
/// </summary>
public class TaxLaw2026ReconciliationTests
{
    private sealed class OwnerTenantContext : ITenantContext
    {
        public string CurrentTenantId => "tenant-tax-2026";
        public Guid CurrentUserId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string CurrentRole => "STORE_OWNER";
        public Guid? CurrentBranchId => null;
        public bool IsSystemAdmin => false;
        public void SetContext(string tenantId, Guid userId, Guid? branchId = null) { }
        public void SetRole(string? role) { }
    }

    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"TaxLaw2026_{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options, new OwnerTenantContext());
    }

    private static HouseholdTaxService CreateService(AppDbContext db, IConfiguration? configuration = null)
    {
        var repo = new HouseholdTaxRepository(db);
        var factory = new TaxCalculationStrategyFactory(new ITaxCalculationStrategy[]
        {
            new RevenuePercentageTaxStrategy(),
            new DeclarationBasedTaxStrategy()
        });
        return new HouseholdTaxService(
            repo,
            factory,
            pinService: null,
            signatureService: new TaxSnapshotSignatureService(),
            configuration: configuration,
            tenantContext: new OwnerTenantContext());
    }

    private static (Tenant Tenant, Branch Branch, Supplier Supplier, Product ReducedProd, Product StandardProd, Product MissingCostProd) SeedBaseData(AppDbContext db)
    {
        var tenant = new Tenant
        {
            Id = "tenant-tax-2026",
            Name = "Hộ Kinh Doanh Tạp Hóa Minh Anh",
            TaxCode = "0108889990",
            Address = "100 Trần Hưng Đạo, Hà Nội"
        };
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            BranchCode = "CN01",
            Name = "Cửa hàng chính",
            Address = tenant.Address,
            IsActive = true,
            IsHeadquarter = true
        };
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Code = "NCC-01",
            Name = "Công ty Phân Phối Hàng Tiêu Dùng",
            Status = SupplierStatus.Active
        };
        var reducedProd = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            ProductCode = "SP-SUA",
            Barcode = "8930001",
            Name = "Sữa tươi hộp",
            BaseUnit = "Thùng",
            CostPrice = 200_000m,
            SellingPrice = 250_000m,
            VatRate = 0.008m,
            EligibleForVatReduction = true,
            Status = ProductStatus.Active
        };
        var standardProd = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            ProductCode = "SP-BIA",
            Barcode = "8930002",
            Name = "Bia lon (không giảm VAT)",
            BaseUnit = "Thùng",
            CostPrice = 300_000m,
            SellingPrice = 400_000m,
            VatRate = 0.01m,
            EligibleForVatReduction = false,
            Status = ProductStatus.Active
        };
        var missingCostProd = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            ProductCode = "SP-NOCOST",
            Barcode = "8930003",
            Name = "Hàng chưa nhập giá vốn",
            BaseUnit = "Cái",
            CostPrice = null,
            SellingPrice = 100_000m,
            VatRate = 0.01m,
            EligibleForVatReduction = false,
            Status = ProductStatus.Active
        };

        db.Tenants.Add(tenant);
        db.Branches.Add(branch);
        db.Suppliers.Add(supplier);
        db.Products.AddRange(reducedProd, standardProd, missingCostProd);
        db.SaveChanges();
        return (tenant, branch, supplier, reducedProd, standardProd, missingCostProd);
    }

    private static Order CreatePaidOrder(
        string tenantId,
        Guid branchId,
        string code,
        DateTime createdAtUtc,
        Product product,
        decimal qty,
        decimal unitPrice,
        decimal costPriceAtSale,
        decimal appliedVatRate)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            OrderCode = code,
            TotalAmount = qty * unitPrice,
            DiscountAmount = 0m,
            ReturnedAmount = 0m,
            PaymentStatus = "PAID",
            Status = "COMPLETED",
            CreatedAt = createdAtUtc
        };
        order.Details.Add(new OrderDetail
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductId = product.Id,
            Product = product,
            ProductName = product.Name,
            UnitName = product.BaseUnit,
            Qty = qty,
            UnitPrice = unitPrice,
            ActualPrice = unitPrice,
            CostPriceAtSale = costPriceAtSale,
            AppliedVatRate = appliedVatRate
        });
        return order;
    }

    [Fact]
    public async Task Golden_2026_Group1_850M_Exempt_UsesForm01TknCnkd_Deadline31Jan_S1a()
    {
        using var db = CreateInMemoryDb();
        var (tenant, branch, _, _, standardProd, _) = SeedBaseData(db);
        db.Orders.Add(CreatePaidOrder(tenant.Id, branch.Id, "DH-850M", new DateTime(2026, 6, 15, 5, 0, 0, DateTimeKind.Utc), standardProd, 850m, 1_000_000m, 700_000m, 0.01m));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var filter = new HouseholdTaxFilterDto { Year = 2026, PeriodType = TaxPeriodType.Year, PeriodNumber = 1 };

        var summary = await service.GetTaxSummaryAsync(filter);
        var preview = await service.GetDeclarationPreviewAsync(filter);

        Assert.True(summary.IsTaxExempt);
        Assert.Equal(HouseholdTaxGroup.Group1_Under1B, summary.HouseholdGroup);
        Assert.Equal(0m, summary.TotalTaxAmount);
        Assert.Equal("S1a-HKD", summary.BooksSummary.PrimaryBookCode);
        // 31/01/2027 is Sunday -> rolls to Monday 01/02/2027
        Assert.Equal(new DateTime(2027, 2, 1), summary.FilingDeadlineLocal.Date);
        Assert.Equal("01/TKN-CNKD", preview.FormCode);
        Assert.False(preview.RequiresBusinessActivityAppendix);
    }

    [Fact]
    public async Task Golden_2026_Group2_1Point5B_Vat20PctReduction_Pit30PctReduction_Form01()
    {
        using var db = CreateInMemoryDb();
        var (tenant, branch, _, reducedProd, standardProd, _) = SeedBaseData(db);
        // Q1: 1.0B reduced (0.8%) + Q2: 300M reduced (0.8%) + 200M standard (1.0%) -> YTD 1.5B
        db.Orders.Add(CreatePaidOrder(tenant.Id, branch.Id, "DH-Q1", new DateTime(2026, 2, 10, 5, 0, 0, DateTimeKind.Utc), reducedProd, 4000m, 250_000m, 200_000m, 0.008m));
        db.Orders.Add(CreatePaidOrder(tenant.Id, branch.Id, "DH-Q2-RED", new DateTime(2026, 5, 10, 5, 0, 0, DateTimeKind.Utc), reducedProd, 1200m, 250_000m, 200_000m, 0.008m));
        db.Orders.Add(CreatePaidOrder(tenant.Id, branch.Id, "DH-Q2-STD", new DateTime(2026, 5, 15, 5, 0, 0, DateTimeKind.Utc), standardProd, 500m, 400_000m, 300_000m, 0.01m));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 2,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage,
            ApplyVatReduction2026 = true
        };

        var summary = await service.GetTaxSummaryAsync(filter);
        var preview = await service.GetDeclarationPreviewAsync(filter);

        // Crossed 1B mid-year (Q1=1.0B <= 1B, Q2 YTD=1.5B > 1B):
        // VAT YTD = 1.3B * 0.8% (10.4M) + 200M * 1.0% (2.0M) = 12.4M
        Assert.True(summary.IsThresholdCrossedMidYear);
        Assert.Equal(12_400_000m, summary.VatAmount);
        // E-invoice 30-day deadline after Q2 end (30/06/2026 + 30 days = 30/07/2026)
        Assert.Equal(new DateTime(2026, 7, 30), summary.EInvoiceRegistrationDeadlineLocal?.Date);

        // PIT = (1.5B - 1.0B) * 0.5% = 2.5M -> 30% reduction (-750k) = 1.75M
        Assert.Equal(2_500_000m, summary.PitBeforeReduction);
        Assert.Equal(750_000m, summary.PitReductionAmount);
        Assert.Equal(1_750_000m, summary.PitAmount);
        Assert.True(summary.IsPitReduced);
        Assert.Equal("Mẫu số 01 (NĐ 398/2026/NĐ-CP)", summary.PitReductionFormCode);
        Assert.Equal(14_150_000m, summary.TotalTaxAmount);
        Assert.Equal(14_150_000m, summary.BooksSummary.TotalTaxObligationsAccrued);
        Assert.Equal("01/CNKD", preview.FormCode);
        Assert.Equal("Mẫu số 01 (NĐ 398/2026/NĐ-CP)", preview.PitReductionFormCode);
    }

    [Fact]
    public async Task Golden_2026_Cross3BInQ2_StaysRevenuePercentageInQ3AndQ4_Gap04()
    {
        using var db = CreateInMemoryDb();
        var (tenant, branch, _, _, standardProd, _) = SeedBaseData(db);
        // Q1 = 800M (exempt), Q2 = +2.7B (YTD 3.5B > 3B), Q3 = +1.0B (YTD 4.5B)
        db.Orders.Add(CreatePaidOrder(tenant.Id, branch.Id, "DH-Q1", new DateTime(2026, 2, 10, 5, 0, 0, DateTimeKind.Utc), standardProd, 2000m, 400_000m, 300_000m, 0.01m));
        db.Orders.Add(CreatePaidOrder(tenant.Id, branch.Id, "DH-Q2", new DateTime(2026, 5, 10, 5, 0, 0, DateTimeKind.Utc), standardProd, 6750m, 400_000m, 300_000m, 0.01m));
        db.Orders.Add(CreatePaidOrder(tenant.Id, branch.Id, "DH-Q3", new DateTime(2026, 8, 10, 5, 0, 0, DateTimeKind.Utc), standardProd, 2500m, 400_000m, 300_000m, 0.01m));

        // Seed finalized Q2 snapshot (which crossed 3B mid-year while staying RevenuePercentage)
        db.HouseholdTaxSnapshots.Add(new HouseholdTaxSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 2,
            AnnualAccumulatedRevenue = 3_500_000_000m,
            HouseholdGroup = HouseholdTaxGroup.Group3_3BTo50B,
            AppliedPitMethod = TaxCalculationMethod.RevenuePercentage,
            TaxableRevenueInPeriod = 2_700_000_000m,
            VatRate = 1.0m,
            VatAmount = 35_000_000m,
            PitRate = 0.5m,
            PitAmount = 8_750_000m,
            TotalTaxAmount = 43_750_000m,
            TaxPolicyJson = JsonSerializer.Serialize(new TaxSnapshotPolicy(
                TaxPolicy.Default,
                new TaxMethodLock(2027, 2028, TaxCalculationMethod.DeclarationBased, "REVENUE_THRESHOLD_CROSSED"))),
            Status = TaxSnapshotStatus.Finalized,
            FinalizedBy = "owner-1",
            FinalizedAtUtc = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var q3Summary = await service.GetTaxSummaryAsync(new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 3,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage
        });

        // Must stay RevenuePercentage for the remainder of 2026 (NOT flip to DeclarationBased mid-year!)
        Assert.Equal(TaxCalculationMethod.RevenuePercentage, q3Summary.CalculationMethod);
        Assert.Equal(2027, q3Summary.DeclarationMethodStartYear);
        // Q3 filing deadline: Oct 31, 2026 is Saturday -> rolls to Monday Nov 2, 2026
        Assert.Equal(new DateTime(2026, 11, 2), q3Summary.FilingDeadlineLocal.Date);
    }

    [Fact]
    public async Task Golden_2027_Law20Nov2026_5B_CanChooseRevenuePercentage_AndVatReductionExpired()
    {
        using var db = CreateInMemoryDb();
        var (tenant, branch, _, reducedProd, _, _) = SeedBaseData(db);
        // 2027 revenue = 5B (within 1B..10B choice threshold from 2027 under Law passed 20/11/2026)
        db.Orders.Add(CreatePaidOrder(tenant.Id, branch.Id, "DH-2027", new DateTime(2027, 3, 10, 5, 0, 0, DateTimeKind.Utc), reducedProd, 20_000m, 250_000m, 200_000m, 0.01m));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var summary = await service.GetTaxSummaryAsync(new HouseholdTaxFilterDto
        {
            Year = 2027,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage,
            ApplyVatReduction2026 = true // Must be ignored in 2027 (expired 31/12/2026)
        });

        Assert.Equal(10_000_000_000m, summary.EffectiveMethodSwitchThreshold);
        Assert.Equal(HouseholdTaxGroup.Group2_1BTo3B, summary.HouseholdGroup);
        Assert.Equal(TaxCalculationMethod.RevenuePercentage, summary.CalculationMethod);
        Assert.False(summary.IsVatReduced);
        Assert.Equal(50_000_000m, summary.VatAmount); // 5B * 1.0% (no 0.8% in 2027)
        // PIT 30% reduction STILL applies in 2027 (<= 10B): (5B - 1B) * 0.5% * 70% = 14M
        Assert.True(summary.IsPitReduced);
        Assert.Equal(14_000_000m, summary.PitAmount);
    }

    [Fact]
    public async Task Golden_2026_DeclarationBased_5B_QuarterlyProvisionalVsAnnualSettlement_Gap11()
    {
        using var db = CreateInMemoryDb();
        var (tenant, branch, _, _, standardProd, _) = SeedBaseData(db);
        // Total 2026 revenue = 5B (5000 units * 1M), documented COGS = 4B (5000 * 800k) -> Profit = 1B
        db.Orders.Add(CreatePaidOrder(tenant.Id, branch.Id, "DH-Q1", new DateTime(2026, 2, 10, 5, 0, 0, DateTimeKind.Utc), standardProd, 1250m, 1_000_000m, 800_000m, 0.01m));
        db.Orders.Add(CreatePaidOrder(tenant.Id, branch.Id, "DH-Q2", new DateTime(2026, 5, 10, 5, 0, 0, DateTimeKind.Utc), standardProd, 1250m, 1_000_000m, 800_000m, 0.01m));
        db.Orders.Add(CreatePaidOrder(tenant.Id, branch.Id, "DH-Q3", new DateTime(2026, 8, 10, 5, 0, 0, DateTimeKind.Utc), standardProd, 1250m, 1_000_000m, 800_000m, 0.01m));
        db.Orders.Add(CreatePaidOrder(tenant.Id, branch.Id, "DH-Q4", new DateTime(2026, 11, 10, 5, 0, 0, DateTimeKind.Utc), standardProd, 1250m, 1_000_000m, 800_000m, 0.01m));
        await db.SaveChangesAsync();

        var service = CreateService(db);

        // Q1 provisional PIT: (1.25B - 1B) * 0.5% * 70% = 875,000 VND
        var q1 = await service.GetTaxSummaryAsync(new HouseholdTaxFilterDto
        {
            Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 1,
            CalculationMethod = TaxCalculationMethod.DeclarationBased
        });
        Assert.True(q1.IsProvisionalPitPeriod);
        Assert.Equal(0.5m, q1.PitRate);
        Assert.Equal(875_000m, q1.PitAmount);

        // Seed finalized Q1-Q4 provisional PIT totaling (5B - 1B) * 0.5% * 70% = 14,000,000 VND
        db.HouseholdTaxSnapshots.Add(new HouseholdTaxSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 4,
            AnnualAccumulatedRevenue = 5_000_000_000m,
            HouseholdGroup = HouseholdTaxGroup.Group3_3BTo50B,
            AppliedPitMethod = TaxCalculationMethod.DeclarationBased,
            PitAmount = 14_000_000m,
            VatAmount = 50_000_000m,
            TotalTaxAmount = 64_000_000m,
            Status = TaxSnapshotStatus.Finalized,
            FinalizedBy = "owner-1",
            FinalizedAtUtc = new DateTime(2027, 1, 10, 0, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        // Annual Settlement (Year 2026): Profit = 1B * 17% = 170M -> 30% NĐ 398 reduction = 119M -> minus 14M provisional = 105M
        var annual = await service.GetTaxSummaryAsync(new HouseholdTaxFilterDto
        {
            Year = 2026, PeriodType = TaxPeriodType.Year, PeriodNumber = 1,
            CalculationMethod = TaxCalculationMethod.DeclarationBased
        });
        Assert.False(annual.IsProvisionalPitPeriod);
        Assert.Equal(17.0m, annual.PitRate);
        Assert.Equal(1_000_000_000m, annual.TaxableIncomeYtd);
        Assert.Equal(170_000_000m, annual.TaxDueYtdBeforeReduction);
        Assert.Equal(51_000_000m, annual.PitReductionYtd);
        Assert.Equal(119_000_000m, annual.TaxDueYtd);
        Assert.Equal(14_000_000m, annual.PriorDeclaredPitTax);
        Assert.Equal(105_000_000m, annual.PitAmount);
        Assert.Equal(new DateTime(2027, 3, 31), annual.FilingDeadlineLocal.Date);
    }

    [Fact]
    public async Task Golden_2026_Exceed10B_ClawbackPitReduction_TriggersForm03_Gap01()
    {
        using var db = CreateInMemoryDb();
        var (tenant, branch, _, _, standardProd, _) = SeedBaseData(db);
        // Q1-Q3 = 9B (eligible <= 10B -> reduced PIT = (9B-1B)*0.5%*70% = 28M); Q4 = +2B -> YTD 11B (> 10B -> full 50M!)
        db.Orders.Add(CreatePaidOrder(tenant.Id, branch.Id, "DH-Q1Q3", new DateTime(2026, 8, 10, 5, 0, 0, DateTimeKind.Utc), standardProd, 9000m, 1_000_000m, 800_000m, 0.01m));
        db.Orders.Add(CreatePaidOrder(tenant.Id, branch.Id, "DH-Q4", new DateTime(2026, 11, 10, 5, 0, 0, DateTimeKind.Utc), standardProd, 2000m, 1_000_000m, 800_000m, 0.01m));
        db.HouseholdTaxSnapshots.Add(new HouseholdTaxSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 3,
            AnnualAccumulatedRevenue = 9_000_000_000m,
            HouseholdGroup = HouseholdTaxGroup.Group3_3BTo50B,
            AppliedPitMethod = TaxCalculationMethod.RevenuePercentage,
            PitAmount = 28_000_000m,
            VatAmount = 90_000_000m,
            TotalTaxAmount = 118_000_000m,
            Status = TaxSnapshotStatus.Finalized,
            FinalizedBy = "owner-1",
            FinalizedAtUtc = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var q4 = await service.GetTaxSummaryAsync(new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 4,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage
        });

        Assert.False(q4.IsPitReduced);
        Assert.True(q4.RequiresPitReductionAdjustmentForm03);
        Assert.Equal("Mẫu số 03 (NĐ 398/2026/NĐ-CP)", q4.PitReductionFormCode);
        // Full YTD PIT = (11B - 1B)*0.5% = 50M; minus prior reduced 28M = 22M (includes 12M clawback!)
        Assert.Equal(50_000_000m, q4.TaxDueYtd);
        Assert.Equal(22_000_000m, q4.PitAmount);
    }

    [Fact]
    public async Task Golden_2026_Art6_ExcludesCashOver5M_OwnerSalary_No70PctCogs_AndFlagsRiskyGrns_Gap12()
    {
        using var db = CreateInMemoryDb();
        var (tenant, branch, supplier, _, standardProd, missingCostProd) = SeedBaseData(db);
        var dateUtc = new DateTime(2026, 5, 10, 5, 0, 0, DateTimeKind.Utc);

        // Order 1: 1.5B with real cost (1B); Order 2: 200M with NO cost (must NOT add 70% = 140M into deductible COGS!)
        db.Orders.Add(CreatePaidOrder(tenant.Id, branch.Id, "DH-REAL", dateUtc, standardProd, 1500m, 1_000_000m, 666_666m, 0.01m));
        db.Orders.Add(CreatePaidOrder(tenant.Id, branch.Id, "DH-NOCOST", dateUtc, missingCostProd, 2000m, 100_000m, 0m, 0.01m));

        // Expenses:
        // 1. Valid Bank rent 15M -> INCLUDED
        // 2. Valid Cash utility 3M (< 5M) -> INCLUDED
        // 3. Invalid Cash rent 10M (>= 5M cash) -> EXCLUDED (Art. 6 Decree 68)
        // 4. Invalid Owner salary 20M (Bank) -> EXCLUDED (Art. 6 Decree 68)
        db.ExpenseVouchers.AddRange(
            new ExpenseVoucher { Id = Guid.NewGuid(), TenantId = tenant.Id, BranchId = branch.Id, VoucherCode = "PC-OK1", ExpenseDate = dateUtc, Category = "Thuê mặt bằng", Amount = 15_000_000m, PaymentSourceType = FinanceAccountType.Bank, Nature = FinancialNature.OperatingExpense, IsTaxDeductible = true, Status = ExpenseVoucherStatus.Paid },
            new ExpenseVoucher { Id = Guid.NewGuid(), TenantId = tenant.Id, BranchId = branch.Id, VoucherCode = "PC-OK2", ExpenseDate = dateUtc, Category = "Tiền điện nước", Amount = 3_000_000m, PaymentSourceType = FinanceAccountType.Till, Nature = FinancialNature.OperatingExpense, IsTaxDeductible = true, Status = ExpenseVoucherStatus.Paid },
            new ExpenseVoucher { Id = Guid.NewGuid(), TenantId = tenant.Id, BranchId = branch.Id, VoucherCode = "PC-CASH5M", ExpenseDate = dateUtc, Category = "Thuê mặt bằng", Amount = 10_000_000m, PaymentSourceType = FinanceAccountType.Safe, Nature = FinancialNature.OperatingExpense, IsTaxDeductible = true, Status = ExpenseVoucherStatus.Paid },
            new ExpenseVoucher { Id = Guid.NewGuid(), TenantId = tenant.Id, BranchId = branch.Id, VoucherCode = "PC-OWNER", ExpenseDate = dateUtc, Category = "Lương chủ hộ", Amount = 20_000_000m, PaymentSourceType = FinanceAccountType.Bank, Nature = FinancialNature.OperatingExpense, IsTaxDeductible = true, Status = ExpenseVoucherStatus.Paid }
        );

        // GoodsReceipts:
        // 1. Compliant GRN: 12M BANK with InvoiceNo -> NOT flagged
        // 2. Risky GRN: 8M CASH & missing InvoiceNo -> FLAGGED
        db.GoodsReceipts.AddRange(
            new GoodsReceipt { Id = Guid.NewGuid(), TenantId = tenant.Id, BranchId = branch.Id, SupplierId = supplier.Id, Code = "PN-OK", Status = "COMPLETED", ReceivedAtUtc = dateUtc, GrandTotal = 12_000_000m, PaymentMethod = "BANK", InvoiceNo = "HD-001" },
            new GoodsReceipt { Id = Guid.NewGuid(), TenantId = tenant.Id, BranchId = branch.Id, SupplierId = supplier.Id, Code = "PN-RISK", Status = "COMPLETED", ReceivedAtUtc = dateUtc, GrandTotal = 8_000_000m, PaymentMethod = "CASH", InvoiceNo = null }
        );
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var summary = await service.GetTaxSummaryAsync(new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 2,
            CalculationMethod = TaxCalculationMethod.DeclarationBased
        });

        // Documented COGS ONLY: 1500 * 666,666 = 999,999,000 (NOT +140M fake 70% COGS!)
        Assert.Equal(999_999_000m, summary.CogsAmount);
        Assert.True(summary.IsCogsEstimated);
        Assert.Equal(140_000_000m, summary.EstimatedCogsWarningAmount);

        // Valid OPEX ONLY: 15M + 3M = 18M (10M cash >= 5M and 20M owner salary excluded!)
        Assert.Equal(18_000_000m, summary.OpexAmount);
        Assert.Equal(10_000_000m, summary.ExcludedCashExpensesAmount);
        Assert.Equal(1, summary.ExcludedCashExpensesCount);
        Assert.Equal(20_000_000m, summary.ExcludedOwnerSalaryAmount);
        Assert.Equal(1, summary.ExcludedOwnerSalaryCount);

        // Risky GoodsReceipts detected
        var risky = Assert.Single(summary.RiskyGoodsReceipts);
        Assert.Equal("PN-RISK", risky.Code);
        Assert.Contains("5,000,000", risky.RiskReason.Replace(".", ","));
        Assert.Contains("InvoiceNo", risky.RiskReason);
    }

    [Fact]
    public void Golden_2027_PosOrderService_AutoRestores1PctVatAtMidnightVietnamNewYear_Gap06()
    {
        var policy = TaxPolicy.Default;
        var beforeMidnightVnInUtc = new DateTime(2026, 12, 31, 16, 59, 59, DateTimeKind.Utc); // 23:59:59 VN 31/12/2026
        var atMidnightVnInUtc = new DateTime(2026, 12, 31, 17, 0, 0, DateTimeKind.Utc);      // 00:00:00 VN 01/01/2027

        Assert.Equal(0.008m, OrderService.ResolveEffectiveProductVatRate(0.008m, beforeMidnightVnInUtc, policy));
        Assert.Equal(0.01m, OrderService.ResolveEffectiveProductVatRate(0.008m, atMidnightVnInUtc, policy));
        Assert.Equal(0.01m, OrderService.ResolveEffectiveProductVatRate(0.01m, beforeMidnightVnInUtc, policy));
    }

    [Fact]
    public async Task Golden_2026_ExcelExportReconciliation_S2aNoDoubleCount_S3aZeroLicenseFeeAndPaidVouchers_TT152Date()
    {
        using var db = CreateInMemoryDb();
        var (tenant, branch, _, reducedProd, standardProd, _) = SeedBaseData(db);
        var q1Date = new DateTime(2026, 2, 10, 5, 0, 0, DateTimeKind.Utc);
        var q2Date = new DateTime(2026, 5, 10, 5, 0, 0, DateTimeKind.Utc);

        // Q1: 1.0B standard; Q2: 300M reduced (0.8%) + 200M standard (1.0%) -> Q2 period revenue = 500M
        db.Orders.Add(CreatePaidOrder(tenant.Id, branch.Id, "DH-Q1", q1Date, standardProd, 2500m, 400_000m, 300_000m, 0.01m));
        db.Orders.Add(CreatePaidOrder(tenant.Id, branch.Id, "DH-Q2-RED", q2Date, reducedProd, 1200m, 250_000m, 200_000m, 0.008m));
        db.Orders.Add(CreatePaidOrder(tenant.Id, branch.Id, "DH-Q2-STD", q2Date, standardProd, 500m, 400_000m, 300_000m, 0.01m));

        // Seed real tax payment vouchers in Q2 (VAT 4M, PIT 1M)
        db.ExpenseVouchers.AddRange(
            new ExpenseVoucher { Id = Guid.NewGuid(), TenantId = tenant.Id, BranchId = branch.Id, VoucherCode = "GNT-VAT-01", ExpenseDate = q2Date, Category = "Nộp thuế GTGT NSNN", Amount = 4_000_000m, PaymentSourceType = FinanceAccountType.Bank, Nature = FinancialNature.TaxPayment, IsTaxDeductible = false, Status = ExpenseVoucherStatus.Paid },
            new ExpenseVoucher { Id = Guid.NewGuid(), TenantId = tenant.Id, BranchId = branch.Id, VoucherCode = "GNT-PIT-01", ExpenseDate = q2Date, Category = "Nộp thuế TNCN NSNN", Amount = 1_000_000m, PaymentSourceType = FinanceAccountType.Bank, Nature = FinancialNature.TaxPayment, IsTaxDeductible = false, Status = ExpenseVoucherStatus.Paid }
        );
        await db.SaveChangesAsync();

        var repo = new HouseholdTaxRepository(db);
        var service = CreateService(db);
        var exportService = new TaxExportService(service, repo, db, new OwnerTenantContext());
        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 2,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage,
            ApplyVatReduction2026 = true
        };

        var summary = await service.GetTaxSummaryAsync(filter);
        var bytes = await exportService.ExportTaxSummaryToExcelAsync(filter, null, CancellationToken.None);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);

        // 1. Verify Circular 152 issue date fixed to 31/12/2025 (GAP-08)
        var s2a = wb.Worksheet("S2a-HKD");
        Assert.Contains("31/12/2025", s2a.Cell("D4").GetString());
        Assert.DoesNotContain("23/12/2025", s2a.Cell("D4").GetString());

        // 2. Verify S2a-HKD has ZERO double counting: Standard rev = 200M, Reduced rev = 300M, Total = 500M!
        var secTotalRows = s2a.RowsUsed().Where(r => r.Cell(1).GetString().Contains("Tổng cộng doanh thu ngành (1)")).ToList();
        Assert.Equal(2, secTotalRows.Count);
        var sumOfSectionRevs = secTotalRows.Sum(r => r.Cell(4).GetValue<decimal>());
        Assert.Equal(500_000_000m, sumOfSectionRevs);

        // S2a total PIT reconciles 100% with summary.PitAmount (1.75M after 30% NĐ 398 reduction)
        var s2aTotalPitRow = s2a.RowsUsed().First(r => r.Cell(1).GetString().Contains("TỔNG SỐ THUẾ TNCN PHẢI NỘP TOÀN BỘ"));
        Assert.Equal(summary.PitAmount, s2aTotalPitRow.Cell(4).GetValue<decimal>());

        // 3. Verify S3a-HKD: License fee == 0 for 2026 (GAP-02) AND real paid vouchers wired into cols 6-8 (GAP-08)
        var s3a = wb.Worksheet("S3a-HKD");
        var licenseRow = s3a.RowsUsed().First(r => r.Cell(2).GetString().Contains("Lệ phí môn bài"));
        Assert.Equal(0m, licenseRow.Cell(4).GetValue<decimal>());
        Assert.Contains("NQ 198/2025", licenseRow.Cell(12).GetString());

        var vatRow = s3a.RowsUsed().First(r => r.Cell(2).GetString().Contains("Thuế Giá trị gia tăng"));
        Assert.Contains("GNT-VAT-01", vatRow.Cell(6).GetString());
        Assert.Equal(4_000_000m, vatRow.Cell(8).GetValue<decimal>());

        var pitRow = s3a.RowsUsed().First(r => r.Cell(2).GetString().Contains("Thuế Thu nhập cá nhân"));
        Assert.Contains("GNT-PIT-01", pitRow.Cell(6).GetString());
        Assert.Equal(1_000_000m, pitRow.Cell(8).GetValue<decimal>());
    }

    [Fact]
    public void Golden_TaxPolicy_LoadsStructuredJsonSection_AndPreservesLegacySignatureVerification()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TaxSettings:Policy:Version"] = "2026-2027-custom",
                ["TaxSettings:Policy:ExemptionThreshold"] = "1000000000",
                ["TaxSettings:Policy:MethodSwitchRevenueThreshold"] = "3000000000",
                ["TaxSettings:Policy:MethodSwitchThresholdFrom2027"] = "10000000000",
                ["TaxSettings:Policy:MethodLockYears"] = "2",
                ["TaxSettings:Policy:VatReductionDeadlineUtc"] = "2026-12-31T17:00:00Z",
                ["TaxSettings:Policy:CashlessExpenseThreshold"] = "5000000",
                ["TaxSettings:Policy:PitReductionPercentage"] = "30",
                ["TaxSettings:Policy:PitReductionMaxRevenue"] = "10000000000"
            })
            .Build();

        var loaded = TaxPolicy.Load(config);
        Assert.Equal("2026-2027-custom", loaded.Version);
        Assert.Equal(3_000_000_000m, loaded.GetMethodSwitchThreshold(2026));
        Assert.Equal(10_000_000_000m, loaded.GetMethodSwitchThreshold(2027));
        Assert.True(loaded.IsLicenseFeeAbolished(2026));
        Assert.False(loaded.IsLicenseFeeAbolished(2025));

        // Legacy 5-field TaxPolicyJson must round-trip without adding extra properties so historic HMAC signatures stay valid
        var legacyJson = """{"Rules":{"Version":"2026-nd68-nd141","ExemptionThreshold":1000000000,"MethodSwitchRevenueThreshold":3000000000,"MethodLockYears":2,"VatReductionDeadlineUtc":"2026-12-31T17:00:00Z"},"MethodLock":null}""";
        var deserialized = JsonSerializer.Deserialize<TaxSnapshotPolicy>(legacyJson);
        Assert.NotNull(deserialized?.Rules);
        var reserialized = JsonSerializer.Serialize(deserialized);
        Assert.DoesNotContain("PitReductionPercentage", reserialized);
    }
}
