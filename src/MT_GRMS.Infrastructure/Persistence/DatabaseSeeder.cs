using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MT_GRMS.Domain.Entities;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.Supplier;
using Promotion = MT_GRMS.Domain.Entities.Sales.Promotion;
using PromotionCondition = MT_GRMS.Domain.Entities.Sales.PromotionCondition;
using VoucherCode = MT_GRMS.Domain.Entities.Sales.VoucherCode;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Infrastructure.Persistence;

/// <summary>
/// Service nạp dữ liệu mẫu khởi tạo (Database Seeder) cho MT-GRMS:
/// Khởi tạo User chủ tiệm, danh mục, sản phẩm phân loại thuế, đơn hàng thực tế và phiếu chi.
/// </summary>
public static class DatabaseSeeder
{
    private const string DefaultTenantId = "tenant_mt_mart_01";

    public static async Task SeedAsync(AppDbContext context, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            // Migration FE-10: Bổ sung cột CostPriceAtSale và Backfill giá vốn đơn hàng
            try
            {
                await context.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE \"OrderDetails\" ADD COLUMN IF NOT EXISTS \"CostPriceAtSale\" numeric(18,4) NOT NULL DEFAULT 0.0000;",
                    ct);

                await context.Database.ExecuteSqlRawAsync(
                    "UPDATE \"OrderDetails\" od " +
                    "SET \"CostPriceAtSale\" = COALESCE(p.\"CostPrice\", od.\"UnitPrice\" * 0.70) " +
                    "FROM \"Products\" p " +
                    "WHERE od.\"ProductID\" = p.\"ProductID\" AND od.\"CostPriceAtSale\" = 0;",
                    ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "CostPriceAtSale migration check passed or already applied.");
            }

