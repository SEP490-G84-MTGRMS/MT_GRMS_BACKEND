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

        // 2. Kiểm tra nếu đã có dữ liệu mẫu thì bỏ qua việc seed
        if (await context.SubscriptionPlans.AnyAsync())
        {
            await CatalogSeedData.SeedAsync(context, "tenant_mt_mart_01");
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

        await context.SubscriptionPlans.AddRangeAsync(planFree, planPro);
        await context.SaveChangesAsync();

        // Doanh nghiệp thuê SaaS mẫu (Default Tenant)
        var defaultTenantId = "tenant_mt_mart_01";
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

        await context.Users.AddRangeAsync(userAdmin, userStoreManager, userCashier);

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

        await context.TenantUsers.AddRangeAsync(tenantUserAdmin, tenantUserManager, tenantUserCashier);
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

        await context.Branches.AddRangeAsync(branchQ1, branchHN);
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
            Status = "Active",
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
            Status = "Active",
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
            Status = "Active"
        };

        await context.Products.AddRangeAsync(prodRice, prodMilk, prodSauce);
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

        await context.BranchStocks.AddRangeAsync(stockRice, stockMilk);

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
        // PHÂN HỆ 6: NHÂN SỰ & TÀI CHÍNH
        // =========================================================================
        var employeeManager = new Employee
        {
            TenantId = defaultTenantId,
            BranchId = branchQ1.Id,
            UserId = userStoreManager.Id,
            EmployeeCode = "NV-001",
            FullName = userStoreManager.FullName,
            CitizenId = "079095001234",
            DateOfBirth = new DateTime(1995, 5, 20),
            Gender = "Nam",
            Phone = userStoreManager.Phone!,
            Email = userStoreManager.Email,
            JobTitle = "Cửa Hàng Trưởng",
            SalaryType = "monthly",
            BaseRate = 15000000.00m,
            LunchAllowance = 730000.00m,
            TravelAllowance = 500000.00m,
            Status = "active"
        };

        var employeeCashier = new Employee
        {
            TenantId = defaultTenantId,
            BranchId = branchQ1.Id,
            UserId = userCashier.Id,
            EmployeeCode = "NV-002",
            FullName = userCashier.FullName,
            CitizenId = "079098005678",
            DateOfBirth = new DateTime(1998, 10, 12),
            Gender = "Nữ",
            Phone = userCashier.Phone!,
            Email = userCashier.Email,
            JobTitle = "Nhân Viên Thu Ngân POS",
            SalaryType = "monthly",
            BaseRate = 8500000.00m,
            LunchAllowance = 730000.00m,
            Status = "active"
        };

        await context.Employees.AddRangeAsync(employeeManager, employeeCashier);

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
    }
}
