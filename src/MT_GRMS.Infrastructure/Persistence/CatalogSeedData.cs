using Microsoft.EntityFrameworkCore;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Infrastructure.Persistence;

/// <summary>
/// Dữ liệu mẫu chuẩn hóa phong phú cho phân hệ Quản lý Hàng hóa & Danh mục (Catalog).
/// Cung cấp 15 danh mục siêu thị tiêu chuẩn, ~94 sản phẩm thực tế kèm hình ảnh Unsplash,
/// mô tả chi tiết, ghi chú nội bộ, đơn vị quy đổi (ProductUnits) và ngày tạo tự nhiên.
/// </summary>
public static class CatalogSeedData
{
    public class SeedUnitItem
    {
        public string UnitName { get; set; } = string.Empty;
        public decimal ConversionRate { get; set; }
        public decimal Price { get; set; }
        public string? Barcode { get; set; }
    }

    public class SeedProductItem
    {
        public string Code { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string BaseUnit { get; set; } = string.Empty;
        public decimal CostPrice { get; set; }
        public decimal SellingPrice { get; set; }
        public decimal MinStock { get; set; } = 10m;
        public decimal MaxStock { get; set; } = 500m;
        public decimal VatRate { get; set; } = 8m;
        public string TaxCategory { get; set; } = "STANDARD_1PCT";
        public string Status { get; set; } = "Active";
        public List<string> ImageUrls { get; set; } = new();
        public string? Description { get; set; }
        public string? InternalNote { get; set; }
        public bool HasBatches { get; set; } = false;
        public bool IsFavorite { get; set; } = false;
        public int CreatedDaysAgo { get; set; } = 0;
        public int CreatedHoursAgo { get; set; } = 0;
        public List<SeedUnitItem> Units { get; set; } = new();
    }

    public class SeedCategoryItem
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<SeedProductItem> Products { get; set; } = new();
    }

    public static async Task SeedAsync(AppDbContext context, string tenantId)
    {
        var categoryTemplates = Get15CategoryTemplates();

        foreach (var catTpl in categoryTemplates)
        {
            var category = await context.Categories
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.CategoryCode == catTpl.Code);

            if (category == null)
            {
                category = new Category
                {
                    TenantId = tenantId,
                    CategoryCode = catTpl.Code,
                    Name = catTpl.Name,
                    Description = catTpl.Description
                };
                await context.Categories.AddAsync(category);
                await context.SaveChangesAsync();
            }
            else
            {
                category.Name = catTpl.Name;
                category.Description = catTpl.Description;
            }

            foreach (var prodTpl in catTpl.Products)
            {
                var product = await context.Products
                    .IgnoreQueryFilters()
                    .Include(p => p.Units)
                    .FirstOrDefaultAsync(p => p.TenantId == tenantId && (p.ProductCode == prodTpl.Code || p.Barcode == prodTpl.Barcode));

                var calculatedCreatedAt = DateTime.UtcNow.AddDays(-prodTpl.CreatedDaysAgo).AddHours(-prodTpl.CreatedHoursAgo);

                if (product == null)
                {
                    product = new Product
                    {
                        TenantId = tenantId,
                        CategoryId = category.Id,
                        ProductCode = prodTpl.Code,
                        Barcode = prodTpl.Barcode,
                        Name = prodTpl.Name,
                        BaseUnit = prodTpl.BaseUnit,
                        CostPrice = prodTpl.CostPrice,
                        SellingPrice = prodTpl.SellingPrice,
                        VatRate = prodTpl.VatRate,
                        TaxCategory = prodTpl.TaxCategory,
                        ProductType = "STANDARD",
                        Status = prodTpl.Status,
                        ImageUrls = prodTpl.ImageUrls,
                        Description = prodTpl.Description,
                        InternalNote = prodTpl.InternalNote,
                        IsFavorite = prodTpl.IsFavorite,
                        CreatedAt = calculatedCreatedAt
                    };

                    await context.Products.AddAsync(product);
                    await context.SaveChangesAsync();
                }
                else
                {
                    product.CategoryId = category.Id;
                    product.Name = prodTpl.Name;
                    product.Barcode = prodTpl.Barcode;
                    product.BaseUnit = prodTpl.BaseUnit;
                    product.CostPrice = prodTpl.CostPrice;
                    product.SellingPrice = prodTpl.SellingPrice;
                    product.VatRate = prodTpl.VatRate;
                    product.TaxCategory = prodTpl.TaxCategory;
                    product.Status = prodTpl.Status;
                    product.ImageUrls = prodTpl.ImageUrls;
                    product.Description = prodTpl.Description;
                    product.InternalNote = prodTpl.InternalNote;
                    product.IsFavorite = prodTpl.IsFavorite;
                    product.CreatedAt = calculatedCreatedAt;
                    product.UpdatedAt = DateTime.UtcNow;
                }

                // Cập nhật hoặc bổ sung các đơn vị quy đổi (ProductUnits)
                if (prodTpl.Units.Any())
                {
                    var existingUnits = await context.ProductUnits
                        .Where(u => u.ProductId == product.Id)
                        .ToListAsync();

                    foreach (var uTpl in prodTpl.Units)
                    {
                        var match = existingUnits.FirstOrDefault(u => u.UnitName.Trim().ToLower() == uTpl.UnitName.Trim().ToLower());
                        if (match == null)
                        {
                            var newUnit = new ProductUnit(
                                product.Id,
                                uTpl.UnitName,
                                uTpl.ConversionRate,
                                uTpl.Price,
                                uTpl.Barcode,
                                false
                            );
                            await context.ProductUnits.AddAsync(newUnit);
                        }
                        else
                        {
                            match.ConversionRate = uTpl.ConversionRate;
                            match.Price = uTpl.Price;
                            if (!string.IsNullOrWhiteSpace(uTpl.Barcode))
                            {
                                match.Barcode = uTpl.Barcode;
                            }
                        }
                    }
                }
            }
        }

