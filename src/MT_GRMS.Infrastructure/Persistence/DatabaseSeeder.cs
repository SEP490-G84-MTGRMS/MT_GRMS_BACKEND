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

            // 6. Seed Additional Products & Supplier Linkages for Order Matrix
            if (!await context.Products.IgnoreQueryFilters().AnyAsync(p => p.ProductCode == "SP-MI-HAOHAO-TC", ct))
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
                    await context.Products.AddAsync(item.prod, ct);

                    await context.SupplierProducts.AddAsync(new SupplierProduct
                    {
                        TenantId = DefaultTenantId,
                        ProductId = item.prod.Id,
                        SupplierId = item.supplierId,
                        SupplierProductCode = item.prod.ProductCode,
                        AgreedWholesalePrice = item.wholesalePrice,
                        DeliveryUoM = item.prod.BaseUnit,
                        MinimumOrderQuantity = item.moq,
                        IsPrimarySupplier = true,
                        Status = SupplierProductStatus.Active
                    }, ct);

                    foreach (var br in branches)
                    {
                        await context.BranchStocks.AddAsync(new BranchStock
                        {
                            TenantId = DefaultTenantId,
                            BranchId = br.Id,
                            ProductId = item.prod.Id,
                            SupplierId = item.supplierId,
                            BatchCode = $"LOT-202610-{item.prod.ProductCode}",
                            QuantityOnHand = item.prod.MinStock * 2,
                            QuantityReserved = 0,
                            LandedCostPerUnit = item.wholesalePrice,
                            ShelfLocation = "KHO-CHÍNH"
                        }, ct);
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
}