            // 0. Seed SaaS Tenant & Default Branch for Grocery Store
            if (!await context.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == DefaultTenantId, ct))
            {
                logger.LogInformation("Seeding SaaS Tenant for Grocery Store...");
                var groceryTenant = new Domain.Entities.SaaS.Tenant
                {
                    Id = DefaultTenantId,
                    Name = "Hộ Kinh Doanh Tạp Hóa Siêu Thị MT-GRMS",
                    Code = "GROCERY-001",
                    Subdomain = "grocery001",
                    TaxCode = "0318999888",
                    Status = "ACTIVE",
                    Address = "12 Nguyễn Huệ, Phường Bến Nghé, Quận 1, TP.HCM",
                    City = "Hồ Chí Minh",
                    CurrentPlan = "PRO",
                    SubscriptionExpiresAt = DateTime.UtcNow.AddYears(1)
                };
                await context.Tenants.AddAsync(groceryTenant, ct);
                await context.SaveChangesAsync(ct);
            }

            var groceryBranchId = Guid.Parse("cd5e8d1b-b08b-448d-beff-b85ea5873d52");
            if (!await context.Branches.IgnoreQueryFilters().AnyAsync(b => b.Id == groceryBranchId, ct))
            {
                logger.LogInformation("Seeding Default Branch for Grocery Store...");
                var branch = new Domain.Entities.SaaS.Branch
                {
                    Id = groceryBranchId,
                    TenantId = DefaultTenantId,
                    BranchCode = "BR-Q1-GROCERY",
                    Name = "Cửa hàng Tạp hóa Chi nhánh Quận 1",
                    Address = "12 Nguyễn Huệ, Quận 1, TP.HCM",
                    IsActive = true
                };
                await context.Branches.AddAsync(branch, ct);
                await context.SaveChangesAsync(ct);
            }

            // 1. Seed Users (Chủ tiệm với mã PIN 1234 đã băm PBKDF2)
            if (!await context.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == "user-owner-001", ct))
            {
                logger.LogInformation("Seeding Users...");
                var saltBytes = RandomNumberGenerator.GetBytes(16);
                var pinHashBytes = Rfc2898DeriveBytes.Pbkdf2(
                    "1234",
                    saltBytes,
                    600_000,
                    HashAlgorithmName.SHA256,
                    32
                );

                var passwordHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Owner@123456"))).ToLowerInvariant();
                var staffPasswordHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Staff@123456"))).ToLowerInvariant();

                var owner = new User
                {
                    Id = "user-owner-001",
                    UserName = "store_owner",
                    FullName = "Chủ Cửa Hàng",
                    Email = "owner@mtgrms.vn",
                    Password = passwordHash,
                    SecurityPinSalt = Convert.ToBase64String(saltBytes),
                    SecurityPinHash = Convert.ToBase64String(pinHashBytes),
                    FailedPinAttempts = 0
                };

                var cashier = new User
                {
                    Id = "user-cashier-001",
                    UserName = "staff_cashier",
                    FullName = "Nhân Viên Thu Ngân",
                    Email = "cashier@mtgrms.vn",
                    Password = staffPasswordHash
                };

                var employee = new User
                {
                    Id = "user-employee-001",
                    UserName = "staff_employee",
                    FullName = "Nhân Viên Cửa Hàng (Employee)",
                    Email = "employee@mtgrms.vn",
                    Password = staffPasswordHash
                };

                await context.Users.AddRangeAsync(new[] { owner, cashier, employee }, ct);
                await context.SaveChangesAsync(ct);
            }

            // 2. Seed Categories
            if (!await context.Categories.IgnoreQueryFilters().AnyAsync(c => c.Id == Guid.Parse("11111111-1111-1111-1111-111111111111"), ct))
            {
                logger.LogInformation("Seeding Categories...");
                var fmcgCategory = new Category
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    TenantId = DefaultTenantId,
                    CategoryCode = "FMCG",
                    CategoryName = "Hàng tiêu dùng nhanh (Bánh kẹo, mì gói, nước ngọt)"
                };

                var alcoholCategory = new Category
                {
                    Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    TenantId = DefaultTenantId,
                    CategoryCode = "ALCOHOL",
                    CategoryName = "Bia, Rượu, Đồ uống có cồn (Thuế TTĐB)"
                };

                var tobaccoCategory = new Category
                {
                    Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    TenantId = DefaultTenantId,
                    CategoryCode = "TOBACCO",
                    CategoryName = "Thuốc lá, Xì gà (Thuế TTĐB)"
                };

                await context.Categories.AddRangeAsync(new[] { fmcgCategory, alcoholCategory, tobaccoCategory }, ct);
                await context.SaveChangesAsync(ct);
            }

            // 3. Seed Products (Gắn TaxCategory theo NQ 204: STANDARD_1PCT vs NO_REDUCTION_1PCT)
            if (!await context.Products.IgnoreQueryFilters().AnyAsync(p => p.Id == Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"), ct))
            {
                logger.LogInformation("Seeding Products with TaxCategory...");
                var products = new List<Product>
                {
                    new Product
                    {
                        Id = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"),
                        TenantId = DefaultTenantId,
                        SKU = "FMCG-001",
                        Barcode = "893603601001",
                        ProductName = "Bánh Chocopie Orion hộp 12 cái (360g)",
                        CategoryId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        BasePrice = 65000m,
                        CostPrice = 48000m,
                        TaxCategory = TaxCategoryConstants.Standard1Pct, // Được giảm GTGT còn 0.8%
                        UnitName = "Hộp"
                    },
                    new Product
                    {
                        Id = Guid.Parse("aaaaaaaa-2222-2222-2222-222222222222"),
                        TenantId = DefaultTenantId,
                        SKU = "FMCG-002",
                        Barcode = "893456302002",
                        ProductName = "Mì tôm Hảo Hảo tôm chua cay thùng 30 gói",
                        CategoryId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        BasePrice = 135000m,
                        CostPrice = 110000m,
                        TaxCategory = TaxCategoryConstants.Standard1Pct, // Được giảm GTGT còn 0.8%
                        UnitName = "Thùng"
                    },
                    new Product
                    {
                        Id = Guid.Parse("aaaaaaaa-3333-3333-3333-333333333333"),
                        TenantId = DefaultTenantId,
                        SKU = "FMCG-003",
                        Barcode = "893504950003",
                        ProductName = "Nước ngọt Coca-Cola lốc 6 lon 320ml",
                        CategoryId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        BasePrice = 58000m,
                        CostPrice = 45000m,
                        TaxCategory = TaxCategoryConstants.Standard1Pct, // Được giảm GTGT còn 0.8%
                        UnitName = "Lốc"
                    },
                    new Product
                    {
                        Id = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111"),
                        TenantId = DefaultTenantId,
                        SKU = "ALC-001",
                        Barcode = "893482221004",
                        ProductName = "Bia Tiger lon thùng 24 lon 330ml",
                        CategoryId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                        BasePrice = 385000m,
                        CostPrice = 340000m,
                        TaxCategory = TaxCategoryConstants.NoReduction1Pct, // Hàng TTĐB: Giữ nguyên 1.0%
                        UnitName = "Thùng"
                    },
                    new Product
                    {
                        Id = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222"),
                        TenantId = DefaultTenantId,
                        SKU = "ALC-002",
                        Barcode = "893482221005",
                        ProductName = "Bia Heineken bạc thùng 24 lon 330ml",
                        CategoryId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                        BasePrice = 440000m,
                        CostPrice = 395000m,
                        TaxCategory = TaxCategoryConstants.NoReduction1Pct, // Hàng TTĐB: Giữ nguyên 1.0%
                        UnitName = "Thùng"
                    },
                    new Product
                    {
                        Id = Guid.Parse("cccccccc-1111-1111-1111-111111111111"),
                        TenantId = DefaultTenantId,
                        SKU = "TOB-001",
                        Barcode = "893601111006",
                        ProductName = "Thuốc lá Craven A đỏ bao mềm",
                        CategoryId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                        BasePrice = 32000m,
                        CostPrice = 28000m,
                        TaxCategory = TaxCategoryConstants.NoReduction1Pct, // Hàng TTĐB: Giữ nguyên 1.0%
                        UnitName = "Gói"
                    }
                };

                await context.Products.AddRangeAsync(products, ct);
                await context.SaveChangesAsync(ct);
            }

            // Resolve default branch for Orders & Expenses foreign key
            var defaultBranch = await context.Branches.IgnoreQueryFilters().FirstOrDefaultAsync(ct);
            var branchId = defaultBranch?.Id ?? Guid.Parse("99999999-9999-9999-9999-999999999999");
            if (defaultBranch == null)
            {
                defaultBranch = new Domain.Entities.SaaS.Branch
                {
                    Id = branchId,
                    TenantId = DefaultTenantId,
                    BranchCode = "BR-DEFAULT",
                    Name = "Chi Nhánh Mặc Định",
                    IsActive = true
                };
                await context.Branches.AddAsync(defaultBranch, ct);
                await context.SaveChangesAsync(ct);
            }

            // 4. Seed Orders & OrderItems (Phát sinh trong Q3/2026: 01/07/2026 - 30/09/2026)
            if (!await context.Orders.IgnoreQueryFilters().AnyAsync(o => o.Id == Guid.Parse("f0000001-1111-1111-1111-111111111111"), ct))
            {
                logger.LogInformation("Seeding Orders and OrderItems...");

                // Đơn hàng 1: Ngày 15/07/2026 - Hàng thường (Được giảm GTGT 0.8%)
                var order1 = new Order
                {
                    Id = Guid.Parse("f0000001-1111-1111-1111-111111111111"),
                    TenantId = DefaultTenantId,
                    OrderCode = "ORD-20260715-001",
                    BranchId = branchId,
                    SubTotal = 465000m,
                    DiscountAmount = 0m,
                    TotalAmount = 465000m,
                    PaymentMethod = "CASH",
                    PaymentStatus = "PAID",
                    CreatedAt = new DateTime(2026, 7, 15, 10, 30, 0, DateTimeKind.Utc)
                };

                order1.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order1.Id,
                    ProductId = Guid.Parse("aaaaaaaa-2222-2222-2222-222222222222"), // Mì Hảo Hảo
                    ProductName = "Mì tôm Hảo Hảo tôm chua cay thùng 30 gói",
                    UnitPrice = 135000m,
                    Quantity = 2m,
                    TotalLineAmount = 270000m,
                    AppliedVatRate = 0.008m, // Giảm 20%
                    VatAmount = 2160m, // 270.000 * 0.8%
                    VatDiscountAmount = 540m
                });

                order1.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order1.Id,
                    ProductId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"), // Chocopie
                    ProductName = "Bánh Chocopie Orion hộp 12 cái (360g)",
                    UnitPrice = 65000m,
                    Quantity = 3m,
                    TotalLineAmount = 195000m,
                    AppliedVatRate = 0.008m, // Giảm 20%
                    VatAmount = 1560m,
                    VatDiscountAmount = 390m
                });

                // Đơn hàng 2: Ngày 20/08/2026 - Hỗn hợp (Hàng thường 0.8% + Bia/Thuốc lá 1.0%)
                var order2 = new Order
                {
                    Id = Guid.Parse("f0000002-2222-2222-2222-222222222222"),
                    TenantId = DefaultTenantId,
                    OrderCode = "ORD-20260820-002",
                    BranchId = branchId,
                    SubTotal = 565000m,
                    DiscountAmount = 0m,
                    TotalAmount = 565000m,
                    PaymentMethod = "BANK_TRANSFER",
                    PaymentStatus = "PAID",
                    CreatedAt = new DateTime(2026, 8, 20, 15, 45, 0, DateTimeKind.Utc)
                };

                order2.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order2.Id,
                    ProductId = Guid.Parse("aaaaaaaa-3333-3333-3333-333333333333"), // Coca
                    ProductName = "Nước ngọt Coca-Cola lốc 6 lon 320ml",
                    UnitPrice = 58000m,
                    Quantity = 2m,
                    TotalLineAmount = 116000m,
                    AppliedVatRate = 0.008m, // Giảm 20%
                    VatAmount = 928m,
                    VatDiscountAmount = 232m
                });

                order2.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order2.Id,
                    ProductId = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111"), // Bia Tiger
                    ProductName = "Bia Tiger lon thùng 24 lon 330ml",
                    UnitPrice = 385000m,
                    Quantity = 1m,
                    TotalLineAmount = 385000m,
                    AppliedVatRate = 0.010m, // Hàng TTĐB: 1.0%
                    VatAmount = 3850m,
                    VatDiscountAmount = 0m
                });

                order2.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order2.Id,
                    ProductId = Guid.Parse("cccccccc-1111-1111-1111-111111111111"), // Thuốc lá
                    ProductName = "Thuốc lá Craven A đỏ bao mềm",
                    UnitPrice = 32000m,
                    Quantity = 2m,
                    TotalLineAmount = 64000m,
                    AppliedVatRate = 0.010m, // Hàng TTĐB: 1.0%
                    VatAmount = 640m,
                    VatDiscountAmount = 0m
                });

                // Đơn hàng 3: Ngày 10/09/2026 - Doanh số bán buôn giá trị cao
                var order3 = new Order
                {
                    Id = Guid.Parse("f0000003-3333-3333-3333-333333333333"),
                    TenantId = DefaultTenantId,
                    OrderCode = "ORD-20260910-003",
                    BranchId = branchId,
                    SubTotal = 385000000m, // 385 triệu doanh thu
                    DiscountAmount = 0m,
                    TotalAmount = 385000000m,
                    PaymentMethod = "BANK_TRANSFER",
                    PaymentStatus = "PAID",
                    CreatedAt = new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc)
                };

                order3.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order3.Id,
                    ProductId = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111"), // Bia Tiger
                    ProductName = "Bia Tiger lon thùng 24 lon 330ml",
                    UnitPrice = 385000m,
                    Quantity = 1000m,
                    TotalLineAmount = 385000000m,
                    AppliedVatRate = 0.010m,
                    VatAmount = 3850000m,
                    VatDiscountAmount = 0m
                });

                await context.Orders.AddRangeAsync(new[] { order1, order2, order3 }, ct);
                await context.SaveChangesAsync(ct);
            }

            // 5. Seed Expense Vouchers (Phiếu chi chi phí mặt bằng, điện nước cho sổ chi phí S2c, S2e)
            if (!await context.ExpenseVouchers.IgnoreQueryFilters().AnyAsync(e => e.VoucherCode == "PC-202607-001", ct))
            {
                logger.LogInformation("Seeding Expense Vouchers...");
                var expenses = new List<ExpenseVoucher>
                {
                    new ExpenseVoucher
                    {
                        Id = Guid.NewGuid(),
                        TenantId = DefaultTenantId,
                        VoucherCode = "PC-202607-001",
                        Category = "Tiền thuê mặt bằng kinh doanh T7/2026",
                        Note = "Thanh toán tiền thuê mặt bằng tạp hóa tháng 7/2026",
                        Amount = 15000000m,
                        BeneficiaryName = "Nguyễn Văn Chủ Nhà",
                        BranchId = branchId,
                        Status = ExpenseVoucherStatus.Approved,
                        IsTaxDeductible = true,
                        ExpenseDate = new DateTime(2026, 7, 5, 8, 0, 0, DateTimeKind.Utc),
                        CreatedAt = new DateTime(2026, 7, 5, 8, 0, 0, DateTimeKind.Utc)
                    },
                    new ExpenseVoucher
                    {
                        Id = Guid.NewGuid(),
                        TenantId = DefaultTenantId,
                        VoucherCode = "PC-202608-001",
                        Category = "Tiền thuê mặt bằng kinh doanh T8/2026",
                        Note = "Thanh toán tiền thuê mặt bằng tạp hóa tháng 8/2026",
                        Amount = 15000000m,
                        BeneficiaryName = "Nguyễn Văn Chủ Nhà",
                        BranchId = branchId,
                        Status = ExpenseVoucherStatus.Approved,
                        IsTaxDeductible = true,
                        ExpenseDate = new DateTime(2026, 8, 5, 8, 0, 0, DateTimeKind.Utc),
                        CreatedAt = new DateTime(2026, 8, 5, 8, 0, 0, DateTimeKind.Utc)
                    },
                    new ExpenseVoucher
                    {
                        Id = Guid.NewGuid(),
                        TenantId = DefaultTenantId,
                        VoucherCode = "PC-202609-001",
                        Category = "Tiền thuê mặt bằng kinh doanh T9/2026",
                        Note = "Thanh toán tiền thuê mặt bằng tạp hóa tháng 9/2026",
                        Amount = 15000000m,
                        BeneficiaryName = "Nguyễn Văn Chủ Nhà",
                        BranchId = branchId,
                        Status = ExpenseVoucherStatus.Approved,
                        IsTaxDeductible = true,
                        ExpenseDate = new DateTime(2026, 9, 5, 8, 0, 0, DateTimeKind.Utc),
                        CreatedAt = new DateTime(2026, 9, 5, 8, 0, 0, DateTimeKind.Utc)
                    },
                    new ExpenseVoucher
                    {
                        Id = Guid.NewGuid(),
                        TenantId = DefaultTenantId,
                        VoucherCode = "PC-202609-002",
                        Category = "Tiền điện, nước, Internet Q3/2026",
                        Note = "Hóa đơn EVN & VNPT chi nhánh",
                        Amount = 6500000m,
                        BeneficiaryName = "Điện lực & Viễn thông",
                        BranchId = branchId,
                        Status = ExpenseVoucherStatus.Approved,
                        IsTaxDeductible = true,
                        ExpenseDate = new DateTime(2026, 9, 25, 14, 0, 0, DateTimeKind.Utc),
                        CreatedAt = new DateTime(2026, 9, 25, 14, 0, 0, DateTimeKind.Utc)
                    }
                };

                await context.ExpenseVouchers.AddRangeAsync(expenses, ct);
                await context.SaveChangesAsync(ct);
            }
            // 6. Seed Promotions and Vouchers
            await SeedPromotionsAsync(context, DefaultTenantId, logger, ct);
            if (DefaultTenantId != "default-tenant")
            {
                await SeedPromotionsAsync(context, "default-tenant", logger, ct);
            }


            // 6. Seed Additional Products & Supplier Linkages for Order Matrix
            if (!await context.SupplierProducts.IgnoreQueryFilters().AnyAsync(sp => sp.SupplierProductCode == "SP-MI-HAOHAO-TC", ct))
            {
                logger.LogInformation("Seeding 20 Additional Grocery Products with Suppliers and Stocks...");

                // Lấy hoặc tạo các Nhà Cung Cấp
                var supAcecook = await context.Suppliers.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Code == "SUP-ACECOOK", ct);
                if (supAcecook == null)
                {
                    supAcecook = new Domain.Entities.Supplier.Supplier
                    {
                        TenantId = DefaultTenantId,
                        Code = "SUP-ACECOOK",
                        Name = "Công Ty Cổ Phần Acecook Việt Nam",
                        SupplierType = SupplierType.Goods,
                        Status = SupplierStatus.Active,
                        TaxCode = "0300808687",
                        Phone = "02838154064",
                        Email = "sales@acecookvietnam.vn",
                        Address = "Lô II-3, Đường số 11, KCN Tân Bình, Tây Thạnh, Tân Phú, TP.HCM",
                        ContactPerson = "Nguyễn Văn Hậu",
                        PaymentTermDays = 15,
                        CreditLimit = 150000000m
                    };
                    await context.Suppliers.AddAsync(supAcecook, ct);
                }

                var supMasan = await context.Suppliers.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Code == "SUP-MASAN", ct);
                if (supMasan == null)
                {
                    supMasan = new Domain.Entities.Supplier.Supplier
                    {
                        TenantId = DefaultTenantId,
                        Code = "SUP-MASAN",
                        Name = "Công Ty Cổ Phần Hàng Tiêu Dùng Masan (Masan Consumer)",
                        SupplierType = SupplierType.Goods,
                        Status = SupplierStatus.Active,
                        TaxCode = "0302017440",
                        Phone = "02862555660",
                        Email = "contact@masangroup.com",
                        Address = "Tầng 12, Tòa nhà MPlaza Saigon, 39 Lê Duẩn, Bến Nghé, Quận 1, TP.HCM",
                        ContactPerson = "Trần Thị Mai",
                        PaymentTermDays = 30,
                        CreditLimit = 250000000m
                    };
                    await context.Suppliers.AddAsync(supMasan, ct);
                }

                var supOrion = await context.Suppliers.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Code == "SUP-ORION", ct);
                if (supOrion == null)
                {
                    supOrion = new Domain.Entities.Supplier.Supplier
                    {
                        TenantId = DefaultTenantId,
                        Code = "SUP-ORION",
                        Name = "Công Ty TNHH Thực Phẩm Orion Vina",
                        SupplierType = SupplierType.Goods,
                        Status = SupplierStatus.Active,
                        TaxCode = "3700685000",
                        Phone = "02743787999",
                        Email = "orionvina@orionworld.com",
                        Address = "KCN Mỹ Phước II, Bến Cát, Bình Dương",
                        ContactPerson = "Lê Hoàng Long",
                        PaymentTermDays = 20,
                        CreditLimit = 180000000m
                    };
                    await context.Suppliers.AddAsync(supOrion, ct);
                }

                var supUnilever = await context.Suppliers.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Code == "SUP-UNILEVER", ct);
                if (supUnilever == null)
                {
                    supUnilever = new Domain.Entities.Supplier.Supplier
                    {
                        TenantId = DefaultTenantId,
                        Code = "SUP-UNILEVER",
                        Name = "Công Ty TNHH Quốc Tế Unilever Việt Nam",
                        SupplierType = SupplierType.Goods,
                        Status = SupplierStatus.Active,
                        TaxCode = "0303123456",
                        Phone = "02854135686",
                        Email = "cskh@unilever.com",
                        Address = "Lô A2-3 KCN Tây Bắc Củ Chi, TP.HCM",
                        ContactPerson = "Phạm Thu Hương",
                        PaymentTermDays = 30,
                        CreditLimit = 300000000m
                    };
                    await context.Suppliers.AddAsync(supUnilever, ct);
                }

                await context.SaveChangesAsync(ct);

                // Cấu hình Delivery Config cho các NCC
                var allSups = new[] { supAcecook, supMasan, supOrion, supUnilever };
                foreach (var sup in allSups)
                {
                    if (!await context.SupplierDeliveryConfigs.IgnoreQueryFilters().AnyAsync(dc => dc.SupplierId == sup.Id, ct))
                    {
                        await context.SupplierDeliveryConfigs.AddAsync(new SupplierDeliveryConfig
                        {
                            TenantId = DefaultTenantId,
                            SupplierId = sup.Id,
                            LeadTimeHours = 24,
                            LeadTimeDays = 1,
                            CutoffTime = "17:00",
                            DeliveryWeekdaysMask = 127,
                            DeliveryDaysJson = "[\"Mon\",\"Tue\",\"Wed\",\"Thu\",\"Fri\",\"Sat\"]"
                        }, ct);
                    }
                }

                // Lấy các Chi nhánh
                var branches = await context.Branches.IgnoreQueryFilters().ToListAsync(ct);
                foreach (var sup in allSups)
                {
                    foreach (var br in branches)
                    {
                        if (!await context.SupplierBranchAssignments.IgnoreQueryFilters().AnyAsync(sba => sba.SupplierId == sup.Id && sba.BranchId == br.Id, ct))
                        {
                            await context.SupplierBranchAssignments.AddAsync(new SupplierBranchAssignment
                            {
                                TenantId = DefaultTenantId,
                                SupplierId = sup.Id,
                                BranchId = br.Id,
                                Status = SupplierStatus.Active
                            }, ct);
                        }
                    }
                }

                var fmcgCat = await context.Categories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.CategoryCode == "FMCG", ct);
                var fmcgCatId = fmcgCat?.Id ?? Guid.Parse("11111111-1111-1111-1111-111111111111");

                var alcCat = await context.Categories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.CategoryCode == "ALCOHOL", ct);
                var alcCatId = alcCat?.Id ?? Guid.Parse("22222222-2222-2222-2222-222222222222");

                var extraProducts = new List<(Product prod, Guid supplierId, decimal wholesalePrice, int moq)>
                {
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-MI-HAOHAO-TC",
                            Barcode = "8934563020021",
                            Name = "Mì Hảo Hảo Tôm Chua Cay (Thùng 30 gói)",
                            BaseUnit = "Thùng",
                            CostPrice = 110000m,
                            SellingPrice = 135000m,
                            MinStock = 10m,
                            MaxStock = 200m,
                            Status = "Active"
                        },
                        supAcecook.Id, 108000m, 5
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-MI-OMACHI-BBQ",
                            Barcode = "8935049501234",
                            Name = "Mì Omachi Xốt Bò Hầm (Thùng 30 gói)",
                            BaseUnit = "Thùng",
                            CostPrice = 195000m,
                            SellingPrice = 235000m,
                            MinStock = 5m,
                            MaxStock = 150m,
                            Status = "Active"
                        },
                        supMasan.Id, 192000m, 3
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-ORION-CHOCOPIE",
                            Barcode = "8936036010015",
                            Name = "Bánh Chocopie Orion Hộp 12 cái (360g)",
                            BaseUnit = "Hộp",
                            CostPrice = 48000m,
                            SellingPrice = 62000m,
                            MinStock = 15m,
                            MaxStock = 300m,
                            Status = "Active"
                        },
                        supOrion.Id, 46000m, 10
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-ORION-CUSTAS",
                            Barcode = "8936036010022",
                            Name = "Bánh Trứng Custas Orion Hộp 12 cái (282g)",
                            BaseUnit = "Hộp",
                            CostPrice = 52000m,
                            SellingPrice = 68000m,
                            MinStock = 12m,
                            MaxStock = 250m,
                            Status = "Active"
                        },
                        supOrion.Id, 50000m, 10
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-NUOC-COCA-320",
                            Barcode = "8935049500038",
                            Name = "Nước Ngọt Coca-Cola Sleek Lon 320ml (Lốc 6)",
                            BaseUnit = "Lốc",
                            CostPrice = 45000m,
                            SellingPrice = 56000m,
                            MinStock = 15m,
                            MaxStock = 300m,
                            Status = "Active"
                        },
                        supAcecook.Id, 43000m, 5
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-NUOC-PEPSI-320",
                            Barcode = "8935049500045",
                            Name = "Nước Ngọt Pepsi Không Calo Lon 320ml (Lốc 6)",
                            BaseUnit = "Lốc",
                            CostPrice = 44000m,
                            SellingPrice = 55000m,
                            MinStock = 15m,
                            MaxStock = 300m,
                            Status = "Active"
                        },
                        supMasan.Id, 42000m, 5
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = alcCatId,
                            ProductCode = "SP-BIA-TIGER-330",
                            Barcode = "8934822210041",
                            Name = "Bia Tiger Nâu Thùng 24 lon 330ml",
                            BaseUnit = "Thùng",
                            CostPrice = 345000m,
                            SellingPrice = 390000m,
                            MinStock = 10m,
                            MaxStock = 100m,
                            Status = "Active"
                        },
                        supAcecook.Id, 340000m, 2
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = alcCatId,
                            ProductCode = "SP-BIA-HEINEKEN-BAC",
                            Barcode = "8934822210058",
                            Name = "Bia Heineken Bạc Silver Thùng 24 lon 330ml",
                            BaseUnit = "Thùng",
                            CostPrice = 398000m,
                            SellingPrice = 450000m,
                            MinStock = 10m,
                            MaxStock = 100m,
                            Status = "Active"
                        },
                        supAcecook.Id, 395000m, 2
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-SUA-TH-180",
                            Barcode = "8936036010077",
                            Name = "Sữa Tươi Tiệt Trùng TH True Milk Ít Đường 180ml (Lốc 4)",
                            BaseUnit = "Lốc",
                            CostPrice = 31000m,
                            SellingPrice = 39000m,
                            MinStock = 20m,
                            MaxStock = 400m,
                            Status = "Active"
                        },
                        supOrion.Id, 30000m, 8
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-SUA-MILO-180",
                            Barcode = "8936036010084",
                            Name = "Thức Uống Lúa Mạch Nestlé Milo 180ml (Lốc 4 hộp)",
                            BaseUnit = "Lốc",
                            CostPrice = 28000m,
                            SellingPrice = 35000m,
                            MinStock = 20m,
                            MaxStock = 400m,
                            Status = "Active"
                        },
                        supOrion.Id, 27000m, 8
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-DAUAN-NEPTUNE-1L",
                            Barcode = "8936036010091",
                            Name = "Dầu Ăn Thượng Hạng Neptune Light Chai 1L",
                            BaseUnit = "Chai",
                            CostPrice = 48000m,
                            SellingPrice = 58000m,
                            MinStock = 12m,
                            MaxStock = 200m,
                            Status = "Active"
                        },
                        supMasan.Id, 46000m, 6
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-DAUAN-SIMPLY-1L",
                            Barcode = "8936036010107",
                            Name = "Dầu Đậu Nành Tinh Khiết Simply Chai 1L",
                            BaseUnit = "Chai",
                            CostPrice = 53000m,
                            SellingPrice = 65000m,
                            MinStock = 12m,
                            MaxStock = 200m,
                            Status = "Active"
                        },
                        supMasan.Id, 51000m, 6
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-NUOCMAM-CHINSU-500",
                            Barcode = "8936036010114",
                            Name = "Nước Mắm Chinsu Cá Hồi Chai 500ml",
                            BaseUnit = "Chai",
                            CostPrice = 39000m,
                            SellingPrice = 48000m,
                            MinStock = 15m,
                            MaxStock = 250m,
                            Status = "Active"
                        },
                        supMasan.Id, 37500m, 6
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-HATNEM-KNORR-900",
                            Barcode = "8936036010121",
                            Name = "Hạt Nêm Knorr Thịt Thăn Xương Ống Gói 900g",
                            BaseUnit = "Gói",
                            CostPrice = 64000m,
                            SellingPrice = 79000m,
                            MinStock = 10m,
                            MaxStock = 200m,
                            Status = "Active"
                        },
                        supUnilever.Id, 62000m, 5
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-BOTGIAT-OMO-3KG",
                            Barcode = "8936036010138",
                            Name = "Nước Giặt OMO Matic Cửa Trên Túi 3.1kg",
                            BaseUnit = "Túi",
                            CostPrice = 162000m,
                            SellingPrice = 198000m,
                            MinStock = 8m,
                            MaxStock = 100m,
                            Status = "Active"
                        },
                        supUnilever.Id, 158000m, 3
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-NUOCXAVAI-COMFORT-3L",
                            Barcode = "8936036010145",
                            Name = "Nước Xả Vải Comfort Hương Ban Mai Túi 3.2L",
                            BaseUnit = "Túi",
                            CostPrice = 175000m,
                            SellingPrice = 215000m,
                            MinStock = 8m,
                            MaxStock = 100m,
                            Status = "Active"
                        },
                        supUnilever.Id, 170000m, 3
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-NUOCRUACHEN-SUNLIGHT-750",
                            Barcode = "8936036010152",
                            Name = "Nước Rửa Chén Sunlight Chanh Chai 750g",
                            BaseUnit = "Chai",
                            CostPrice = 26000m,
                            SellingPrice = 33000m,
                            MinStock = 20m,
                            MaxStock = 300m,
                            Status = "Active"
                        },
                        supUnilever.Id, 25000m, 10
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-KEMDANHRANG-PS-200",
                            Barcode = "8936036010169",
                            Name = "Kem Đánh Răng P/S Trà Xanh Ống 180g",
                            BaseUnit = "Hộp",
                            CostPrice = 32000m,
                            SellingPrice = 42000m,
                            MinStock = 20m,
                            MaxStock = 300m,
                            Status = "Active"
                        },
                        supUnilever.Id, 31000m, 12
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-SNACK-LAY-NATURAL",
                            Barcode = "8936036010176",
                            Name = "Snack Khoai Tây Lay's Tự Nhiên Classic Gói 95g",
                            BaseUnit = "Gói",
                            CostPrice = 16500m,
                            SellingPrice = 22000m,
                            MinStock = 30m,
                            MaxStock = 500m,
                            Status = "Active"
                        },
                        supOrion.Id, 16000m, 15
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-XUCXICH-CP-500",
                            Barcode = "8936036010183",
                            Name = "Xúc Xích Tiệt Trùng Heo C.P Gói 500g (10 cây)",
                            BaseUnit = "Gói",
                            CostPrice = 42000m,
                            SellingPrice = 54000m,
                            MinStock = 15m,
                            MaxStock = 200m,
                            Status = "Active"
                        },
                        supAcecook.Id, 40000m, 10
                    )
                };

                foreach (var item in extraProducts)
                {
                    var existingProd = await context.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.ProductCode == item.prod.ProductCode, ct);
                    var targetProd = existingProd ?? item.prod;
                    if (existingProd == null)
                    {
                        await context.Products.AddAsync(item.prod, ct);
                    }

                    if (!await context.SupplierProducts.IgnoreQueryFilters().AnyAsync(sp => sp.ProductId == targetProd.Id && sp.SupplierId == item.supplierId, ct))
                    {
                        await context.SupplierProducts.AddAsync(new SupplierProduct
                        {
                            TenantId = DefaultTenantId,
                            ProductId = targetProd.Id,
                            SupplierId = item.supplierId,
                            SupplierProductCode = targetProd.ProductCode,
                            AgreedWholesalePrice = item.wholesalePrice,
                            DeliveryUoM = targetProd.BaseUnit,
                            MinimumOrderQuantity = item.moq,
                            IsPrimarySupplier = true,
                            Status = SupplierProductStatus.Active
                        }, ct);
                    }

                    foreach (var br in branches)
                    {
                        if (!await context.BranchStocks.IgnoreQueryFilters().AnyAsync(bs => bs.BranchId == br.Id && bs.ProductId == targetProd.Id, ct))
                        {
                            await context.BranchStocks.AddAsync(new BranchStock
                            {
                                TenantId = DefaultTenantId,
                                BranchId = br.Id,
                                ProductId = targetProd.Id,
                                SupplierId = item.supplierId,
                                BatchCode = $"LOT-202610-{targetProd.ProductCode}",
                                QuantityOnHand = targetProd.MinStock * 2,
                                QuantityReserved = 0,
                                LandedCostPerUnit = item.wholesalePrice,
                                ShelfLocation = "KHO-CHÍNH"
                            }, ct);
                        }
                    }
                }

                await context.SaveChangesAsync(ct);
            }

            // Đồng bộ TenantId của tất cả Products, BranchStocks, Suppliers về tenant_mt_mart_01
            var misalignedProds = await context.Products.IgnoreQueryFilters().Where(p => p.TenantId != DefaultTenantId).ToListAsync(ct);
            foreach (var p in misalignedProds) { p.TenantId = DefaultTenantId; }

            var misalignedStocks = await context.BranchStocks.IgnoreQueryFilters().Where(s => s.TenantId != DefaultTenantId).ToListAsync(ct);
            foreach (var s in misalignedStocks) { s.TenantId = DefaultTenantId; }

            var misalignedSups = await context.Suppliers.IgnoreQueryFilters().Where(s => s.TenantId != DefaultTenantId).ToListAsync(ct);
            foreach (var s in misalignedSups) { s.TenantId = DefaultTenantId; }

            var misalignedSp = await context.SupplierProducts.IgnoreQueryFilters().Where(s => s.TenantId != DefaultTenantId).ToListAsync(ct);
            foreach (var s in misalignedSp) { s.TenantId = DefaultTenantId; }

            var misalignedSba = await context.SupplierBranchAssignments.IgnoreQueryFilters().Where(s => s.TenantId != DefaultTenantId).ToListAsync(ct);
            foreach (var s in misalignedSba) { s.TenantId = DefaultTenantId; }

            var misalignedSdc = await context.SupplierDeliveryConfigs.IgnoreQueryFilters().Where(s => s.TenantId != DefaultTenantId).ToListAsync(ct);
            foreach (var s in misalignedSdc) { s.TenantId = DefaultTenantId; }

            await context.SaveChangesAsync(ct);

            logger.LogInformation("DatabaseSeeder finished successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during DatabaseSeeder execution.");
            throw;
        }
    }
    private static async Task SeedPromotionsAsync(AppDbContext context, string tenantId, ILogger logger, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        if (!await context.Promotions.IgnoreQueryFilters().AnyAsync(p => p.TenantId == tenantId && p.PromotionCode == "KM-GIAM10-TOIDA50K", ct))
        {
            logger.LogInformation("Seeding Promotions and Vouchers for Tenant {TenantId}...", tenantId);

            var promotions = new List<Promotion>
            {
                new Promotion
                {
                    TenantId = tenantId,
                    PromotionCode = "KM-GIAM10-TOIDA50K",
                    PromotionName = "Giảm 10% cho đơn từ 200.000đ (Tối đa 50.000đ)",
                    Type = "ORDER_DISCOUNT",
                    DiscountValue = 10.00m,
                    MinOrderValue = 200000.00m,
                    MaxDiscountAmount = 50000.00m,
                    UsageLimit = 500,
                    UsedCount = 15,
                    BudgetLimit = 10000000.00m,
                    TotalDiscountGiven = 750000.00m,
                    StartDate = now.AddDays(-30),
                    EndDate = now.AddDays(180),
                    IsActive = true,
                    Description = "Chương trình tri ân khách hàng: Giảm 10% cho hóa đơn từ 200.000đ, trần chiết khấu 50.000đ.",
                    TargetBranches = "ALL"
                },
                new Promotion
                {
                    TenantId = tenantId,
                    PromotionCode = "KM-GIAM30K-DON150K",
                    PromotionName = "Giảm thẳng 30.000đ cho đơn từ 150.000đ",
                    Type = "FIXED_AMOUNT",
                    DiscountValue = 30000.00m,
                    MinOrderValue = 150000.00m,
                    MaxDiscountAmount = null,
                    UsageLimit = 200,
                    UsedCount = 28,
                    BudgetLimit = 6000000.00m,
                    TotalDiscountGiven = 840000.00m,
                    StartDate = now.AddDays(-15),
                    EndDate = now.AddDays(90),
                    IsActive = true,
                    Description = "Ưu đãi trực tiếp: Trừ ngay 30.000 VNĐ cho hóa đơn mua sắm từ 150.000 VNĐ.",
                    TargetBranches = "ALL"
                }
            };

            // Promotion 3: Mua 3 Chocopie tặng 1 Coca
            var p3 = new Promotion
            {
                TenantId = tenantId,
                PromotionCode = "KM-MUA3TANG1-COCA",
                PromotionName = "Mua 3 Bánh Chocopie tặng 1 Coca-Cola lon",
                Type = "BUY_X_GET_Y",
                DiscountValue = 0.00m,
                MinOrderValue = 0.00m,
                UsageLimit = 100,
                UsedCount = 8,
                StartDate = now.AddDays(-10),
                EndDate = now.AddDays(60),
                IsActive = true,
                Description = "Ưu đãi giải khát: Khi mua đủ 3 hộp Bánh Chocopie Orion, tặng ngay 1 lốc Coca-Cola lon 320ml.",
                TargetBranches = "ALL"
            };
            p3.Conditions.Add(new PromotionCondition
            {
                TenantId = tenantId,
                PromotionId = p3.Id,
                ProductId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"),
                ProductRole = "BUY",
                MinQty = 3.00m,
                Note = "Mua tối thiểu 3 hộp Bánh Chocopie Orion"
            });
            p3.Conditions.Add(new PromotionCondition
            {
                TenantId = tenantId,
                PromotionId = p3.Id,
                ProductId = Guid.Parse("aaaaaaaa-3333-3333-3333-333333333333"),
                ProductRole = "GIFT",
                MinQty = 1.00m,
                Note = "Tặng 1 lốc nước ngọt Coca-Cola 320ml"
            });
            promotions.Add(p3);

            // Promotion 4: Voucher VIP 50k
            var p4 = new Promotion
            {
                TenantId = tenantId,
                PromotionCode = "VOUCHER-VIP50K",
                PromotionName = "Chiến dịch Voucher VIP Giảm 50.000đ",
                Type = "VOUCHER",
                DiscountValue = 50000.00m,
                MinOrderValue = 250000.00m,
                MaxDiscountAmount = null,
                UsageLimit = 10,
                UsedCount = 1,
                BudgetLimit = 500000.00m,
                TotalDiscountGiven = 50000.00m,
                StartDate = now.AddDays(-20),
                EndDate = now.AddDays(120),
                IsActive = true,
                Description = "Mã voucher tri ân giảm 50.000đ cho khách hàng VIP đạt cấp độ Vàng/Kim Cương.",
                TargetBranches = "ALL",
                VoucherPrefix = "VIP50K",
                TotalVoucherQuantity = 4
            };
            p4.VoucherCodes.Add(new VoucherCode { TenantId = tenantId, PromotionId = p4.Id, Code = "VIP50K-TEST01", Status = "UNUSED" });
            p4.VoucherCodes.Add(new VoucherCode { TenantId = tenantId, PromotionId = p4.Id, Code = "VIP50K-TEST02", Status = "UNUSED" });
            p4.VoucherCodes.Add(new VoucherCode { TenantId = tenantId, PromotionId = p4.Id, Code = "VIP50K-TEST03", Status = "UNUSED" });
            p4.VoucherCodes.Add(new VoucherCode
            {
                TenantId = tenantId,
                PromotionId = p4.Id,
                Code = "VIP50K-DA-DUNG",
                Status = "USED",
                OrderCode = "ORD-20261015-001",
                OrderId = Guid.Parse("f0000001-1111-1111-1111-111111111111"),
                UsedAt = now.AddDays(-2)
            });
            promotions.Add(p4);

            // Promotion 5: Voucher Cuối Tuần 20%
            var p5 = new Promotion
            {
                TenantId = tenantId,
                PromotionCode = "VOUCHER-CUOITUAN20",
                PromotionName = "Voucher Giảm 20% Mua Sắm Cuối Tuần (Tối đa 40.000đ)",
                Type = "VOUCHER",
                DiscountValue = 20.00m,
                MinOrderValue = 100000.00m,
                MaxDiscountAmount = 40000.00m,
                UsageLimit = 20,
                UsedCount = 0,
                StartDate = now.AddDays(-5),
                EndDate = now.AddDays(90),
                IsActive = true,
                Description = "Mã voucher giảm 20% áp dụng cho khách hàng thanh toán tại quầy POS vào cuối tuần.",
                TargetBranches = "ALL",
                VoucherPrefix = "WEEKEND",
                TotalVoucherQuantity = 3
            };
            p5.VoucherCodes.Add(new VoucherCode { TenantId = tenantId, PromotionId = p5.Id, Code = "WEEKEND-20PCT01", Status = "UNUSED" });
            p5.VoucherCodes.Add(new VoucherCode { TenantId = tenantId, PromotionId = p5.Id, Code = "WEEKEND-20PCT02", Status = "UNUSED" });
            p5.VoucherCodes.Add(new VoucherCode { TenantId = tenantId, PromotionId = p5.Id, Code = "WEEKEND-20PCT03", Status = "UNUSED" });
            promotions.Add(p5);

            // Promotion 6: Hết hạn
            var p6 = new Promotion
            {
                TenantId = tenantId,
                PromotionCode = "KM-TET2025-HETHAN",
                PromotionName = "Khuyến mãi Tết 2025 (Đã hết hạn)",
                Type = "ORDER_DISCOUNT",
                DiscountValue = 15.00m,
                MinOrderValue = 100000.00m,
                MaxDiscountAmount = 30000.00m,
                UsageLimit = 100,
                UsedCount = 100,
                StartDate = now.AddDays(-400),
                EndDate = now.AddDays(-340),
                IsActive = false,
                Description = "Chương trình khuyến mãi mùa Tết năm cũ, đã hết hạn để test bộ lọc trạng thái.",
                TargetBranches = "ALL"
            };
            promotions.Add(p6);

            await context.Promotions.AddRangeAsync(promotions, ct);
            await context.SaveChangesAsync(ct);
        }

        // 7. Seed Dữ liệu bán lẻ POS (Hàng tạp hóa, Barcode, Tồn kho FEFO & Khách hàng công nợ)
        await SeedPosRetailDataAsync(context, logger, ct);
    }

    private static async Task SeedPosRetailDataAsync(AppDbContext context, ILogger logger, CancellationToken ct)
    {
        // 1. Tạo hoặc lấy Branch POS chuẩn
        var branch = await context.Branches.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.BranchCode == "BR-POS-01", ct);
        if (branch == null)
        {
            branch = new Domain.Entities.SaaS.Branch
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TenantId = DefaultTenantId,
                BranchCode = "BR-POS-01",
                Name = "Siêu Thị Mini MT-Grocery (Cầu Giấy)",
                Address = "123 Xuân Thủy, Cầu Giấy, Hà Nội",
                Phone = "0987.654.321",
                IsActive = true
            };
            await context.Branches.AddAsync(branch, ct);
            await context.SaveChangesAsync(ct);
        }

        // 2. Thêm khách hàng mẫu phục vụ test bán hàng và bán nợ
        if (!await context.Customers.IgnoreQueryFilters().AnyAsync(c => c.CustomerCode == "KH-POS-001", ct))
        {
            logger.LogInformation("Seeding POS Retail Customers...");
            var customers = new List<Domain.Entities.Sales.Customer>
            {
                new Domain.Entities.Sales.Customer
                {
                    Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    TenantId = DefaultTenantId,
                    CustomerCode = "KH-POS-001",
                    Name = "Nguyễn Văn Khách (Thân Thiết)",
                    Phone = "0912345678",
                    Email = "khach.nguyen@gmail.com",
                    Address = "Số 15 Cầu Giấy, Hà Nội",
                    MembershipTier = "Gold",
                    RewardPoints = 250,
                    TotalSpending = 5500000m,
                    TotalDebt = 350000m,
                    DebtLimit = 5000000m,
                    AllowDebt = true,
                    IsBlockedForDebt = false
                },
                new Domain.Entities.Sales.Customer
                {
                    Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    TenantId = DefaultTenantId,
                    CustomerCode = "KH-POS-002",
                    Name = "Chị Lan Tạp Hóa Xóm",
                    Phone = "0987654321",
                    Address = "Ngõ 165 Cầu Giấy, Hà Nội",
                    MembershipTier = "Platinum",
                    RewardPoints = 520,
                    TotalSpending = 18000000m,
                    TotalDebt = 2100000m,
                    DebtLimit = 10000000m,
                    AllowDebt = true,
                    IsBlockedForDebt = false
                },
                new Domain.Entities.Sales.Customer
                {
                    Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    TenantId = DefaultTenantId,
                    CustomerCode = "KH-POS-003",
                    Name = "Bác Ba (Bị Khóa Nợ)",
                    Phone = "0905123456",
                    Address = "Phố Dịch Vọng, Cầu Giấy, Hà Nội",
                    MembershipTier = "Standard",
                    RewardPoints = 20,
                    TotalSpending = 1200000m,
                    TotalDebt = 1200000m,
                    DebtLimit = 1000000m,
                    AllowDebt = false,
                    IsBlockedForDebt = true
                }
            };
            await context.Customers.AddRangeAsync(customers, ct);
            await context.SaveChangesAsync(ct);
        }

        // 3. Thêm các sản phẩm tạp hóa có Barcode chuẩn
        var catFmcg = await context.Categories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.CategoryCode == "FMCG", ct);
        var catId = catFmcg?.Id ?? Guid.Parse("11111111-1111-1111-1111-111111111111");

        var posProducts = new List<(Guid Id, string SKU, string Barcode, string Name, decimal Price, decimal CostPrice, string Unit, decimal Vat)>
        {
            (Guid.Parse("e0000001-0000-0000-0000-000000000001"), "HH-TOM-CAY", "8934563138164", "Mì Hảo Hảo tôm chua cay 75g", 4500m, 3600m, "Gói", 0.01m),
            (Guid.Parse("e0000002-0000-0000-0000-000000000002"), "COCA-320ML", "8935049500123", "Nước ngọt Coca-Cola lon 320ml", 10000m, 7800m, "Lon", 0.01m),
            (Guid.Parse("e0000003-0000-0000-0000-000000000003"), "REDBULL-250", "8850228000153", "Nước tăng lực Redbull lon 250ml", 12000m, 9500m, "Lon", 0.01m),
            (Guid.Parse("e0000004-0000-0000-0000-000000000004"), "LAVIE-500ML", "8936036010022", "Nước khoáng Lavie chai 500ml", 6000m, 4200m, "Chai", 0.01m),
            (Guid.Parse("e0000005-0000-0000-0000-000000000005"), "VNM-100PCT", "8934673123456", "Sữa tươi tiệt trùng Vinamilk 100% 180ml", 8500m, 6800m, "Hộp", 0.01m),
            (Guid.Parse("e0000006-0000-0000-0000-000000000006"), "KEN-330ML", "8934822212345", "Bia Heineken lon 330ml", 22000m, 18500m, "Lon", 0.01m),
            (Guid.Parse("e0000007-0000-0000-0000-000000000007"), "SIMPLY-1L", "8934561230018", "Dầu ăn đậu nành Simply chai 1 lít", 55000m, 45000m, "Chai", 0.01m),
            (Guid.Parse("e0000008-0000-0000-0000-000000000008"), "GAO-ST25-5K", "8935001780099", "Gạo ST25 Ông Cua túi 5kg Thượng Hạng", 180000m, 150000m, "Túi", 0.01m),
            (Guid.Parse("e0000009-0000-0000-0000-000000000009"), "SUNLIGHT-750", "8934561234567", "Nước rửa chén Sunlight Chanh túi 750g", 30000m, 24000m, "Túi", 0.01m)
        };

        foreach (var item in posProducts)
        {
            var existingProd = await context.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == item.Id || p.Barcode == item.Barcode, ct);
            if (existingProd == null)
            {
                var newP = new Product
                {
                    Id = item.Id,
                    TenantId = DefaultTenantId,
                    ProductCode = item.SKU,
                    Barcode = item.Barcode,
                    Name = item.Name,
                    CategoryId = catId,
                    SellingPrice = item.Price,
                    CostPrice = item.CostPrice,
                    BaseUnit = item.Unit,
                    VatRate = item.Vat,
                    TaxCategory = "STANDARD_1PCT",
                    Status = "Active"
                };
                await context.Products.AddAsync(newP, ct);
            }
        }
        await context.SaveChangesAsync(ct);

        // 4. Tạo tồn kho chi nhánh BranchStocks theo FEFO cho các sản phẩm
        var pMap = await context.Products.IgnoreQueryFilters()
            .Where(p => posProducts.Select(x => x.Id).Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        foreach (var item in posProducts)
        {
            if (pMap.TryGetValue(item.Id, out var prod))
            {
                var hasStock = await context.BranchStocks.IgnoreQueryFilters()
                    .AnyAsync(s => s.BranchId == branch.Id && s.ProductId == prod.Id, ct);

                if (!hasStock)
                {
                    // Lô 1: Hết hạn sớm (trong 1-2 tháng tới)
                    var stockLotEarly = new Domain.Entities.Inventory.BranchStock
                    {
                        Id = Guid.NewGuid(),
                        TenantId = DefaultTenantId,
                        BranchId = branch.Id,
                        ProductId = prod.Id,
                        BatchCode = $"LOT-{item.SKU}-EARLY",
                        ManufactureDate = DateTime.UtcNow.AddMonths(-3),
                        ExpiryDate = DateTime.UtcNow.AddMonths(2),
                        QuantityOnHand = 25m,
                        LandedCostPerUnit = item.CostPrice,
                        ShelfLocation = "Quầy 01",
                        ZoneArea = "Khu Bán Lẻ"
                    };

                    // Lô 2: Hết hạn muộn (trong 12 tháng tới)
                    var stockLotLate = new Domain.Entities.Inventory.BranchStock
                    {
                        Id = Guid.NewGuid(),
                        TenantId = DefaultTenantId,
                        BranchId = branch.Id,
                        ProductId = prod.Id,
                        BatchCode = $"LOT-{item.SKU}-LATE",
                        ManufactureDate = DateTime.UtcNow.AddMonths(-1),
                        ExpiryDate = DateTime.UtcNow.AddMonths(12),
                        QuantityOnHand = 100m,
                        LandedCostPerUnit = item.CostPrice,
                        ShelfLocation = "Kho Phụ",
                        ZoneArea = "Kho Chi Nhánh"
                    };

                    await context.BranchStocks.AddRangeAsync(new[] { stockLotEarly, stockLotLate }, ct);
                }
            }
        }
        await context.SaveChangesAsync(ct);
        logger.LogInformation("Seeded POS Retail Products and FEFO BranchStocks successfully.");
    }
}
