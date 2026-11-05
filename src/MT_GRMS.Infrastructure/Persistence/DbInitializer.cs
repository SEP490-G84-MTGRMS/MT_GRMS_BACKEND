using Microsoft.EntityFrameworkCore;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Infrastructure.Persistence;

/// <summary>
/// Lớp hỗ trợ Code-First: Tự động khởi tạo cấu trúc CSDL và nạp dữ liệu mẫu ban đầu (Seed Data).
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(AppDbContext context)
    {
        // 1. Tự động áp dụng Migrations để tạo CSDL và toàn bộ 34 bảng nếu chưa có
        if (context.Database.IsRelational())
        {
            await context.Database.MigrateAsync();
        }
        else
        {
            await context.Database.EnsureCreatedAsync();
        }

        var defaultTenantId = "tenant_mt_mart_01";

        // 2. Kiểm tra nếu đã có dữ liệu mẫu thì đảm bảo đồng bộ Catalog, Nhân sự (Employees, ShiftConfigs, BranchAttendancePolicies), Orders
        if (await context.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == defaultTenantId))
        {
            await CatalogSeedData.SeedAsync(context, defaultTenantId);
            await EmployeeSeedData.SeedAsync(context, defaultTenantId);
            await SeedOrdersAndPreOrdersAsync(context);
            return;
        }

        // =========================================================================
        // PHÂN HỆ 1: SAAS, MULTI-TENANCY & IDENTITY
        // =========================================================================

        // Gói cước SaaS
        var planFree = new SubscriptionPlan
        {
            Id = "plan_free",
            PlanCode = "FREE",
            PlanName = "Gói Cơ Bản (Miễn Phí)",
            PriceMonthly = 0.00m,
            PriceYearly = 0.00m,
            MaxBranches = 1,
            MaxStaff = 3,
            EnabledModulesJson = "[\"Catalog\",\"Pos\"]",
            IsActive = true
        };

        var planPro = new SubscriptionPlan
        {
            Id = "plan_pro",
            PlanCode = "PRO",
            PlanName = "Gói Doanh Nghiệp (Toàn Năng)",
            PriceMonthly = 999000.00m,
            PriceYearly = 9990000.00m,
            MaxBranches = 10,
            MaxStaff = 100,
            EnabledModulesJson = "[\"Catalog\",\"Pos\",\"Inventory\",\"Supplier\",\"Finance\",\"HR\",\"Tax\"]",
            IsActive = true
        };

        if (!await context.SubscriptionPlans.AnyAsync())
        {
            await context.SubscriptionPlans.AddRangeAsync(planFree, planPro);
            await context.SaveChangesAsync();
        }

        // Doanh nghiệp thuê SaaS mẫu (Default Tenant)
        var demoTenant = new Tenant
        {
            Id = defaultTenantId,
            Name = "Chuỗi Siêu Thị Thực Phẩm Sạch MT-Mart",
            Subdomain = "mtmart",
            Code = "MTMART",
            TaxCode = "0314567890",
            Hotline = "1900 6868",
            Address = "Số 123 Đường Nguyễn Huệ, Phường Bến Nghé",
            City = "Hồ Chí Minh",
            Status = "ACTIVE",
            CurrentPlan = planPro.PlanCode,
            SubscriptionExpiresAt = DateTime.UtcNow.AddYears(1)
        };
        await context.Tenants.AddAsync(demoTenant);

        // Tài khoản người dùng mẫu
        var userAdmin = new User
        {
            Id = "usr_super_admin",
            UserName = "admin",
            PasswordHash = "Admin@123",
            FullName = "Hệ Thống Super Admin",
            Email = "admin@mtgrms.vn",
            Phone = "0901234567",
            IsActive = true,
            IsEmailVerified = true
        };

        var userStoreManager = new User
        {
            Id = "usr_store_manager",
            UserName = "manager01",
            PasswordHash = "Manager@123",
            FullName = "Nguyễn Văn Hùng (Quản Lý Cửa Hàng)",
            Email = "hung.nguyen@mtmart.vn",
            Phone = "0909876543",
            IsActive = true,
            IsEmailVerified = true
        };

        var userCashier = new User
        {
            Id = "usr_cashier",
            UserName = "cashier01",
            PasswordHash = "Cashier@123",
            FullName = "Lê Thị Thảo (Thu Ngân POS)",
            Email = "thao.le@mtmart.vn",
            Phone = "0912345678",
            IsActive = true,
            IsEmailVerified = true
        };

        var userStoreOwner = new User
        {
            Id = "usr_store_owner",
            UserName = "chucuahang",
            PasswordHash = "123456",
            FullName = "Tran Van Chu (Chu cua hang)",
            Email = "chucuahang@mtmart.vn",
            Phone = "0902345678",
            IsActive = true,
            IsEmailVerified = true
        };

        var userStaff = new User
        {
            Id = "usr_staff_cashier",
            UserName = "nhanvien",
            PasswordHash = "123456",
            FullName = "Le Thi Thu Ngan (Nhan vien ban hang)",
            Email = "nhanvien@mtmart.vn",
            Phone = "0988765432",
            IsActive = true,
            IsEmailVerified = true
        };

        await context.Users.AddRangeAsync(userAdmin, userStoreManager, userCashier, userStoreOwner, userStaff);

        // Phân quyền TenantUser
        var tenantUserAdmin = new TenantUser
        {
            TenantId = defaultTenantId,
            UserId = userAdmin.Id,
            Role = "SUPER_ADMIN",
            IsActive = true
        };

        var tenantUserManager = new TenantUser
        {
            TenantId = defaultTenantId,
            UserId = userStoreManager.Id,
            Role = "BRANCH_MANAGER",
            IsActive = true
        };

        var tenantUserCashier = new TenantUser
        {
            TenantId = defaultTenantId,
            UserId = userCashier.Id,
            Role = "CASHIER",
            IsActive = true
        };

        var tenantUserOwner = new TenantUser
        {
            TenantId = defaultTenantId,
            UserId = userStoreOwner.Id,
            Role = "STORE_OWNER",
            IsActive = true
        };

        var tenantUserStaff = new TenantUser
        {
            TenantId = defaultTenantId,
            UserId = userStaff.Id,
            Role = "CASHIER",
            IsActive = true
        };

        await context.TenantUsers.AddRangeAsync(tenantUserAdmin, tenantUserManager, tenantUserCashier, tenantUserOwner, tenantUserStaff);
        await context.SaveChangesAsync();

        // =========================================================================
        // PHÂN HỆ 2: CHI NHÁNH & KHO
        // =========================================================================
        var branchQ1 = new Branch
        {
            TenantId = defaultTenantId,
            BranchCode = "BR-HCM-01",
            Name = "MT-Mart Chi Nhánh Quận 1",
            Address = "Số 45 Lê Duẩn, Bến Nghé, Quận 1",
            Phone = "028 3822 5566",
            Latitude = 10.7769m,
            Longitude = 106.7009m,
            CheckinRadiusMeters = 100,
            IsActive = true
        };

        var branchHN = new Branch
        {
            TenantId = defaultTenantId,
            BranchCode = "BR-HN-01",
            Name = "MT-Mart Chi Nhánh Cầu Giấy",
            Address = "Số 88 Cầu Giấy, Quan Hoa, Cầu Giấy",
            Phone = "024 3766 8899",
            Latitude = 21.0333m,
            Longitude = 105.7833m,
            CheckinRadiusMeters = 150,
            IsActive = true
        };

        var branchTX = new Branch
        {
            TenantId = defaultTenantId,
            BranchCode = "BR-HN-02",
            Name = "MT-Mart Chi Nhánh Thanh Xuân",
            Address = "Số 129 Nguyễn Trãi, Thượng Đình, Thanh Xuân",
            Phone = "024 3858 6677",
            Latitude = 20.9998m,
            Longitude = 105.8144m,
            CheckinRadiusMeters = 100,
            IsActive = true
        };

        await context.Branches.AddRangeAsync(branchQ1, branchHN, branchTX);
        await context.SaveChangesAsync();

        // =========================================================================
        // PHÂN HỆ 3: DANH MỤC & SẢN PHẨM (CATALOG)
        // =========================================================================
        var catFresh = new Category
        {
            TenantId = defaultTenantId,
            CategoryCode = "FRESH",
            Name = "Thực Phẩm & Nông Sản Tươi Sống",
            Description = "Rau, củ, quả, thịt cá tươi đạt chuẩn VietGAP"
        };

        var catDry = new Category
        {
            TenantId = defaultTenantId,
            CategoryCode = "DRY",
            Name = "Bách Hóa Tiêu Dùng & Gạo Hạt",
            Description = "Gạo, gia vị, dầu ăn, thực phẩm khô đóng gói"
        };

        var catBeverage = new Category
        {
            TenantId = defaultTenantId,
            CategoryCode = "BEV",
            Name = "Sữa & Đồ Uống Giải Khát",
            Description = "Sữa tươi tiệt trùng, nước khoáng, nước trái cây"
        };

        await context.Categories.AddRangeAsync(catFresh, catDry, catBeverage);
        await context.SaveChangesAsync();

        // Sản phẩm
        var prodRice = new Product
        {
            TenantId = defaultTenantId,
            CategoryId = catDry.Id,
            ProductCode = "SP-GAO-ST25",
            Barcode = "893850123401",
            Name = "Gạo Thơm ST25 Ông Cua Túi 5kg",
            BaseUnit = "Túi",
            CostPrice = 160000.00m,
            SellingPrice = 195000.00m,
            MinStock = 10.00m,
            MaxStock = 500.00m,
            VatRate = 0.00m,
            ProductType = "STANDARD",
            Status = ProductStatus.Active,
            HasBatches = true,
            IsFavorite = true
        };

        var prodMilk = new Product
        {
            TenantId = defaultTenantId,
            CategoryId = catBeverage.Id,
            ProductCode = "SP-SUA-VNM-1L",
            Barcode = "893850123402",
            Name = "Sữa Tươi Tiệt Trùng Vinamilk Có Đường 1L",
            BaseUnit = "Hộp",
            CostPrice = 30000.00m,
            SellingPrice = 38000.00m,
            MinStock = 20.00m,
            MaxStock = 1000.00m,
            VatRate = 8.00m,
            ProductType = "STANDARD",
            Status = ProductStatus.Active,
            HasBatches = true,
            IsFavorite = true
        };

        var prodSauce = new Product
        {
            TenantId = defaultTenantId,
            CategoryId = catDry.Id,
            ProductCode = "SP-NUOCMAM-NAMNGU",
            Barcode = "893850123403",
            Name = "Nước Mắm Nam Ngư Đệ Nhị Chai 900ml",
            BaseUnit = "Chai",
            CostPrice = 22000.00m,
            SellingPrice = 28500.00m,
            MinStock = 15.00m,
            MaxStock = 300.00m,
            VatRate = 8.00m,
            ProductType = "STANDARD",
            Status = ProductStatus.Active
        };

        var prodG7 = new Product
        {
            TenantId = defaultTenantId,
            CategoryId = catBeverage.Id,
            ProductCode = "G7-3IN1-18G",
            Barcode = "8935024122310",
            Name = "Cà phê hòa tan G7 3in1 Hộp 18 gói",
            BaseUnit = "Hộp",
            CostPrice = 45000.00m,
            SellingPrice = 58000.00m,
            MinStock = 10.00m,
            MaxStock = 200.00m,
            VatRate = 1.00m,
            ProductType = "STANDARD",
            Status = ProductStatus.Active,
            HasBatches = true,
            IsFavorite = true
        };

        var prodChoco = new Product
        {
            TenantId = defaultTenantId,
            CategoryId = catDry.Id,
            ProductCode = "CHOCO-12P",
            Barcode = "8936036020380",
            Name = "Bánh Orion Choco-Pie truyền thống Hộp 12 cái",
            BaseUnit = "Hộp",
            CostPrice = 42000.00m,
            SellingPrice = 55000.00m,
            MinStock = 10.00m,
            MaxStock = 200.00m,
            VatRate = 1.00m,
            ProductType = "STANDARD",
            Status = ProductStatus.Active,
            HasBatches = true,
            IsFavorite = true
        };

        var prodMuoi = new Product
        {
            TenantId = defaultTenantId,
            CategoryId = catDry.Id,
            ProductCode = "MHH-TOMCHUA-120G",
            Barcode = "8934563065249",
            Name = "Muối chấm Hảo Hảo tôm chua cay Hũ 120g",
            BaseUnit = "Hũ",
            CostPrice = 12000.00m,
            SellingPrice = 16000.00m,
            MinStock = 20.00m,
            MaxStock = 300.00m,
            VatRate = 1.00m,
            ProductType = "STANDARD",
            Status = ProductStatus.Active,
            HasBatches = false,
            IsFavorite = true
        };

        var prodBia = new Product
        {
            TenantId = defaultTenantId,
            CategoryId = catBeverage.Id,
            ProductCode = "BIA-HANOI-SLEEK",
            Barcode = "8936000440824",
            Name = "Bia Hà Nội Sleek Lon cao 330ml",
            BaseUnit = "Lon",
            CostPrice = 10500.00m,
            SellingPrice = 14000.00m,
            MinStock = 24.00m,
            MaxStock = 500.00m,
            VatRate = 1.00m,
            ProductType = "STANDARD",
            Status = ProductStatus.Active,
            HasBatches = true,
            IsFavorite = true
        };

        await context.Products.AddRangeAsync(prodRice, prodMilk, prodSauce, prodG7, prodChoco, prodMuoi, prodBia);
        await context.SaveChangesAsync();

        // Đơn vị quy đổi (1 Thùng = 12 Hộp sữa)
        var unitCartonMilk = new ProductUnit(prodMilk.Id, "Thùng", 12.00m, 445000.00m, "893850123499", false);
        await context.ProductUnits.AddAsync(unitCartonMilk);

        // =========================================================================
        // PHÂN HỆ 4: NHÀ CUNG CẤP & CHÍNH SÁCH GIÁ
        // =========================================================================
        var supplierVinamilk = new Domain.Entities.Supplier.Supplier
        {
            TenantId = defaultTenantId,
            Code = "SUP-VINAMILK",
            Name = "Công Ty Cổ Phần Sữa Việt Nam (Vinamilk)",
            SupplierType = Domain.Enums.SupplierType.Goods,
            Status = Domain.Enums.SupplierStatus.Active,
            TaxCode = "0300588569",
            Phone = "02854155555",
            Email = "contact@vinamilk.com.vn",
            Address = "Số 10 Tân Trào, Tân Phú, Quận 7, TP.HCM",
            ContactPerson = "Nguyễn Anh Tuấn",
            PaymentTermDays = 30,
            CreditLimit = 200000000.00m
        };

        var supplierRice = new Domain.Entities.Supplier.Supplier
        {
            TenantId = defaultTenantId,
            Code = "SUP-HOCUA",
            Name = "Doanh Nghiệp Tư Nhân Hồ Quang Cua (Gạo ST25)",
            SupplierType = Domain.Enums.SupplierType.Goods,
            Status = Domain.Enums.SupplierStatus.Active,
            TaxCode = "2200123456",
            Phone = "02993888999",
            Email = "gaost25@soc-trang.vn",
            Address = "Tỉnh Sóc Trăng",
            ContactPerson = "Hồ Quang Cua",
            PaymentTermDays = 15,
            CreditLimit = 100000000.00m
        };

        await context.Suppliers.AddRangeAsync(supplierVinamilk, supplierRice);
        await context.SaveChangesAsync();

        // Tồn kho chi nhánh (BranchStocks theo lô FEFO)
        var stockRice = new BranchStock
        {
            TenantId = defaultTenantId,
            BranchId = branchQ1.Id,
            ProductId = prodRice.Id,
            SupplierId = supplierRice.Id,
            BatchCode = "BATCH-ST25-20261001",
            ManufactureDate = DateTime.UtcNow.AddMonths(-1),
            ExpiryDate = DateTime.UtcNow.AddMonths(11),
            QuantityOnHand = 150.000m,
            QuantityReserved = 5.000m,
            LandedCostPerUnit = 160000.00m,
            ShelfLocation = "A1-03",
            ZoneArea = "Kho Khô"
        };

        var stockMilk = new BranchStock
        {
            TenantId = defaultTenantId,
            BranchId = branchQ1.Id,
            ProductId = prodMilk.Id,
            SupplierId = supplierVinamilk.Id,
            BatchCode = "BATCH-VNM-20261015",
            ManufactureDate = DateTime.UtcNow.AddDays(-12),
            ExpiryDate = DateTime.UtcNow.AddMonths(5),
            QuantityOnHand = 320.000m,
            QuantityReserved = 0.000m,
            LandedCostPerUnit = 30000.00m,
            ShelfLocation = "B2-01",
            ZoneArea = "Kho Mát"
        };

        var stockSauce = new BranchStock
        {
            TenantId = defaultTenantId,
            BranchId = branchQ1.Id,
            ProductId = prodSauce.Id,
            BatchCode = "BATCH-SAUCE-2026",
            ExpiryDate = DateTime.UtcNow.AddYears(1),
            QuantityOnHand = 100.000m,
            QuantityReserved = 0.000m,
            LandedCostPerUnit = 22000.00m
        };

        var stockG7 = new BranchStock
        {
            TenantId = defaultTenantId,
            BranchId = branchQ1.Id,
            ProductId = prodG7.Id,
            BatchCode = "BATCH-G7-2026",
            ExpiryDate = DateTime.UtcNow.AddYears(2),
            QuantityOnHand = 80.000m,
            QuantityReserved = 0.000m,
            LandedCostPerUnit = 45000.00m
        };

        var stockChoco = new BranchStock
        {
            TenantId = defaultTenantId,
            BranchId = branchQ1.Id,
            ProductId = prodChoco.Id,
            BatchCode = "BATCH-CP-2026",
            ExpiryDate = DateTime.UtcNow.AddMonths(8),
            QuantityOnHand = 60.000m,
            QuantityReserved = 0.000m,
            LandedCostPerUnit = 42000.00m
        };

        var stockMuoi = new BranchStock
        {
            TenantId = defaultTenantId,
            BranchId = branchQ1.Id,
            ProductId = prodMuoi.Id,
            BatchCode = "BATCH-MHH-2026",
            ExpiryDate = DateTime.UtcNow.AddYears(1),
            QuantityOnHand = 150.000m,
            QuantityReserved = 0.000m,
            LandedCostPerUnit = 12000.00m
        };

        var stockBia = new BranchStock
        {
            TenantId = defaultTenantId,
            BranchId = branchQ1.Id,
            ProductId = prodBia.Id,
            BatchCode = "BATCH-BIA-2026",
            ExpiryDate = DateTime.UtcNow.AddMonths(6),
            QuantityOnHand = 120.000m,
            QuantityReserved = 0.000m,
            LandedCostPerUnit = 10500.00m
        };

        await context.BranchStocks.AddRangeAsync(stockRice, stockMilk, stockSauce, stockG7, stockChoco, stockMuoi, stockBia);

        // =========================================================================
        // PHÂN HỆ 5: KHÁCH HÀNG & CHƯƠNG TRÌNH KHUYẾN MÃI
        // =========================================================================
        var customerVip = new Customer
        {
            TenantId = defaultTenantId,
            CustomerCode = "KH-0001",
            Name = "Nguyễn Văn Hưng",
            Phone = "0903112233",
            Email = "hung.nguyen@gmail.com",
            Address = "24 Đường số 5, Phường Đa Kao, Quận 1",
            MembershipTier = "Gold",
            RewardPoints = 250,
            TotalSpending = 12500000.00m,
            AllowDebt = true,
            DebtLimit = 5000000.00m
        };

        var promoOpening = new Promotion
        {
            TenantId = defaultTenantId,
            PromotionCode = "KM-KHAITRUONG-10K",
            PromotionName = "Giảm 10,000đ Mừng Tuần Lễ Mua Sắm",
            Type = "FIXED_AMOUNT",
            DiscountValue = 10000.00m,
            StartDate = DateTime.UtcNow.AddDays(-7),
            EndDate = DateTime.UtcNow.AddDays(30),
            IsActive = true
        };

        await context.Customers.AddAsync(customerVip);
        await context.Promotions.AddAsync(promoOpening);

        // =========================================================================
        // PHÂN HỆ 6: NHÂN SỰ (Hồ sơ, Ca làm việc, Chính sách chấm công) & TÀI CHÍNH
        // =========================================================================
        await EmployeeSeedData.SeedAsync(context, defaultTenantId);

        // Sổ quỹ tiền mặt ban đầu
        var initialCashBook = new CashBook
        {
            BranchId = branchQ1.Id,
            TransactionCode = "CB-INIT-2026",
            SourceDocType = "OPENING_BALANCE",
            TransactionType = "RECEIPT",
            Amount = 10000000.00m,
            OpeningBalance = 0.00m,
            CurrentBalance = 10000000.00m,
            Description = "Nạp tiền lẻ quỹ đầu ca phục vụ thanh toán POS"
        };
        await context.CashBooks.AddAsync(initialCashBook);

        // Nạp 15 danh mục và 5-8 sản phẩm mỗi danh mục
        await CatalogSeedData.SeedAsync(context, defaultTenantId);

        // Lưu toàn bộ dữ liệu mẫu vào CSDL
        await context.SaveChangesAsync();
        await SeedOrdersAndPreOrdersAsync(context);
    }

    public static async Task SeedOrdersAndPreOrdersAsync(AppDbContext context)
    {
        var tenantId = "tenant_mt_mart_01";

        // 1. Đảm bảo có đủ 3 chi nhánh trong hệ thống
        var branchHN = await context.Branches.FirstOrDefaultAsync(b => b.TenantId == tenantId && b.BranchCode == "BR-HN-01");
        var branchQ1 = await context.Branches.FirstOrDefaultAsync(b => b.TenantId == tenantId && b.BranchCode == "BR-HCM-01");
        var branchTX = await context.Branches.FirstOrDefaultAsync(b => b.TenantId == tenantId && b.BranchCode == "BR-HN-02");

        if (branchHN == null)
        {
            branchHN = new Branch
            {
                TenantId = tenantId,
                BranchCode = "BR-HN-01",
                Name = "MT-Mart Chi Nhánh Cầu Giấy",
                Address = "Số 88 Cầu Giấy, Quan Hoa, Cầu Giấy",
                Phone = "024 3766 8899",
                Latitude = 21.0333m,
                Longitude = 105.7833m,
                CheckinRadiusMeters = 150,
                IsActive = true
            };
            await context.Branches.AddAsync(branchHN);
            await context.SaveChangesAsync();
        }

        if (branchQ1 == null)
        {
            branchQ1 = new Branch
            {
                TenantId = tenantId,
                BranchCode = "BR-HCM-01",
                Name = "MT-Mart Chi Nhánh Quận 1",
                Address = "Số 45 Lê Duẩn, Bến Nghé, Quận 1",
                Phone = "028 3822 5566",
                Latitude = 10.7769m,
                Longitude = 106.7009m,
                CheckinRadiusMeters = 100,
                IsActive = true
            };
            await context.Branches.AddAsync(branchQ1);
            await context.SaveChangesAsync();
        }

        if (branchTX == null)
        {
            branchTX = new Branch
            {
                TenantId = tenantId,
                BranchCode = "BR-HN-02",
                Name = "MT-Mart Chi Nhánh Thanh Xuân",
                Address = "Số 129 Nguyễn Trãi, Thượng Đình, Thanh Xuân",
                Phone = "024 3858 6677",
                Latitude = 20.9998m,
                Longitude = 105.8144m,
                CheckinRadiusMeters = 100,
                IsActive = true
            };
            await context.Branches.AddAsync(branchTX);
            await context.SaveChangesAsync();
        }

        // Đảm bảo tồn kho cho cả 3 chi nhánh
        var q1Stocks = await context.BranchStocks.Where(s => s.BranchId == branchQ1.Id).ToListAsync();
        if (q1Stocks.Any())
        {
            if (!await context.BranchStocks.AnyAsync(s => s.BranchId == branchHN.Id))
            {
                foreach (var s in q1Stocks)
                {
                    context.BranchStocks.Add(new BranchStock
                    {
                        TenantId = tenantId,
                        BranchId = branchHN.Id,
                        ProductId = s.ProductId,
                        SupplierId = s.SupplierId,
                        BatchCode = s.BatchCode,
                        ManufactureDate = s.ManufactureDate,
                        ExpiryDate = s.ExpiryDate,
                        QuantityOnHand = s.QuantityOnHand,
                        QuantityReserved = 0,
                        LandedCostPerUnit = s.LandedCostPerUnit,
                        ShelfLocation = s.ShelfLocation,
                        ZoneArea = s.ZoneArea
                    });
                }
            }

            if (!await context.BranchStocks.AnyAsync(s => s.BranchId == branchTX.Id))
            {
                foreach (var s in q1Stocks)
                {
                    context.BranchStocks.Add(new BranchStock
                    {
                        TenantId = tenantId,
                        BranchId = branchTX.Id,
                        ProductId = s.ProductId,
                        SupplierId = s.SupplierId,
                        BatchCode = s.BatchCode,
                        ManufactureDate = s.ManufactureDate,
                        ExpiryDate = s.ExpiryDate,
                        QuantityOnHand = s.QuantityOnHand,
                        QuantityReserved = 0,
                        LandedCostPerUnit = s.LandedCostPerUnit,
                        ShelfLocation = s.ShelfLocation,
                        ZoneArea = s.ZoneArea
                    });
                }
            }
            await context.SaveChangesAsync();
        }

        // Kiểm tra nếu đã có orders thì cập nhật phân bổ chuẩn theo 3 chi nhánh
        if (await context.Orders.AnyAsync(o => o.TenantId == tenantId))
        {
            var ord1 = await context.Orders.FirstOrDefaultAsync(o => o.OrderCode == "HD-20261101-001");
            if (ord1 != null) ord1.BranchId = branchHN.Id;

            var po1 = await context.Orders.FirstOrDefaultAsync(o => o.OrderCode == "PO-20261101-001");
            if (po1 != null) po1.BranchId = branchHN.Id;

            var ord2 = await context.Orders.FirstOrDefaultAsync(o => o.OrderCode == "HD-20261101-002");
            if (ord2 != null) ord2.BranchId = branchTX.Id;

            var po2 = await context.Orders.FirstOrDefaultAsync(o => o.OrderCode == "PO-20261101-002");
            if (po2 != null) po2.BranchId = branchTX.Id;

            var ord3 = await context.Orders.FirstOrDefaultAsync(o => o.OrderCode == "HD-20261101-003");
            if (ord3 != null) ord3.BranchId = branchQ1.Id;

            var ord4 = await context.Orders.FirstOrDefaultAsync(o => o.OrderCode == "HD-20261101-004");
            if (ord4 != null) ord4.BranchId = branchQ1.Id;

            var po3 = await context.Orders.FirstOrDefaultAsync(o => o.OrderCode == "PO-20261031-003");
            if (po3 != null) po3.BranchId = branchQ1.Id;

            await context.SaveChangesAsync();
            return;
        }

        // Đảm bảo có các khách hàng mẫu thân thiết
        var custHung = await context.Customers.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Phone == "0903112233");
        if (custHung == null)
        {
            custHung = new Customer
            {
                TenantId = tenantId,
                CustomerCode = "KH-0001",
                Name = "Nguyễn Văn Hưng",
                Phone = "0903112233",
                Email = "hung.nguyen@gmail.com",
                Address = "24 Đường số 5, Phường Đa Kao, Quận 1",
                MembershipTier = "Gold",
                RewardPoints = 250,
                TotalSpending = 12500000m,
                AllowDebt = true,
                TotalDebt = 0m,
            };
            await context.Customers.AddAsync(custHung);
        }

        var custKhach = await context.Customers.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Phone == "0912345678");
        if (custKhach == null)
        {
            custKhach = new Customer
            {
                TenantId = tenantId,
                CustomerCode = "KH-POS-001",
                Name = "Nguyễn Văn Khách (Thân Thiết)",
                Phone = "0912345678",
                Email = "khach.pos@gmail.com",
                Address = "25 Cầu Giấy, Hà Nội",
                MembershipTier = "Gold",
                RewardPoints = 250,
                TotalSpending = 5500000m,
                AllowDebt = true,
                TotalDebt = 350000m,
            };
            await context.Customers.AddAsync(custKhach);
        }

        var custLan = await context.Customers.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Phone == "0987654321");
        if (custLan == null)
        {
            custLan = new Customer
            {
                TenantId = tenantId,
                CustomerCode = "KH-POS-002",
                Name = "Chị Lan Tạp Hóa Xóm",
                Phone = "0987654321",
                Email = "lan.taphoa@gmail.com",
                Address = "88 Nguyễn Trãi, Thanh Xuân, Hà Nội",
                MembershipTier = "VIP",
                RewardPoints = 580,
                TotalSpending = 18500000m,
                AllowDebt = true,
                TotalDebt = 2100000m,
            };
            await context.Customers.AddAsync(custLan);
        }

        await context.SaveChangesAsync();

        // Lấy sản phẩm có trong kho
        var products = await context.Products.Where(p => p.TenantId == tenantId).ToListAsync();
        var prodGao = products.FirstOrDefault(p => p.Barcode == "893850123401") ?? products.FirstOrDefault();
        var prodSua = products.FirstOrDefault(p => p.Barcode == "893850123402") ?? products.Skip(1).FirstOrDefault() ?? prodGao;
        var prodNuocMam = products.FirstOrDefault(p => p.Barcode == "893850123403") ?? products.Skip(2).FirstOrDefault() ?? prodGao;
        var prodDauAn = products.FirstOrDefault(p => p.Barcode == "893850123405") ?? products.Skip(3).FirstOrDefault() ?? prodGao;

        var now = DateTime.UtcNow;

        // 1. HÓA ĐƠN TIỀN MẶT ĐÃ HOÀN THÀNH (Chi nhánh Cầu Giấy)
        var order1 = new Order
        {
            TenantId = tenantId,
            BranchId = branchHN.Id,
            CustomerId = custHung.Id,
            OrderCode = "HD-20261101-001",
            OrderDate = now.AddHours(-3),
            TotalAmount = 66500m,
            DiscountAmount = 0m,
            FinalAmount = 66500m,
            DepositAmount = 100000m, // tiền khách đưa
            PaymentMethod = "CASH",
            PaymentStatus = "PAID",
            Status = "Completed",
            Note = "Thanh toán đủ tiền mặt tại quầy"
        };
        if (prodSua != null)
        {
            order1.Details.Add(new OrderDetail
            {
                OrderId = order1.Id,
                ProductId = prodSua.Id,
                LineNumber = 1,
                Qty = 1,
                UnitPrice = 38000m,
                DiscountAmount = 0m,
                ActualPrice = 38000m,
                AppliedVatRate = 0.01m,
                VatAmount = 380m
            });
        }
        if (prodNuocMam != null)
        {
            order1.Details.Add(new OrderDetail
            {
                OrderId = order1.Id,
                ProductId = prodNuocMam.Id,
                LineNumber = 2,
                Qty = 1,
                UnitPrice = 28500m,
                DiscountAmount = 0m,
                ActualPrice = 28500m,
                AppliedVatRate = 0.01m,
                VatAmount = 285m
            });
        }

        // 2. HÓA ĐƠN VIETQR ĐÃ HOÀN THÀNH (Chi nhánh Thanh Xuân)
        var order2 = new Order
        {
            TenantId = tenantId,
            BranchId = branchTX.Id,
            CustomerId = custKhach.Id,
            OrderCode = "HD-20261101-002",
            OrderDate = now.AddHours(-2),
            TotalAmount = 260000m,
            DiscountAmount = 0m,
            FinalAmount = 260000m,
            DepositAmount = 260000m,
            PaymentMethod = "VIETQR",
            PaymentStatus = "PAID",
            Status = "Completed",
            Note = "Quét VietQR chuyển khoản thành công"
        };
        if (prodGao != null)
        {
            order2.Details.Add(new OrderDetail
            {
                OrderId = order2.Id,
                ProductId = prodGao.Id,
                LineNumber = 1,
                Qty = 1,
                UnitPrice = 195000m,
                DiscountAmount = 0m,
                ActualPrice = 195000m,
                AppliedVatRate = 0.01m,
                VatAmount = 1950m
            });
        }
        if (prodDauAn != null)
        {
            order2.Details.Add(new OrderDetail
            {
                OrderId = order2.Id,
                ProductId = prodDauAn.Id,
                LineNumber = 2,
                Qty = 1,
                UnitPrice = 65000m,
                DiscountAmount = 0m,
                ActualPrice = 65000m,
                AppliedVatRate = 0.01m,
                VatAmount = 650m
            });
        }

        // 3. HÓA ĐƠN BÁN NỢ CHO CHỊ LAN (Chi nhánh Quận 1)
        var order3 = new Order
        {
            TenantId = tenantId,
            BranchId = branchQ1.Id,
            CustomerId = custLan.Id,
            OrderCode = "HD-20261101-003",
            OrderDate = now.AddMinutes(-45),
            TotalAmount = 522500m,
            DiscountAmount = 0m,
            FinalAmount = 522500m,
            DepositAmount = 0m,
            PaymentMethod = "DEBT",
            PaymentStatus = "UNPAID",
            Status = "Completed",
            Note = "Chủ tiệm duyệt bán nợ ghi sổ tạp hóa"
        };
        if (prodSua != null)
        {
            order3.Details.Add(new OrderDetail
            {
                OrderId = order3.Id,
                ProductId = prodSua.Id,
                LineNumber = 1,
                Qty = 10,
                UnitPrice = 38000m,
                DiscountAmount = 0m,
                ActualPrice = 38000m,
                AppliedVatRate = 0.01m,
                VatAmount = 3800m
            });
        }
        if (prodNuocMam != null)
        {
            order3.Details.Add(new OrderDetail
            {
                OrderId = order3.Id,
                ProductId = prodNuocMam.Id,
                LineNumber = 2,
                Qty = 5,
                UnitPrice = 28500m,
                DiscountAmount = 0m,
                ActualPrice = 28500m,
                AppliedVatRate = 0.01m,
                VatAmount = 1425m
            });
        }

        // 4. HÓA ĐƠN ĐÃ HỦY (Chi nhánh Quận 1)
        var order4 = new Order
        {
            TenantId = tenantId,
            BranchId = branchQ1.Id,
            CustomerId = null,
            OrderCode = "HD-20261101-004",
            OrderDate = now.AddMinutes(-20),
            TotalAmount = 38000m,
            DiscountAmount = 0m,
            FinalAmount = 38000m,
            DepositAmount = 0m,
            PaymentMethod = "CASH",
            PaymentStatus = "UNPAID",
            Status = "Cancelled",
            Note = "Khách đổi ý trả lại hàng"
        };
        if (prodSua != null)
        {
            order4.Details.Add(new OrderDetail
            {
                OrderId = order4.Id,
                ProductId = prodSua.Id,
                LineNumber = 1,
                Qty = 1,
                UnitPrice = 38000m,
                DiscountAmount = 0m,
                ActualPrice = 38000m
            });
        }

        // 5. ĐƠN ĐẶT TRƯỚC GIỮ KHO 1 (Chi nhánh Cầu Giấy)
        var preOrder1 = new Order
        {
            TenantId = tenantId,
            BranchId = branchHN.Id,
            CustomerId = custLan.Id,
            OrderCode = "PO-20261101-001",
            OrderDate = now.AddHours(-4),
            TotalAmount = 1735000m,
            DiscountAmount = 0m,
            FinalAmount = 1735000m,
            DepositAmount = 500000m, // Đã đặt cọc 500k
            PaymentMethod = "VIETQR",
            PaymentStatus = "PARTIAL",
            Status = "PreOrder",
            Note = "Hẹn nhận hàng lúc 17:30 chiều nay tại quầy siêu thị"
        };
        if (prodGao != null)
        {
            preOrder1.Details.Add(new OrderDetail
            {
                OrderId = preOrder1.Id,
                ProductId = prodGao.Id,
                LineNumber = 1,
                Qty = 5,
                UnitPrice = 195000m,
                ActualPrice = 195000m
            });
        }
        if (prodSua != null)
        {
            preOrder1.Details.Add(new OrderDetail
            {
                OrderId = preOrder1.Id,
                ProductId = prodSua.Id,
                LineNumber = 2,
                Qty = 20,
                UnitPrice = 38000m,
                ActualPrice = 38000m
            });
        }

        // 6. ĐƠN ĐẶT TRƯỚC GIỮ KHO 2 (Chi nhánh Thanh Xuân)
        var preOrder2 = new Order
        {
            TenantId = tenantId,
            BranchId = branchTX.Id,
            CustomerId = custHung.Id,
            OrderCode = "PO-20261101-002",
            OrderDate = now.AddHours(-1),
            TotalAmount = 850000m,
            DiscountAmount = 0m,
            FinalAmount = 850000m,
            DepositAmount = 300000m, // Đã cọc 300k
            PaymentMethod = "CASH",
            PaymentStatus = "PARTIAL",
            Status = "PreOrder",
            Note = "Hẹn nhận lúc 18:00 tối nay. Soạn giỏ quà tặng thực phẩm sạch."
        };
        if (prodNuocMam != null)
        {
            preOrder2.Details.Add(new OrderDetail
            {
                OrderId = preOrder2.Id,
                ProductId = prodNuocMam.Id,
                LineNumber = 1,
                Qty = 10,
                UnitPrice = 28500m,
                ActualPrice = 28500m
            });
        }
        if (prodDauAn != null)
        {
            preOrder2.Details.Add(new OrderDetail
            {
                OrderId = preOrder2.Id,
                ProductId = prodDauAn.Id,
                LineNumber = 2,
                Qty = 5,
                UnitPrice = 65000m,
                ActualPrice = 65000m
            });
        }

        // 7. ĐƠN ĐẶT TRƯỚC ĐÃ ĐẾN NHẬN VÀ HOÀN TẤT (Chi nhánh Quận 1)
        var preOrder3 = new Order
        {
            TenantId = tenantId,
            BranchId = branchQ1.Id,
            CustomerId = custKhach.Id,
            OrderCode = "PO-20261031-003",
            OrderDate = now.AddDays(-1),
            TotalAmount = 585000m,
            DiscountAmount = 0m,
            FinalAmount = 585000m,
            DepositAmount = 200000m,
            PaymentMethod = "CASH",
            PaymentStatus = "PAID",
            Status = "Completed",
            Note = "Khách đã đến quầy nhận hàng đủ hôm qua"
        };
        if (prodGao != null)
        {
            preOrder3.Details.Add(new OrderDetail
            {
                OrderId = preOrder3.Id,
                ProductId = prodGao.Id,
                LineNumber = 1,
                Qty = 3,
                UnitPrice = 195000m,
                ActualPrice = 195000m
            });
        }

        await context.Orders.AddRangeAsync(order1, order2, order3, order4, preOrder1, preOrder2, preOrder3);
        await context.SaveChangesAsync();
    }
}
