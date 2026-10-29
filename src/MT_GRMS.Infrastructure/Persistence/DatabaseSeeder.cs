using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MT_GRMS.Domain.Entities;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Infrastructure.Persistence;

/// <summary>
/// Service nạp dữ liệu mẫu khởi tạo (Database Seeder) cho MT-GRMS:
/// Khởi tạo User chủ tiệm, danh mục, sản phẩm phân loại thuế, đơn hàng thực tế và phiếu chi.
/// </summary>
public static class DatabaseSeeder
{
    private const string DefaultTenantId = "tenant-grocery-001";

    public static async Task SeedAsync(AppDbContext context, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            // 1. Seed Users (Chủ tiệm với mã PIN 1234 đã băm PBKDF2)
            if (!await context.Users.AnyAsync(ct))
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
                    Username = "store_owner",
                    Password = passwordHash,
                    SecurityPinSalt = Convert.ToBase64String(saltBytes),
                    SecurityPinHash = Convert.ToBase64String(pinHashBytes),
                    FailedPinAttempts = 0
                };

                var cashier = new User
                {
                    Id = "user-cashier-001",
                    Username = "staff_cashier",
                    Password = staffPasswordHash
                };

                await context.Users.AddRangeAsync(new[] { owner, cashier }, ct);
                await context.SaveChangesAsync(ct);
            }

            // 2. Seed Categories
            if (!await context.Categories.IgnoreQueryFilters().AnyAsync(ct))
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
            if (!await context.Products.IgnoreQueryFilters().AnyAsync(ct))
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

            // 4. Seed Orders & OrderItems (Phát sinh trong Q3/2026: 01/07/2026 - 30/09/2026)
            if (!await context.Orders.IgnoreQueryFilters().AnyAsync(ct))
            {
                logger.LogInformation("Seeding Orders and OrderItems...");

                // Đơn hàng 1: Ngày 15/07/2026 - Hàng thường (Được giảm GTGT 0.8%)
                var order1 = new Order
                {
                    Id = Guid.Parse("f0000001-1111-1111-1111-111111111111"),
                    TenantId = DefaultTenantId,
                    OrderCode = "ORD-20260715-001",
                    StoreId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
                    SubTotal = 465000m,
                    DiscountAmount = 0m,
                    TotalAmount = 465000m,
                    PaymentMethod = PaymentMethod.Cash,
                    PaymentStatus = PaymentStatus.Paid,
                    CreatedAt = new DateTime(2026, 7, 15, 10, 30, 0, DateTimeKind.Utc)
                };

                order1.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = DefaultTenantId,
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
                    TenantId = DefaultTenantId,
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
                    StoreId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
                    SubTotal = 565000m,
                    DiscountAmount = 0m,
                    TotalAmount = 565000m,
                    PaymentMethod = PaymentMethod.BankTransfer,
                    PaymentStatus = PaymentStatus.Paid,
                    CreatedAt = new DateTime(2026, 8, 20, 15, 45, 0, DateTimeKind.Utc)
                };

                order2.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = DefaultTenantId,
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
                    TenantId = DefaultTenantId,
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
                    TenantId = DefaultTenantId,
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
                    StoreId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
                    SubTotal = 385000000m, // 385 triệu doanh thu
                    DiscountAmount = 0m,
                    TotalAmount = 385000000m,
                    PaymentMethod = PaymentMethod.BankTransfer,
                    PaymentStatus = PaymentStatus.Paid,
                    CreatedAt = new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc)
                };

                order3.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = DefaultTenantId,
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
            if (!await context.ExpenseVouchers.IgnoreQueryFilters().AnyAsync(ct))
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
                        BranchId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
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
                        BranchId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
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
                        BranchId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
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
                        BranchId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
                        Status = ExpenseVoucherStatus.Approved,
                        IsTaxDeductible = true,
                        ExpenseDate = new DateTime(2026, 9, 25, 14, 0, 0, DateTimeKind.Utc),
                        CreatedAt = new DateTime(2026, 9, 25, 14, 0, 0, DateTimeKind.Utc)
                    }
                };

                await context.ExpenseVouchers.AddRangeAsync(expenses, ct);
                await context.SaveChangesAsync(ct);
            }

            logger.LogInformation("DatabaseSeeder finished successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during DatabaseSeeder execution.");
            throw;
        }
    }
}