        await context.SaveChangesAsync();
    }

    public static List<SeedCategoryItem> Get15CategoryTemplates()
    {
        return new List<SeedCategoryItem>
        {
            // 1. Sữa & Đồ Uống Giải Khát
            new SeedCategoryItem
            {
                Code = "BEV",
                Name = "Sữa & Đồ Uống Giải Khát",
                Description = "Sữa tươi tiệt trùng, nước ngọt có ga, nước khoáng và trà giải nhiệt",
                Products = new List<SeedProductItem>
                {
                    new()
                    {
                        Code = "SP-BEV-001", Barcode = "893850120101", Name = "Nước ngọt Coca Cola lon 330ml", BaseUnit = "Lon",
                        CostPrice = 8500m, SellingPrice = 11000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT", IsFavorite = true,
                        CreatedDaysAgo = 0, CreatedHoursAgo = 2,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1622483767028-3f66f32aef97?w=500&auto=format&fit=crop&q=60" },
                        Description = "Nước giải khát có ga Coca-Cola chính hãng lon 330ml mát lạnh, sảng khoái.",
                        InternalNote = "Vị trí tủ mát Kệ A1. Nhập hàng tuần từ NPP Coca-Cola.",
                        Units = new()
                        {
                            new() { UnitName = "Lốc", ConversionRate = 6m, Price = 62000m, Barcode = "8938501201016" },
                            new() { UnitName = "Thùng", ConversionRate = 24m, Price = 240000m, Barcode = "2893850120101" }
                        }
                    },
                    new()
                    {
                        Code = "SP-BEV-002", Barcode = "893850120102", Name = "Nước ngọt Pepsi lon 320ml", BaseUnit = "Lon",
                        CostPrice = 8000m, SellingPrice = 10500m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 0, CreatedHoursAgo = 5,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1581009146145-b5ef050c2e1e?w=500&auto=format&fit=crop&q=60" },
                        Description = "Pepsi vị cola tươi mát, bùng nổ năng lượng, dung tích 320ml tiện lợi.",
                        InternalNote = "Bảo quản nhiệt độ phòng hoặc tủ mát 4-8 độ C.",
                        Units = new()
                        {
                            new() { UnitName = "Lốc", ConversionRate = 6m, Price = 60000m, Barcode = "8938501201026" },
                            new() { UnitName = "Thùng", ConversionRate = 24m, Price = 230000m, Barcode = "2893850120102" }
                        }
                    },
                    new()
                    {
                        Code = "SP-BEV-003", Barcode = "893850120103", Name = "Nước khoáng tự nhiên Lavie chai 500ml", BaseUnit = "Chai",
                        CostPrice = 4000m, SellingPrice = 6000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 1, CreatedHoursAgo = 3,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1548839140-29a749e1bc4e?w=500&auto=format&fit=crop&q=60" },
                        Description = "Nước khoáng thiên nhiên nguyên chất từ nguồn khoáng ngầm LaVie.",
                        InternalNote = "Sản phẩm bán chạy mùa hè. Kệ trưng bày trung tâm.",
                        Units = new()
                        {
                            new() { UnitName = "Lốc", ConversionRate = 6m, Price = 34000m, Barcode = "8938501201036" },
                            new() { UnitName = "Thùng", ConversionRate = 24m, Price = 130000m, Barcode = "2893850120103" }
                        }
                    },
                    new()
                    {
                        Code = "SP-BEV-004", Barcode = "893850120104", Name = "Trà xanh Không Độ chai 455ml", BaseUnit = "Chai",
                        CostPrice = 7500m, SellingPrice = 10000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 1, CreatedHoursAgo = 8,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1608256246200-53e635b5b65f?w=500&auto=format&fit=crop&q=60" },
                        Description = "Trà xanh đóng chai với hợp chất EGCG giải nhiệt cuộc sống.",
                        InternalNote = "Hàng bán lẻ và theo lốc tại quầy thanh toán.",
                        Units = new()
                        {
                            new() { UnitName = "Lốc", ConversionRate = 6m, Price = 56000m, Barcode = "8938501201046" },
                            new() { UnitName = "Thùng", ConversionRate = 24m, Price = 215000m, Barcode = "2893850120104" }
                        }
                    },
                    new()
                    {
                        Code = "SP-BEV-005", Barcode = "893850120105", Name = "Nước tăng lực Redbull lon 250ml", BaseUnit = "Lon",
                        CostPrice = 11000m, SellingPrice = 15000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT", IsFavorite = true,
                        CreatedDaysAgo = 2, CreatedHoursAgo = 1,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1551024709-8f23befc6f87?w=500&auto=format&fit=crop&q=60" },
                        Description = "Nước tăng lực Red Bull nắp vàng Thái Lan lon 250ml tỉnh táo tức thì.",
                        InternalNote = "Sản phẩm chủ lực quầy thu ngân.",
                        Units = new()
                        {
                            new() { UnitName = "Lốc", ConversionRate = 6m, Price = 86000m, Barcode = "8938501201056" },
                            new() { UnitName = "Thùng", ConversionRate = 24m, Price = 330000m, Barcode = "2893850120105" }
                        }
                    },
                    new()
                    {
                        Code = "SP-BEV-006", Barcode = "893850120106", Name = "Sữa tươi tiệt trùng Vinamilk có đường 1L", BaseUnit = "Hộp",
                        CostPrice = 30000m, SellingPrice = 38000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 2, CreatedHoursAgo = 6,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1556881286-fc6915169721?w=500&auto=format&fit=crop&q=60" },
                        Description = "Sữa tươi tiệt trùng 100% sữa bò tươi nguyên chất Vinamilk hộp 1 lít.",
                        InternalNote = "Kiểm tra hạn sử dụng khi nhập hàng (tối thiểu còn 4 tháng).",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 12m, Price = 445000m, Barcode = "8938501201066" }
                        }
                    },
                    new()
                    {
                        Code = "SP-BEV-007", Barcode = "893850120107", Name = "Sữa chua uống tiệt trùng Yomost dâu lốc 4 hộp", BaseUnit = "Lốc",
                        CostPrice = 24000m, SellingPrice = 31000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 3, CreatedHoursAgo = 4,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1559839914-1b31645a221a?w=500&auto=format&fit=crop&q=60" },
                        Description = "Sữa chua lên men tự nhiên kết hợp hương dâu thơm ngon Yomost 170ml.",
                        InternalNote = "Trưng bày kệ sữa mát hoặc kệ khô thoáng mát.",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 12m, Price = 360000m, Barcode = "8938501201076" }
                        }
                    }
                }
            },

            // 2. Bách Hóa Tiêu Dùng & Gạo Hạt
            new SeedCategoryItem
            {
                Code = "DRY",
                Name = "Bách Hóa Tiêu Dùng & Gạo Hạt",
                Description = "Gạo đặc sản, các loại đậu hạt dinh dưỡng và thực phẩm khô",
                Products = new List<SeedProductItem>
                {
                    new()
                    {
                        Code = "SP-DRY-001", Barcode = "893850120201", Name = "Gạo thơm ST25 Ông Cua túi 5kg", BaseUnit = "Túi",
                        CostPrice = 160000m, SellingPrice = 195000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT", HasBatches = true, IsFavorite = true,
                        CreatedDaysAgo = 3, CreatedHoursAgo = 9,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1586201375761-83865001e31c?w=500&auto=format&fit=crop&q=60" },
                        Description = "Gạo ST25 chính hãng DNTN Hồ Quang Cua đạt giải gạo ngon nhất thế giới.",
                        InternalNote = "Bảo quản cao ráo, tránh ẩm mốc và mối mọt.",
                        Units = new()
                        {
                            new() { UnitName = "Bao 25kg", ConversionRate = 5m, Price = 950000m, Barcode = "8938501202015" }
                        }
                    },
                    new()
                    {
                        Code = "SP-DRY-002", Barcode = "893850120202", Name = "Gạo Nàng Thơm Chợ Đào túi 5kg", BaseUnit = "Túi",
                        CostPrice = 110000m, SellingPrice = 135000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 4, CreatedHoursAgo = 2,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1536304993881-ff6e9eefa2a6?w=500&auto=format&fit=crop&q=60" },
                        Description = "Gạo thơm dẻo đặc sản Long An túi 5kg nguyên chất không pha trộn.",
                        InternalNote = "Kệ gạo hàng số 1.",
                        Units = new()
                        {
                            new() { UnitName = "Bao 25kg", ConversionRate = 5m, Price = 650000m, Barcode = "8938501202025" }
                        }
                    },
                    new()
                    {
                        Code = "SP-DRY-003", Barcode = "893850120203", Name = "Đậu xanh bóc vỏ túi 500g", BaseUnit = "Túi",
                        CostPrice = 25000m, SellingPrice = 33000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 4, CreatedHoursAgo = 7,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1515543237350-b3eea1ec8082?w=500&auto=format&fit=crop&q=60" },
                        Description = "Đậu xanh đãi sạch vỏ, hạt đều, chuyên dùng nấu chè và làm bánh.",
                        InternalNote = "Kệ ngũ cốc khô.",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 20m, Price = 620000m, Barcode = "8938501202030" }
                        }
                    },
                    new()
                    {
                        Code = "SP-DRY-004", Barcode = "893850120204", Name = "Đậu đen xanh lòng túi 500g", BaseUnit = "Túi",
                        CostPrice = 28000m, SellingPrice = 36000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 5, CreatedHoursAgo = 3,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1515543237350-b3eea1ec8082?w=500&auto=format&fit=crop&q=60" },
                        Description = "Đậu đen hạt nhỏ xanh lòng tự nhiên, giàu dinh dưỡng, thanh nhiệt.",
                        InternalNote = "Kệ ngũ cốc khô.",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 20m, Price = 680000m, Barcode = "8938501202040" }
                        }
                    },
                    new()
                    {
                        Code = "SP-DRY-005", Barcode = "893850120205", Name = "Nếp cái hoa vàng gói 1kg", BaseUnit = "Gói",
                        CostPrice = 32000m, SellingPrice = 42000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 5, CreatedHoursAgo = 8,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1586201375761-83865001e31c?w=500&auto=format&fit=crop&q=60" },
                        Description = "Gạo nếp cái hoa vàng dẻo quánh, thơm nức, thích hợp thổi xôi và làm bánh chưng.",
                        InternalNote = "Kệ gạo hàng số 1."
                    },
                    new()
                    {
                        Code = "SP-DRY-006", Barcode = "893850120206", Name = "Hạt sen sấy giòn ăn liền gói 250g", BaseUnit = "Gói",
                        CostPrice = 55000m, SellingPrice = 72000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 6, CreatedHoursAgo = 4,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1515543237350-b3eea1ec8082?w=500&auto=format&fit=crop&q=60" },
                        Description = "Hạt sen sấy chân không giòn rụm nguyên vị tự nhiên, đặc sản Đồng Tháp.",
                        InternalNote = "Kệ đồ ăn nhẹ dinh dưỡng."
                    }
                }
            },

            // 3. Thực Phẩm & Nông Sản Tươi Sống
            new SeedCategoryItem
            {
                Code = "FRESH",
                Name = "Thực Phẩm & Nông Sản Tươi Sống",
                Description = "Thịt bò, thịt heo, phi lê gà và thủy hải sản tươi sạch VietGAP",
                Products = new List<SeedProductItem>
                {
                    new()
                    {
                        Code = "SP-FRH-001", Barcode = "893850120301", Name = "Thịt ba chỉ bò Mỹ đông lạnh khay 500g", BaseUnit = "Khay",
                        CostPrice = 85000m, SellingPrice = 115000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT", HasBatches = true, IsFavorite = true,
                        CreatedDaysAgo = 6, CreatedHoursAgo = 10,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1603048588665-791ca8aea617?w=500&auto=format&fit=crop&q=60" },
                        Description = "Thịt ba chỉ bò Mỹ cuộn lát mỏng chuẩn ăn lẩu và nướng BBQ tại nhà.",
                        InternalNote = "Bảo quản ngăn đông sâu -18 độ C."
                    },
                    new()
                    {
                        Code = "SP-FRH-002", Barcode = "893850120302", Name = "Ức gà phi lê tươi CP khay 500g", BaseUnit = "Khay",
                        CostPrice = 38000m, SellingPrice = 52000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 7, CreatedHoursAgo = 3,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1604503468506-a8da13d82791?w=500&auto=format&fit=crop&q=60" },
                        Description = "Ức gà phi lê sạch da không xương chuẩn CP VietGAP cho người tập gym.",
                        InternalNote = "Tủ mát thịt tươi 0-4 độ C."
                    },
                    new()
                    {
                        Code = "SP-FRH-003", Barcode = "893850120303", Name = "Cá hồi Na Uy phi lê tươi khay 300g", BaseUnit = "Khay",
                        CostPrice = 155000m, SellingPrice = 205000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 7, CreatedHoursAgo = 9,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1519708227418-c8fd9a32b7a2?w=500&auto=format&fit=crop&q=60" },
                        Description = "Cá hồi Na Uy tươi nhập khẩu nguyên con, phi lê trong ngày có chứng nhận xuất xứ.",
                        InternalNote = "Bảo quản đá vảy 0-2 độ C. Tiêu thụ trong vòng 3 ngày."
                    },
                    new()
                    {
                        Code = "SP-FRH-004", Barcode = "893850120304", Name = "Thịt nạc dăm heo MeatDeli khay 400g", BaseUnit = "Khay",
                        CostPrice = 65000m, SellingPrice = 86000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 8, CreatedHoursAgo = 4,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1603048588665-791ca8aea617?w=500&auto=format&fit=crop&q=60" },
                        Description = "Thịt heo mát MeatDeli công nghệ Oxy-Fresh 9 ngày tươi ngon, mỡ nạc xen kẽ.",
                        InternalNote = "Tủ mát thịt tươi 0-4 độ C."
                    },
                    new()
                    {
                        Code = "SP-FRH-005", Barcode = "893850120305", Name = "Tôm thẻ chân trắng tươi sống khay 500g", BaseUnit = "Khay",
                        CostPrice = 90000m, SellingPrice = 125000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 8, CreatedHoursAgo = 8,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1551024709-8f23befc6f87?w=500&auto=format&fit=crop&q=60" },
                        Description = "Tôm thẻ chân trắng vùng nuôi Bến Tre tươi ngon, vỏ mỏng thịt chắc ngọt.",
                        InternalNote = "Bảo quản khay đá lạnh."
                    },
                    new()
                    {
                        Code = "SP-FRH-006", Barcode = "893850120306", Name = "Sườn non heo tươi sạch MeatDeli khay 500g", BaseUnit = "Khay",
                        CostPrice = 95000m, SellingPrice = 129000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 9, CreatedHoursAgo = 5,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1603048588665-791ca8aea617?w=500&auto=format&fit=crop&q=60" },
                        Description = "Sườn non heo nhiều nạc sụn mềm, thích hợp nấu canh hoặc rim chua ngọt.",
                        InternalNote = "Tủ mát thịt tươi 0-4 độ C."
                    }
                }
            },

            // 4. Gia Vị & Phụ Gia Nấu Ăn
            new SeedCategoryItem
            {
                Code = "SPICE",
                Name = "Gia Vị & Phụ Gia Nấu Ăn",
                Description = "Nước mắm, dầu ăn, hạt nêm, nước tương và sốt ướp",
                Products = new List<SeedProductItem>
                {
                    new()
                    {
                        Code = "SP-SPC-001", Barcode = "893850120401", Name = "Nước mắm Nam Ngư Đệ Nhị chai 900ml", BaseUnit = "Chai",
                        CostPrice = 22000m, SellingPrice = 28500m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 9, CreatedHoursAgo = 11,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1589301760014-d929f3979dbc?w=500&auto=format&fit=crop&q=60" },
                        Description = "Nước mắm Nam Ngư Đệ Nhị hương vị cá cơm thơm ngon đậm đà truyền thống.",
                        InternalNote = "Kệ gia vị mặn hàng 2.",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 15m, Price = 410000m, Barcode = "8938501204015" }
                        }
                    },
                    new()
                    {
                        Code = "SP-SPC-002", Barcode = "893850120402", Name = "Nước tương Maggi Đậm Đặc chai 700ml", BaseUnit = "Chai",
                        CostPrice = 21000m, SellingPrice = 27000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 10, CreatedHoursAgo = 2,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1589301760014-d929f3979dbc?w=500&auto=format&fit=crop&q=60" },
                        Description = "Nước tương đậu nành lên men tự nhiên Maggi đậm đặc thơm ngon.",
                        InternalNote = "Kệ gia vị hàng 2.",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 12m, Price = 310000m, Barcode = "8938501204022" }
                        }
                    },
                    new()
                    {
                        Code = "SP-SPC-003", Barcode = "893850120403", Name = "Dầu ăn Tường An Cooking Oil chai 1L", BaseUnit = "Chai",
                        CostPrice = 42000m, SellingPrice = 53000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT", IsFavorite = true,
                        CreatedDaysAgo = 10, CreatedHoursAgo = 7,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1474979266404-7eaacbcd87c5?w=500&auto=format&fit=crop&q=60" },
                        Description = "Dầu thực vật tinh luyện Tường An giàu Omega 3-6-9 tốt cho tim mạch.",
                        InternalNote = "Kệ dầu ăn dầu hào.",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 12m, Price = 620000m, Barcode = "8938501204033" }
                        }
                    },
                    new()
                    {
                        Code = "SP-SPC-004", Barcode = "893850120404", Name = "Bột ngọt Ajinomoto hạt lớn gói 454g", BaseUnit = "Gói",
                        CostPrice = 31000m, SellingPrice = 39000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 11, CreatedHoursAgo = 3,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1506368249639-73a05d6f6488?w=500&auto=format&fit=crop&q=60" },
                        Description = "Mì chính Ajinomoto hạt lớn sản xuất từ mật mía đường thiên nhiên.",
                        InternalNote = "Kệ hạt nêm mì chính."
                    },
                    new()
                    {
                        Code = "SP-SPC-005", Barcode = "893850120405", Name = "Hạt nêm Knorr thịt thăn xương ống gói 900g", BaseUnit = "Gói",
                        CostPrice = 63000m, SellingPrice = 79000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 11, CreatedHoursAgo = 8,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1506368249639-73a05d6f6488?w=500&auto=format&fit=crop&q=60" },
                        Description = "Hạt nêm Knorr chiết xuất thịt thăn xương ống và tủy thơm ngọt tự nhiên.",
                        InternalNote = "Kệ gia vị nêm nếm."
                    },
                    new()
                    {
                        Code = "SP-SPC-006", Barcode = "893850120406", Name = "Đường tinh luyện Biên Hòa gói 1kg", BaseUnit = "Gói",
                        CostPrice = 24000m, SellingPrice = 30500m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 12, CreatedHoursAgo = 4,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1506368249639-73a05d6f6488?w=500&auto=format&fit=crop&q=60" },
                        Description = "Đường cát trắng tinh luyện Biên Hòa Pure 1kg ngọt dịu tinh khiết.",
                        InternalNote = "Kệ đường muối."
                    },
                    new()
                    {
                        Code = "SP-SPC-007", Barcode = "893850120407", Name = "Tương ớt Chinsu đượm vị cay chai 250g", BaseUnit = "Chai",
                        CostPrice = 11000m, SellingPrice = 15000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 12, CreatedHoursAgo = 9,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1589301760014-d929f3979dbc?w=500&auto=format&fit=crop&q=60" },
                        Description = "Tương ớt Chinsu vị cay nồng đặc trưng, chấm vạn món ngon.",
                        InternalNote = "Kệ sốt chấm tương ớt."
                    }
                }
            },

            // 5. Bánh Kẹo & Đồ Ăn Vặt
            new SeedCategoryItem
            {
                Code = "CONF",
                Name = "Bánh Kẹo & Đồ Ăn Vặt",
                Description = "Snack, bánh quy, sô cô la và kẹo dẻo các loại",
                Products = new List<SeedProductItem>
                {
                    new()
                    {
                        Code = "SP-CNF-001", Barcode = "893850120501", Name = "Bánh ChocoPie Orion truyền thống hộp 12 cái", BaseUnit = "Hộp",
                        CostPrice = 46000m, SellingPrice = 58000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT", IsFavorite = true,
                        CreatedDaysAgo = 13, CreatedHoursAgo = 2,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1549007994-cb92caebd54b?w=500&auto=format&fit=crop&q=60" },
                        Description = "Bánh Chocopie phủ socola và nhân marshmallow dẻo ngọt thơm ngon.",
                        InternalNote = "Kệ bánh ngọt dãy C.",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 8m, Price = 450000m, Barcode = "8938501205018" }
                        }
                    },
                    new()
                    {
                        Code = "SP-CNF-002", Barcode = "893850120502", Name = "Bánh quy bơ Danisa hộp thiếc 454g", BaseUnit = "Hộp",
                        CostPrice = 115000m, SellingPrice = 145000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 13, CreatedHoursAgo = 6,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1558961363-fa8fdf82db35?w=500&auto=format&fit=crop&q=60" },
                        Description = "Bánh quy bơ Danisa hoàng gia Đan Mạch hộp thiếc sang trọng làm quà biếu.",
                        InternalNote = "Kệ bánh hộp quà tặng."
                    },
                    new()
                    {
                        Code = "SP-CNF-003", Barcode = "893850120503", Name = "Kẹo mềm Sugus hương trái cây gói 120g", BaseUnit = "Gói",
                        CostPrice = 14000m, SellingPrice = 19000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 14, CreatedHoursAgo = 4,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1558961363-fa8fdf82db35?w=500&auto=format&fit=crop&q=60" },
                        Description = "Kẹo nhai mềm Sugus đủ vị dâu tây, cam, táo, nho thơm ngon.",
                        InternalNote = "Kệ kẹo ngọt."
                    },
                    new()
                    {
                        Code = "SP-CNF-004", Barcode = "893850120504", Name = "Snack khoai tây Lay's vị tự nhiên gói 95g", BaseUnit = "Gói",
                        CostPrice = 16000m, SellingPrice = 22000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 14, CreatedHoursAgo = 9,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1566478989037-eec170784d0b?w=500&auto=format&fit=crop&q=60" },
                        Description = "Snack khoai tây chiên giòn rụm lát mỏng Lay's Classic khoái khẩu.",
                        InternalNote = "Kệ bim bim ăn vặt dãy C."
                    },
                    new()
                    {
                        Code = "SP-CNF-005", Barcode = "893850120505", Name = "Rong biển sấy giòn Taokaenoi gói 32g", BaseUnit = "Gói",
                        CostPrice = 21000m, SellingPrice = 28000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 15, CreatedHoursAgo = 3,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1566478989037-eec170784d0b?w=500&auto=format&fit=crop&q=60" },
                        Description = "Rong biển Taokaenoi Thái Lan nướng giòn vị cay đậm đà hấp dẫn.",
                        InternalNote = "Kệ ăn vặt nhập khẩu."
                    },
                    new()
                    {
                        Code = "SP-CNF-006", Barcode = "893850120506", Name = "Bánh gạo One One vị ngọt dịu gói 150g", BaseUnit = "Gói",
                        CostPrice = 22000m, SellingPrice = 29000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 15, CreatedHoursAgo = 8,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1549007994-cb92caebd54b?w=500&auto=format&fit=crop&q=60" },
                        Description = "Bánh gạo nướng giòn xốp One One vị ngọt thanh tự nhiên an toàn cho sức khỏe.",
                        InternalNote = "Kệ bánh gạo."
                    }
                }
            },

            // 6. Thực Phẩm Đông Lạnh & Đóng Hộp
            new SeedCategoryItem
            {
                Code = "FROZ",
                Name = "Thực Phẩm Đông Lạnh & Đóng Hộp",
                Description = "Xúc xích, cá hộp, nem rán, sủi cảo và đồ hộp tiện lợi",
                Products = new List<SeedProductItem>
                {
                    new()
                    {
                        Code = "SP-FRZ-001", Barcode = "893850120601", Name = "Cá nục sốt cà Three Lady Cooks hộp 155g", BaseUnit = "Hộp",
                        CostPrice = 15000m, SellingPrice = 20000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 16, CreatedHoursAgo = 3,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1589301760014-d929f3979dbc?w=500&auto=format&fit=crop&q=60" },
                        Description = "Cá nục sốt cà chua Ba Cô Gái thơm ngon đậm vị truyền thống Thái Lan.",
                        InternalNote = "Kệ đồ hộp tiện lợi.",
                        Units = new()
                        {
                            new() { UnitName = "Lốc", ConversionRate = 10m, Price = 195000m, Barcode = "8938501206010" }
                        }
                    },
                    new()
                    {
                        Code = "SP-FRZ-002", Barcode = "893850120602", Name = "Xúc xích tiệt trùng Vissan heo gói 5 cây", BaseUnit = "Gói",
                        CostPrice = 18000m, SellingPrice = 24000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 16, CreatedHoursAgo = 7,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1603048588665-791ca8aea617?w=500&auto=format&fit=crop&q=60" },
                        Description = "Xúc xích tiệt trùng Vissan dinh dưỡng, ăn liền tiện lợi mọi lúc mọi nơi.",
                        InternalNote = "Kệ xúc xích đồ khô.",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 20m, Price = 460000m, Barcode = "8938501206020" }
                        }
                    },
                    new()
                    {
                        Code = "SP-FRZ-003", Barcode = "893850120603", Name = "Sủi cảo tôm thịt Cầu Tre gói 500g", BaseUnit = "Gói",
                        CostPrice = 65000m, SellingPrice = 84000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 17, CreatedHoursAgo = 4,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1551024709-8f23befc6f87?w=500&auto=format&fit=crop&q=60" },
                        Description = "Sủi cảo nhân tôm thịt tươi giòn sần sật, hấp hoặc chiên thơm nức.",
                        InternalNote = "Tủ đông kem - sủi cảo -18 độ C."
                    },
                    new()
                    {
                        Code = "SP-FRZ-004", Barcode = "893850120604", Name = "Nem rán truyền thống CP gói 500g", BaseUnit = "Gói",
                        CostPrice = 48000m, SellingPrice = 63000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 17, CreatedHoursAgo = 9,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1603048588665-791ca8aea617?w=500&auto=format&fit=crop&q=60" },
                        Description = "Chả giò rế nem rán truyền thống vỏ giòn rụm chuẩn vị cơm nhà.",
                        InternalNote = "Tủ đông -18 độ C."
                    },
                    new()
                    {
                        Code = "SP-FRZ-005", Barcode = "893850120605", Name = "Há cảo mini tôm thịt CP khay 300g", BaseUnit = "Khay",
                        CostPrice = 39000m, SellingPrice = 51000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 18, CreatedHoursAgo = 2,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1551024709-8f23befc6f87?w=500&auto=format&fit=crop&q=60" },
                        Description = "Há cảo tôm thịt nhân đầy đặn, vỏ mỏng dẻo thơm, ăn sáng tuyệt ngon.",
                        InternalNote = "Tủ đông thực phẩm."
                    },
                    new()
                    {
                        Code = "SP-FRZ-006", Barcode = "893850120606", Name = "Bò viên gân đặc biệt Vissan gói 200g", BaseUnit = "Gói",
                        CostPrice = 36000m, SellingPrice = 48000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 18, CreatedHoursAgo = 8,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1603048588665-791ca8aea617?w=500&auto=format&fit=crop&q=60" },
                        Description = "Bò viên gân giòn sần sật, đậm vị thịt bò, thích hợp nấu phở và ăn lẩu.",
                        InternalNote = "Tủ đông thực phẩm mát."
                    }
                }
            },

            // 7. Hóa Mỹ Phẩm & Chăm Sóc Cá Nhân
            new SeedCategoryItem
            {
                Code = "COSM",
                Name = "Hóa Mỹ Phẩm & Chăm Sóc Cá Nhân",
                Description = "Dầu gội, sữa tắm, kem đánh răng và sản phẩm vệ sinh cá nhân",
                Products = new List<SeedProductItem>
                {
                    new()
                    {
                        Code = "SP-CSM-001", Barcode = "893850120701", Name = "Dầu gội Clear Men mát lạnh sạch gàu chai 630g", BaseUnit = "Chai",
                        CostPrice = 145000m, SellingPrice = 182000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT", IsFavorite = true,
                        CreatedDaysAgo = 19, CreatedHoursAgo = 3,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1585421514738-01798e348b17?w=500&auto=format&fit=crop&q=60" },
                        Description = "Dầu gội trị gàu Clear Men với tinh chất bạc hà sảng khoái suốt ngày dài.",
                        InternalNote = "Kệ hóa mỹ phẩm nam.",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 12m, Price = 2100000m, Barcode = "8938501207012" }
                        }
                    },
                    new()
                    {
                        Code = "SP-CSM-002", Barcode = "893850120702", Name = "Sữa tắm Lifebuoy bảo vệ vượt trội chai 800g", BaseUnit = "Chai",
                        CostPrice = 135000m, SellingPrice = 169000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 19, CreatedHoursAgo = 7,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1585421514738-01798e348b17?w=500&auto=format&fit=crop&q=60" },
                        Description = "Sữa tắm Lifebuoy sạch khuẩn vượt trội với công thức ion bạc bảo vệ sức khỏe.",
                        InternalNote = "Kệ sữa tắm gia đình."
                    },
                    new()
                    {
                        Code = "SP-CSM-003", Barcode = "893850120703", Name = "Kem đánh răng Colgate Total bảo vệ 12h tuýp 200g", BaseUnit = "Tuýp",
                        CostPrice = 32000m, SellingPrice = 42000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 20, CreatedHoursAgo = 2,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1585421514738-01798e348b17?w=500&auto=format&fit=crop&q=60" },
                        Description = "Kem đánh răng Colgate Total ngừa sâu răng và bảo vệ khoang miệng suốt 12 giờ.",
                        InternalNote = "Kệ chăm sóc răng miệng."
                    },
                    new()
                    {
                        Code = "SP-CSM-004", Barcode = "893850120704", Name = "Bàn chải đánh răng P/S than tre kháng khuẩn", BaseUnit = "Cây",
                        CostPrice = 18000m, SellingPrice = 26000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 20, CreatedHoursAgo = 6,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1585421514738-01798e348b17?w=500&auto=format&fit=crop&q=60" },
                        Description = "Lông tơ than tre siêu mềm mảnh kháng khuẩn 99.9% của thương hiệu P/S.",
                        InternalNote = "Kệ bàn chải đánh răng."
                    },
                    new()
                    {
                        Code = "SP-CSM-005", Barcode = "893850120705", Name = "Sữa rửa mặt Nivea Men sáng da sạch nhờn tuýp 100g", BaseUnit = "Tuýp",
                        CostPrice = 68000m, SellingPrice = 89000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 21, CreatedHoursAgo = 3,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1585421514738-01798e348b17?w=500&auto=format&fit=crop&q=60" },
                        Description = "Sữa rửa mặt bùn khoáng Nivea Men hút sạch dầu nhờn và ngừa mụn hiệu quả.",
                        InternalNote = "Kệ mỹ phẩm nam."
                    },
                    new()
                    {
                        Code = "SP-CSM-006", Barcode = "893850120706", Name = "Nước súc miệng Listerine Cool Mint chai 500ml", BaseUnit = "Chai",
                        CostPrice = 92000m, SellingPrice = 119000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 21, CreatedHoursAgo = 8,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1585421514738-01798e348b17?w=500&auto=format&fit=crop&q=60" },
                        Description = "Nước súc miệng diệt khuẩn Listerine vị bạc hà the mát hơi thở tự tin.",
                        InternalNote = "Kệ chăm sóc răng miệng."
                    }
                }
            },

            // 8. Tẩy Rửa & Vệ Sinh Nhà Cửa
            new SeedCategoryItem
            {
                Code = "CLEAN",
                Name = "Tẩy Rửa & Vệ Sinh Nhà Cửa",
                Description = "Nước giặt, nước xả, nước rửa chén và chất tẩy rửa gia dụng",
                Products = new List<SeedProductItem>
                {
                    new()
                    {
                        Code = "SP-CLN-001", Barcode = "893850120801", Name = "Nước giặt OMO Matic cửa trên túi 3.6kg", BaseUnit = "Túi",
                        CostPrice = 175000m, SellingPrice = 219000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT", IsFavorite = true,
                        CreatedDaysAgo = 22, CreatedHoursAgo = 4,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1584308666744-24d5c474f2ae?w=500&auto=format&fit=crop&q=60" },
                        Description = "Nước giặt OMO Matic xoáy bay vết bẩn cứng đầu chỉ trong 1 lần giặt.",
                        InternalNote = "Kệ chất tẩy rửa dãy D.",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 4m, Price = 850000m, Barcode = "8938501208014" }
                        }
                    },
                    new()
                    {
                        Code = "SP-CLN-002", Barcode = "893850120802", Name = "Nước xả vải Comfort một lần xả hương ban mai túi 3.2L", BaseUnit = "Túi",
                        CostPrice = 165000m, SellingPrice = 209000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 22, CreatedHoursAgo = 9,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1584308666744-24d5c474f2ae?w=500&auto=format&fit=crop&q=60" },
                        Description = "Nước xả làm mềm vải Comfort lưu giữ hương hoa sớm mai thơm ngát suốt ngày.",
                        InternalNote = "Kệ nước xả vải.",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 4m, Price = 810000m, Barcode = "8938501208024" }
                        }
                    },
                    new()
                    {
                        Code = "SP-CLN-003", Barcode = "893850120803", Name = "Nước rửa chén Sunlight chanh tươi túi 2.1kg", BaseUnit = "Túi",
                        CostPrice = 52000m, SellingPrice = 67000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 23, CreatedHoursAgo = 2,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1584308666744-24d5c474f2ae?w=500&auto=format&fit=crop&q=60" },
                        Description = "Nước rửa chén Sunlight tinh chất chanh tươi đánh bay dầu mỡ nhanh chóng.",
                        InternalNote = "Kệ nước rửa chén.",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 6m, Price = 390000m, Barcode = "8938501208036" }
                        }
                    },
                    new()
                    {
                        Code = "SP-CLN-004", Barcode = "893850120804", Name = "Nước lau sàn Sunlight hương hoa hạ chai 1kg", BaseUnit = "Chai",
                        CostPrice = 28000m, SellingPrice = 36000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 23, CreatedHoursAgo = 7,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1584308666744-24d5c474f2ae?w=500&auto=format&fit=crop&q=60" },
                        Description = "Nước lau sàn sạch bóng kin kít và xua đuổi côn trùng hiệu quả.",
                        InternalNote = "Kệ nước lau sàn."
                    },
                    new()
                    {
                        Code = "SP-CLN-005", Barcode = "893850120805", Name = "Nước tẩy bồn cầu Vim diệt khuẩn đậm đặc chai 900ml", BaseUnit = "Chai",
                        CostPrice = 33000m, SellingPrice = 43000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 24, CreatedHoursAgo = 3,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1584308666744-24d5c474f2ae?w=500&auto=format&fit=crop&q=60" },
                        Description = "Nước tẩy bồn cầu Vim diệt sạch 99.9% vi khuẩn gây hại.",
                        InternalNote = "Kệ hóa chất tẩy rửa."
                    },
                    new()
                    {
                        Code = "SP-CLN-006", Barcode = "893850120806", Name = "Khăn giấy lụa rút Pulppy 2 lớp lốc 3 gói", BaseUnit = "Lốc",
                        CostPrice = 42000m, SellingPrice = 55000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 24, CreatedHoursAgo = 8,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1584308666744-24d5c474f2ae?w=500&auto=format&fit=crop&q=60" },
                        Description = "Khăn giấy rút Pulppy mềm mại như lụa, thấm hút nước cực tốt.",
                        InternalNote = "Kệ giấy vệ sinh khăn giấy."
                    }
                }
            },

            // 9. Mì Ăn Liền & Thực Phẩm Tiện Lợi
            new SeedCategoryItem
            {
                Code = "INST",
                Name = "Mì Ăn Liền & Thực Phẩm Tiện Lợi",
                Description = "Mì gói, phở gói, cháo ăn liền và miến tiện dụng",
                Products = new List<SeedProductItem>
                {
                    new()
                    {
                        Code = "SP-INS-001", Barcode = "893850120901", Name = "Mì Hảo Hảo tôm chua cay gói 75g", BaseUnit = "Gói",
                        CostPrice = 3800m, SellingPrice = 4800m, VatRate = 8m, TaxCategory = "STANDARD_1PCT", IsFavorite = true,
                        CreatedDaysAgo = 25, CreatedHoursAgo = 2,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1612927601601-6638404737ce?w=500&auto=format&fit=crop&q=60" },
                        Description = "Mì ăn liền Hảo Hảo hương vị tôm chua cay quốc dân số 1 Việt Nam.",
                        InternalNote = "Kệ mì gói dãy E. Mặt hàng bán chạy nhất.",
                        Units = new()
                        {
                            new() { UnitName = "Lốc", ConversionRate = 5m, Price = 23500m, Barcode = "8938501209015" },
                            new() { UnitName = "Thùng", ConversionRate = 30m, Price = 135000m, Barcode = "2893850120901" }
                        }
                    },
                    new()
                    {
                        Code = "SP-INS-002", Barcode = "893850120902", Name = "Mì xào tương đen Samyang Hàn Quốc gói 140g", BaseUnit = "Gói",
                        CostPrice = 23000m, SellingPrice = 31000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 25, CreatedHoursAgo = 6,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1612927601601-6638404737ce?w=500&auto=format&fit=crop&q=60" },
                        Description = "Mì trộn sốt tương đen chuẩn vị Hàn Quốc sợi dai thơm ngậy.",
                        InternalNote = "Kệ mì nhập khẩu Hàn Quốc.",
                        Units = new()
                        {
                            new() { UnitName = "Lốc", ConversionRate = 5m, Price = 150000m, Barcode = "8938501209025" }
                        }
                    },
                    new()
                    {
                        Code = "SP-INS-003", Barcode = "893850120903", Name = "Phở bò Đệ Nhất gói 68g", BaseUnit = "Gói",
                        CostPrice = 6800m, SellingPrice = 8500m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 26, CreatedHoursAgo = 3,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1569718212165-3a8278d5f624?w=500&auto=format&fit=crop&q=60" },
                        Description = "Phở ăn liền Đệ Nhất nước cốt thịt bò hầm thật thơm ngọt vị truyền thống.",
                        InternalNote = "Kệ phở miến ăn liền.",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 30m, Price = 245000m, Barcode = "8938501209030" }
                        }
                    },
                    new()
                    {
                        Code = "SP-INS-004", Barcode = "893850120904", Name = "Cháo yến sào gà nấm YenViet gói 50g", BaseUnit = "Gói",
                        CostPrice = 9000m, SellingPrice = 12000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 26, CreatedHoursAgo = 8,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1569718212165-3a8278d5f624?w=500&auto=format&fit=crop&q=60" },
                        Description = "Cháo ăn liền dinh dưỡng bổ sung tổ yến sào tự nhiên và thịt gà nấm thơm ngon.",
                        InternalNote = "Kệ cháo dinh dưỡng."
                    },
                    new()
                    {
                        Code = "SP-INS-005", Barcode = "893850120905", Name = "Miến Phú Hương vị sườn heo gói 58g", BaseUnit = "Gói",
                        CostPrice = 9500m, SellingPrice = 12500m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 27, CreatedHoursAgo = 4,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1569718212165-3a8278d5f624?w=500&auto=format&fit=crop&q=60" },
                        Description = "Miến đậu xanh Phú Hương thanh mát sợi dai ngon nước súp sườn heo hầm đậm đà.",
                        InternalNote = "Kệ miến khô."
                    },
                    new()
                    {
                        Code = "SP-INS-006", Barcode = "893850120906", Name = "Hủ tiếu Nam Vang Cung Đình tô 80g", BaseUnit = "Tô",
                        CostPrice = 13000m, SellingPrice = 17500m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 27, CreatedHoursAgo = 9,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1569718212165-3a8278d5f624?w=500&auto=format&fit=crop&q=60" },
                        Description = "Hủ tiếu Nam Vang đóng tô tiện lợi có gói thịt bằm và hành phi thơm lừng.",
                        InternalNote = "Kệ mì tô ăn liền."
                    }
                }
            },

            // 10. Trà, Cà Phê & Đồ Uống Pha Chế
            new SeedCategoryItem
            {
                Code = "TEA",
                Name = "Trà, Cà Phê & Đồ Uống Pha Chế",
                Description = "Cà phê hòa tan, trà túi lọc, bột cacao và trà thảo mộc",
                Products = new List<SeedProductItem>
                {
                    new()
                    {
                        Code = "SP-TEA-001", Barcode = "893850121001", Name = "Cà phê hòa tan Nescafe 3in1 Đậm Đà hộp 20 gói", BaseUnit = "Hộp",
                        CostPrice = 51000m, SellingPrice = 64000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT", IsFavorite = true,
                        CreatedDaysAgo = 28, CreatedHoursAgo = 2,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1514432324607-a09d9b4aefdd?w=500&auto=format&fit=crop&q=60" },
                        Description = "Cà phê hòa tan Nescafe vị đậm đà hương thơm khó cưỡng của hạt cà phê Robusta.",
                        InternalNote = "Kệ cà phê hòa tan.",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 24m, Price = 1480000m, Barcode = "8938501210014" }
                        }
                    },
                    new()
                    {
                        Code = "SP-TEA-002", Barcode = "893850121002", Name = "Cà phê sữa G7 Trung Nguyên hộp 18 gói", BaseUnit = "Hộp",
                        CostPrice = 47000m, SellingPrice = 59000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 28, CreatedHoursAgo = 7,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1514432324607-a09d9b4aefdd?w=500&auto=format&fit=crop&q=60" },
                        Description = "Cà phê hòa tan G7 Trung Nguyên nức tiếng Buôn Ma Thuột thơm nồng say đắm.",
                        InternalNote = "Kệ cà phê sữa.",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 24m, Price = 1380000m, Barcode = "8938501210024" }
                        }
                    },
                    new()
                    {
                        Code = "SP-TEA-003", Barcode = "893850121003", Name = "Trà túi lọc Lipton nhãn vàng hộp 100 gói", BaseUnit = "Hộp",
                        CostPrice = 72000m, SellingPrice = 91000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 29, CreatedHoursAgo = 3,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1576092768241-dec231879fc3?w=500&auto=format&fit=crop&q=60" },
                        Description = "Trà đen túi lọc Lipton Yellow Label danh tiếng thế giới chiết xuất từ búp trà tươi.",
                        InternalNote = "Kệ trà túi lọc."
                    },
                    new()
                    {
                        Code = "SP-TEA-004", Barcode = "893850121004", Name = "Trà lài Thái Nguyên đặc sản gói 200g", BaseUnit = "Gói",
                        CostPrice = 45000m, SellingPrice = 60000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 29, CreatedHoursAgo = 8,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1576092768241-dec231879fc3?w=500&auto=format&fit=crop&q=60" },
                        Description = "Trà búp tân cương Thái Nguyên ướp hoa nhài tự nhiên thơm ngát dịu dàng.",
                        InternalNote = "Kệ trà khô đặc sản."
                    },
                    new()
                    {
                        Code = "SP-TEA-005", Barcode = "893850121005", Name = "Trà đào sả Cozy hộp 16 gói", BaseUnit = "Hộp",
                        CostPrice = 28000m, SellingPrice = 37000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 30, CreatedHoursAgo = 2,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1576092768241-dec231879fc3?w=500&auto=format&fit=crop&q=60" },
                        Description = "Trà hòa tan Cozy vị đào thơm mát thoảng hương sả giải nhiệt mùa hè.",
                        InternalNote = "Kệ trà hòa tan."
                    },
                    new()
                    {
                        Code = "SP-TEA-006", Barcode = "893850121006", Name = "Bột cacao nguyên chất Đắk Lắk hộp 250g", BaseUnit = "Hộp",
                        CostPrice = 65000m, SellingPrice = 85000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 30, CreatedHoursAgo = 7,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1514432324607-a09d9b4aefdd?w=500&auto=format&fit=crop&q=60" },
                        Description = "Bột ca cao nguyên chất không đường Đắk Lắk béo ngậy dùng pha chế đồ uống và làm bánh.",
                        InternalNote = "Kệ bột pha chế."
                    }
                }
            },

            // 11. Sản Phẩm Mẹ & Bé
            new SeedCategoryItem
            {
                Code = "BABY",
                Name = "Sản Phẩm Mẹ & Bé",
                Description = "Tã bỉm, sữa bột trẻ em, khăn ướt và bột ăn dặm",
                Products = new List<SeedProductItem>
                {
                    new()
                    {
                        Code = "SP-BBY-001", Barcode = "893850121101", Name = "Tã dán Bobby sơ sinh XS gói 70 miếng", BaseUnit = "Gói",
                        CostPrice = 135000m, SellingPrice = 168000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 10, CreatedHoursAgo = 1,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1515488042361-ee00e0ddd4e4?w=500&auto=format&fit=crop&q=60" },
                        Description = "Tã dán Bobby sơ sinh với lõi nén thần kỳ mỏng nhẹ thấm hút chống tràn tuyệt đối.",
                        InternalNote = "Kệ tã bỉm trẻ em.",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 4m, Price = 650000m, Barcode = "8938501211014" }
                        }
                    },
                    new()
                    {
                        Code = "SP-BBY-002", Barcode = "893850121102", Name = "Tã quần Huggies Dry Pants size M 60 miếng", BaseUnit = "Gói",
                        CostPrice = 215000m, SellingPrice = 265000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT", IsFavorite = true,
                        CreatedDaysAgo = 11, CreatedHoursAgo = 5,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1515488042361-ee00e0ddd4e4?w=500&auto=format&fit=crop&q=60" },
                        Description = "Tã quần Huggies đệm mây co giãn 4 chiều mềm mại nâng niu làn da bé.",
                        InternalNote = "Kệ tã bỉm trẻ em.",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 3m, Price = 780000m, Barcode = "8938501211023" }
                        }
                    },
                    new()
                    {
                        Code = "SP-BBY-003", Barcode = "893850121103", Name = "Khăn ướt em bé Fressi Care không cồn gói 80 miếng", BaseUnit = "Gói",
                        CostPrice = 24000m, SellingPrice = 33000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 12, CreatedHoursAgo = 1,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1515488042361-ee00e0ddd4e4?w=500&auto=format&fit=crop&q=60" },
                        Description = "Khăn ướt Fressi Care không chứa cồn, không paraben, an toàn tuyệt đối cho da nhạy cảm.",
                        InternalNote = "Kệ khăn ướt mẹ và bé.",
                        Units = new()
                        {
                            new() { UnitName = "Lốc", ConversionRate = 3m, Price = 92000m, Barcode = "8938501211033" }
                        }
                    },
                    new()
                    {
                        Code = "SP-BBY-004", Barcode = "893850121104", Name = "Sữa bột Meiji số 0 nội địa Nhật lon 800g", BaseUnit = "Lon",
                        CostPrice = 460000m, SellingPrice = 535000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 13, CreatedHoursAgo = 5,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1556881286-fc6915169721?w=500&auto=format&fit=crop&q=60" },
                        Description = "Sữa bột Meiji nội địa Nhật Bản cho trẻ 0-1 tuổi bổ sung DHA và kháng thể tự nhiên.",
                        InternalNote = "Kệ sữa công thức trẻ em."
                    },
                    new()
                    {
                        Code = "SP-BBY-005", Barcode = "893850121105", Name = "Bột ăn dặm Nestle Cerelac gạo lức sữa hộp 200g", BaseUnit = "Hộp",
                        CostPrice = 58000m, SellingPrice = 74000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 14, CreatedHoursAgo = 2,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1517673132405-a56a62b18caf?w=500&auto=format&fit=crop&q=60" },
                        Description = "Bột ăn dặm ngũ cốc dinh dưỡng bổ sung vi chất sắt và kẽm cho bé tập ăn.",
                        InternalNote = "Kệ đồ ăn dặm."
                    },
                    new()
                    {
                        Code = "SP-BBY-006", Barcode = "893850121106", Name = "Sữa tắm gội toàn thân Johnson's Baby chai 500ml", BaseUnit = "Chai",
                        CostPrice = 110000m, SellingPrice = 139000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 15, CreatedHoursAgo = 1,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1585421514738-01798e348b17?w=500&auto=format&fit=crop&q=60" },
                        Description = "Tắm gội 2 trong 1 công thức không cay mắt Johnson's Baby chai vòi tiện dụng.",
                        InternalNote = "Kệ tắm gội trẻ em."
                    }
                }
            },

            // 12. Đồ Làm Bánh & Ngũ Cốc Dinh Dưỡng
            new SeedCategoryItem
            {
                Code = "BAKE",
                Name = "Đồ Làm Bánh & Ngũ Cốc Dinh Dưỡng",
                Description = "Bột mì, bột chiên, ngũ cốc dinh dưỡng và men nở",
                Products = new List<SeedProductItem>
                {
                    new()
                    {
                        Code = "SP-BAK-001", Barcode = "893850121201", Name = "Bột mì đa dụng Hoa Ngọc Lan gói 1kg", BaseUnit = "Gói",
                        CostPrice = 19000m, SellingPrice = 25000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 16, CreatedHoursAgo = 2,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1509440159596-0249088772ff?w=500&auto=format&fit=crop&q=60" },
                        Description = "Bột mì cao cấp Hoa Ngọc Lan trắng mịn, chuyên làm bánh mì, bánh bông lan.",
                        InternalNote = "Kệ nguyên liệu làm bánh.",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 10m, Price = 240000m, Barcode = "8938501212010" }
                        }
                    },
                    new()
                    {
                        Code = "SP-BAK-002", Barcode = "893850121202", Name = "Bột chiên giòn Tài Ký gói 150g", BaseUnit = "Gói",
                        CostPrice = 8000m, SellingPrice = 11500m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 17, CreatedHoursAgo = 1,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1509440159596-0249088772ff?w=500&auto=format&fit=crop&q=60" },
                        Description = "Bột tẩm chiên giòn Tài Ký chiên tôm, mực, rau củ giòn lâu và ráo dầu.",
                        InternalNote = "Kệ bột chiên."
                    },
                    new()
                    {
                        Code = "SP-BAK-003", Barcode = "893850121203", Name = "Ngũ cốc sấy giòn Calbee Nhật Bản túi 750g", BaseUnit = "Túi",
                        CostPrice = 165000m, SellingPrice = 215000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT", IsFavorite = true,
                        CreatedDaysAgo = 18, CreatedHoursAgo = 4,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1517673132405-a56a62b18caf?w=500&auto=format&fit=crop&q=60" },
                        Description = "Ngũ cốc hoa quả sấy giòn Calbee đỏ nội địa Nhật ăn kèm sữa tươi tuyệt đỉnh.",
                        InternalNote = "Kệ ngũ cốc nhập khẩu."
                    },
                    new()
                    {
                        Code = "SP-BAK-004", Barcode = "893850121204", Name = "Yến mạch nguyên chất Quaker Oats hộp 500g", BaseUnit = "Hộp",
                        CostPrice = 55000m, SellingPrice = 73000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 19, CreatedHoursAgo = 2,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1517673132405-a56a62b18caf?w=500&auto=format&fit=crop&q=60" },
                        Description = "Yến mạch cán dẹt nguyên chất Quaker Mỹ giàu chất xơ hòa tan tốt cho tiêu hóa.",
                        InternalNote = "Kệ đồ ăn kiêng."
                    },
                    new()
                    {
                        Code = "SP-BAK-005", Barcode = "893850121205", Name = "Men nở khô Mauripan hộp 5 gói", BaseUnit = "Hộp",
                        CostPrice = 16000m, SellingPrice = 22000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 20, CreatedHoursAgo = 1,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1509440159596-0249088772ff?w=500&auto=format&fit=crop&q=60" },
                        Description = "Men bánh mì Mauripan kích nở nhanh hoạt tính cao chuyên làm bánh bao bánh mì.",
                        InternalNote = "Kệ phụ gia làm bánh."
                    },
                    new()
                    {
                        Code = "SP-BAK-006", Barcode = "893850121206", Name = "Hạt chia Organic Úc giàu Omega-3 gói 500g", BaseUnit = "Gói",
                        CostPrice = 120000m, SellingPrice = 155000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 21, CreatedHoursAgo = 3,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1517673132405-a56a62b18caf?w=500&auto=format&fit=crop&q=60" },
                        Description = "Hạt chia đen hữu cơ Organic chia seed Úc tốt cho tim mạch và vóc dáng.",
                        InternalNote = "Kệ hạt dinh dưỡng."
                    }
                }
            },

            // 13. Trứng & Đậu Phụ Tươi
            new SeedCategoryItem
            {
                Code = "EGG",
                Name = "Trứng & Đậu Phụ Tươi",
                Description = "Trứng gà tươi, trứng vịt, trứng cút và đậu phụ sạch",
                Products = new List<SeedProductItem>
                {
                    new()
                    {
                        Code = "SP-EGG-001", Barcode = "893850121301", Name = "Trứng gà sạch Ba Huân vỉ 10 quả", BaseUnit = "Vỉ",
                        CostPrice = 26000m, SellingPrice = 33000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT", HasBatches = true, IsFavorite = true,
                        CreatedDaysAgo = 1, CreatedHoursAgo = 1,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1506976785307-8732e854ad03?w=500&auto=format&fit=crop&q=60" },
                        Description = "Trứng gà tươi tiệt trùng xử lý nhiệt Ba Huân vỉ 10 quả đạt chuẩn VietGAP.",
                        InternalNote = "Kệ trứng tươi hàng ngày. Bán theo vỉ hoặc thùng.",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 10m, Price = 315000m, Barcode = "8938501213010" }
                        }
                    },
                    new()
                    {
                        Code = "SP-EGG-002", Barcode = "893850121302", Name = "Trứng vịt tươi Ba Huân vỉ 10 quả", BaseUnit = "Vỉ",
                        CostPrice = 32000m, SellingPrice = 39000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 2, CreatedHoursAgo = 2,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1582722872445-44dc5f7e3c8f?w=500&auto=format&fit=crop&q=60" },
                        Description = "Trứng vịt vỏ dày lòng đỏ đậm đà chuyên kho thịt và luộc lòng đào.",
                        InternalNote = "Kệ trứng tươi.",
                        Units = new()
                        {
                            new() { UnitName = "Thùng", ConversionRate = 10m, Price = 370000m, Barcode = "8938501213020" }
                        }
                    },
                    new()
                    {
                        Code = "SP-EGG-003", Barcode = "893850121303", Name = "Trứng cút tươi sạch vỉ 30 quả", BaseUnit = "Vỉ",
                        CostPrice = 21000m, SellingPrice = 28000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 3, CreatedHoursAgo = 3,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1506976785307-8732e854ad03?w=500&auto=format&fit=crop&q=60" },
                        Description = "Trứng chim cút tươi mới vỉ 30 quả bổ dưỡng cho trẻ nhỏ.",
                        InternalNote = "Kệ trứng tươi."
                    },
                    new()
                    {
                        Code = "SP-EGG-004", Barcode = "893850121304", Name = "Đậu phụ trắng Meko miếng tươi 300g", BaseUnit = "Miếng",
                        CostPrice = 8000m, SellingPrice = 11000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 4, CreatedHoursAgo = 1,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1546069901-ba9599a7e63c?w=500&auto=format&fit=crop&q=60" },
                        Description = "Đậu phụ trắng Meko tươi nguyên chất không thạch cao béo ngậy mềm mịn.",
                        InternalNote = "Tủ mát đậu phụ 4 độ C. Nhập tươi mỗi sáng."
                    },
                    new()
                    {
                        Code = "SP-EGG-005", Barcode = "893850121305", Name = "Đậu hũ non trứng Ichiban lốc 2 cây", BaseUnit = "Lốc",
                        CostPrice = 14000m, SellingPrice = 19000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 5, CreatedHoursAgo = 2,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1546069901-ba9599a7e63c?w=500&auto=format&fit=crop&q=60" },
                        Description = "Đậu hũ trứng cây Ichiban mềm tan chuyên nấu canh rong biển và chiên giòn.",
                        InternalNote = "Tủ mát bảo quản 4-8 độ C."
                    },
                    new()
                    {
                        Code = "SP-EGG-006", Barcode = "893850121306", Name = "Trứng gà so thảo dược Dabaco vỉ 10 quả", BaseUnit = "Vỉ",
                        CostPrice = 38000m, SellingPrice = 48000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 6, CreatedHoursAgo = 3,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1506976785307-8732e854ad03?w=500&auto=format&fit=crop&q=60" },
                        Description = "Trứng gà so nuôi bằng thảo dược tự nhiên Dabaco thơm ngậy không tanh.",
                        InternalNote = "Kệ trứng cao cấp."
                    }
                }
            },

            // 14. Trái Cây Nhập Khẩu & Nội Địa
            new SeedCategoryItem
            {
                Code = "FRUIT",
                Name = "Trái Cây Nhập Khẩu & Nội Địa",
                Description = "Táo Envy, cam sành, nho đen và trái cây tươi theo mùa",
                Products = new List<SeedProductItem>
                {
                    new()
                    {
                        Code = "SP-FRU-001", Barcode = "893850121401", Name = "Táo Envy New Zealand khay 4 quả", BaseUnit = "Khay",
                        CostPrice = 85000m, SellingPrice = 115000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT", HasBatches = true, IsFavorite = true,
                        CreatedDaysAgo = 1, CreatedHoursAgo = 5,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1560806887-1e4cd0b6cbd6?w=500&auto=format&fit=crop&q=60" },
                        Description = "Táo Envy New Zealand giòn ngọt mọng nước, mùi thơm đặc trưng cao cấp.",
                        InternalNote = "Tủ mát trái cây tươi 4-6 độ C."
                    },
                    new()
                    {
                        Code = "SP-FRU-002", Barcode = "893850121402", Name = "Cam sành Tiền Giang mọng nước kg", BaseUnit = "Kg",
                        CostPrice = 18000m, SellingPrice = 25000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 2, CreatedHoursAgo = 4,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1557800636-894a64c1696f?w=500&auto=format&fit=crop&q=60" },
                        Description = "Cam sành Tiền Giang vỏ xanh bóng tép mọng nước chuyên vắt nước giải khát.",
                        InternalNote = "Kệ trái cây tươi thông thoáng."
                    },
                    new()
                    {
                        Code = "SP-FRU-003", Barcode = "893850121403", Name = "Nho đen không hạt Autumn Royal Mỹ khay 500g", BaseUnit = "Khay",
                        CostPrice = 95000m, SellingPrice = 135000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 3, CreatedHoursAgo = 5,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1537640538966-79f369143f8f?w=500&auto=format&fit=crop&q=60" },
                        Description = "Nho đen ngón tay Autumn Royal Mỹ quả thuôn dài giòn đượm vị ngọt ngào.",
                        InternalNote = "Tủ mát trái cây 4 độ C."
                    },
                    new()
                    {
                        Code = "SP-FRU-004", Barcode = "893850121404", Name = "Xoài cát Hòa Lộc Tiền Giang loại 1 kg", BaseUnit = "Kg",
                        CostPrice = 55000m, SellingPrice = 75000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 4, CreatedHoursAgo = 6,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1557800636-894a64c1696f?w=500&auto=format&fit=crop&q=60" },
                        Description = "Xoài cát Hòa Lộc quả to đẹp chín vàng ươm thịt thơm ngọt đậm đà không xơ.",
                        InternalNote = "Kệ trái cây chín."
                    },
                    new()
                    {
                        Code = "SP-FRU-005", Barcode = "893850121405", Name = "Chuối tiêu hồng thơm chín cây nải kg", BaseUnit = "Kg",
                        CostPrice = 16000m, SellingPrice = 22000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 5, CreatedHoursAgo = 5,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1571771894821-ce9b6c11b08e?w=500&auto=format&fit=crop&q=60" },
                        Description = "Chuối tiêu hồng chín cây tự nhiên quả cong đều ngọt ngào bổ dưỡng.",
                        InternalNote = "Kệ chuối tươi."
                    },
                    new()
                    {
                        Code = "SP-FRU-006", Barcode = "893850121406", Name = "Dưa hấu ruột đỏ Long An quả 2-3kg", BaseUnit = "Quả",
                        CostPrice = 28000m, SellingPrice = 39000m, VatRate = 0m, TaxCategory = "STANDARD_1PCT", HasBatches = true,
                        CreatedDaysAgo = 6, CreatedHoursAgo = 6,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1587049352846-4a222e784d38?w=500&auto=format&fit=crop&q=60" },
                        Description = "Dưa hấu không hạt ruột đỏ Long An vỏ mỏng ngọt lịm mọng nước giải nhiệt.",
                        InternalNote = "Kệ dưa hấu sàn quầy."
                    }
                }
            },

            // 15. Đồ Dùng Gia Đình & Tiện Ích
            new SeedCategoryItem
            {
                Code = "HOUSE",
                Name = "Đồ Dùng Gia Đình & Tiện Ích",
                Description = "Màng bọc thực phẩm, túi rác tự hủy, giấy bạc và vật dụng nhà bếp",
                Products = new List<SeedProductItem>
                {
                    new()
                    {
                        Code = "SP-HOU-001", Barcode = "893850121501", Name = "Màng bọc thực phẩm PE Sunhouse cuộn 100m", BaseUnit = "Cuộn",
                        CostPrice = 42000m, SellingPrice = 56000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 7, CreatedHoursAgo = 2,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1584308666744-24d5c474f2ae?w=500&auto=format&fit=crop&q=60" },
                        Description = "Màng bọc thực phẩm Sunhouse nhựa PE an toàn có dao cắt tiện lợi bảo quản đồ ăn.",
                        InternalNote = "Kệ đồ gia dụng tiện ích.",
                        Units = new()
                        {
                            new() { UnitName = "Lốc", ConversionRate = 6m, Price = 320000m, Barcode = "8938501215016" }
                        }
                    },
                    new()
                    {
                        Code = "SP-HOU-002", Barcode = "893850121502", Name = "Túi rác sinh học tự hủy Opec cuộn 3 màu 1kg", BaseUnit = "Cuộn",
                        CostPrice = 36000m, SellingPrice = 48000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 8, CreatedHoursAgo = 2,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1584308666744-24d5c474f2ae?w=500&auto=format&fit=crop&q=60" },
                        Description = "Túi đựng rác tự phân hủy sinh học thân thiện môi trường cuộn 3 màu tiện dụng.",
                        InternalNote = "Kệ túi rác đồ vệ sinh."
                    },
                    new()
                    {
                        Code = "SP-HOU-003", Barcode = "893850121503", Name = "Giấy bạc nướng thực phẩm Goodfoil cuộn 5m", BaseUnit = "Cuộn",
                        CostPrice = 24000m, SellingPrice = 32000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 9, CreatedHoursAgo = 1,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1584308666744-24d5c474f2ae?w=500&auto=format&fit=crop&q=60" },
                        Description = "Màng nhôm nướng thực phẩm Goodfoil giữ nhiệt tốt chịu nhiệt cao trong lò nướng và nồi chiên.",
                        InternalNote = "Kệ giấy bạc màng bọc."
                    },
                    new()
                    {
                        Code = "SP-HOU-004", Barcode = "893850121504", Name = "Miếng rửa chén bọt biển 2 mặt Scotch-Brite lốc 3 miếng", BaseUnit = "Lốc",
                        CostPrice = 18000m, SellingPrice = 25000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 10, CreatedHoursAgo = 3,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1584308666744-24d5c474f2ae?w=500&auto=format&fit=crop&q=60" },
                        Description = "Miếng cọ rửa Scotch-Brite 3M mặt ráp chà sạch xoong chảo mặt bọt biển tạo bọt mịn.",
                        InternalNote = "Kệ miếng rửa chén."
                    },
                    new()
                    {
                        Code = "SP-HOU-005", Barcode = "893850121505", Name = "Pin kiềm AA Panasonic Alkaline vỉ 4 viên", BaseUnit = "Vỉ",
                        CostPrice = 38000m, SellingPrice = 49000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        CreatedDaysAgo = 11, CreatedHoursAgo = 4,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1584308666744-24d5c474f2ae?w=500&auto=format&fit=crop&q=60" },
                        Description = "Pin kiềm Panasonic Alkaline dung lượng cao chống rò rỉ bền bỉ cho đồ gia dụng.",
                        InternalNote = "Kệ pin bóng đèn quầy thu ngân."
                    },
                    new()
                    {
                        Code = "SP-HOU-006", Barcode = "893850121506", Name = "Bật lửa an toàn cán dài châm bếp Bic cây", BaseUnit = "Cây",
                        CostPrice = 18000m, SellingPrice = 25000m, VatRate = 8m, TaxCategory = "STANDARD_1PCT",
                        Status = "Discontinued", // Để test bộ lọc trạng thái "Ngừng kinh doanh"
                        CreatedDaysAgo = 12, CreatedHoursAgo = 5,
                        ImageUrls = new() { "https://images.unsplash.com/photo-1584308666744-24d5c474f2ae?w=500&auto=format&fit=crop&q=60" },
                        Description = "Bật lửa cán dài Bic châm bếp gas, bếp cồn an toàn tiện lợi không lo bỏng tay.",
                        InternalNote = "Tạm ngừng kinh doanh chuyển sang mẫu sạc điện.",
                        Units = new()
                        {
                            new() { UnitName = "Hộp", ConversionRate = 12m, Price = 280000m, Barcode = "8938501215062" }
                        }
                    }
                }
            }
        };
    }
}
