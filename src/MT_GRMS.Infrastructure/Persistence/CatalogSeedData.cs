using Microsoft.EntityFrameworkCore;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Infrastructure.Persistence;

/// <summary>
/// Dữ liệu mẫu phong phú cho phân hệ Quản lý Hàng hóa & Danh mục (Catalog).
/// Cung cấp 15 danh mục siêu thị tiêu chuẩn, mỗi danh mục chứa từ 5 đến 8 sản phẩm thực tế.
/// </summary>
public static class CatalogSeedData
{
    private class SeedProductItem
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
        public bool HasBatches { get; set; } = false;
        public bool IsFavorite { get; set; } = false;
    }

    private class SeedCategoryItem
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<SeedProductItem> Products { get; set; } = new();
    }

    public static async Task SeedAsync(AppDbContext context, string tenantId)
    {
        var categoryTemplates = Get15CategoryTemplates();

        // 1. Duyệt qua 15 danh mục mẫu
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

            // 2. Thêm từ 5-8 sản phẩm cho danh mục
            var existingCodes = await context.Products
                .IgnoreQueryFilters()
                .Where(p => p.TenantId == tenantId && p.CategoryId == category.Id)
                .Select(p => p.ProductCode)
                .ToListAsync();

            var existingBarcodes = await context.Products
                .IgnoreQueryFilters()
                .Select(p => p.Barcode)
                .ToListAsync();

            var productsToAdd = new List<Product>();

            foreach (var prodTpl in catTpl.Products)
            {
                if (existingCodes.Contains(prodTpl.Code) || existingBarcodes.Contains(prodTpl.Barcode))
                {
                    continue;
                }

                productsToAdd.Add(new Product
                {
                    TenantId = tenantId,
                    CategoryId = category.Id,
                    ProductCode = prodTpl.Code,
                    Barcode = prodTpl.Barcode,
                    Name = prodTpl.Name,
                    BaseUnit = prodTpl.BaseUnit,
                    CostPrice = prodTpl.CostPrice,
                    SellingPrice = prodTpl.SellingPrice,
                    MinStock = prodTpl.MinStock,
                    MaxStock = prodTpl.MaxStock,
                    VatRate = prodTpl.VatRate,
                    ProductType = "STANDARD",
                    Status = "Active",
                    HasBatches = prodTpl.HasBatches,
                    IsFavorite = prodTpl.IsFavorite
                });
            }

            if (productsToAdd.Any())
            {
                await context.Products.AddRangeAsync(productsToAdd);
                await context.SaveChangesAsync();
            }
        }
    }

    private static List<SeedCategoryItem> Get15CategoryTemplates()
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
                    new() { Code = "SP-BEV-001", Barcode = "893850120101", Name = "Nước ngọt Coca Cola lon 330ml", BaseUnit = "Lon", CostPrice = 8500m, SellingPrice = 11000m, IsFavorite = true },
                    new() { Code = "SP-BEV-002", Barcode = "893850120102", Name = "Nước ngọt Pepsi lon 320ml", BaseUnit = "Lon", CostPrice = 8000m, SellingPrice = 10500m },
                    new() { Code = "SP-BEV-003", Barcode = "893850120103", Name = "Nước khoáng tự nhiên Lavie chai 500ml", BaseUnit = "Chai", CostPrice = 4000m, SellingPrice = 6000m },
                    new() { Code = "SP-BEV-004", Barcode = "893850120104", Name = "Trà xanh Không Độ chai 455ml", BaseUnit = "Chai", CostPrice = 7500m, SellingPrice = 10000m },
                    new() { Code = "SP-BEV-005", Barcode = "893850120105", Name = "Nước tăng lực Redbull lon 250ml", BaseUnit = "Lon", CostPrice = 11000m, SellingPrice = 15000m, IsFavorite = true },
                    new() { Code = "SP-BEV-006", Barcode = "893850120106", Name = "Sữa tươi tiệt trùng Vinamilk có đường 1L", BaseUnit = "Hộp", CostPrice = 30000m, SellingPrice = 38000m, HasBatches = true },
                    new() { Code = "SP-BEV-007", Barcode = "893850120107", Name = "Sữa chua uống tiệt trùng Yomost dâu lốc 4 hộp", BaseUnit = "Lốc", CostPrice = 24000m, SellingPrice = 31000m, HasBatches = true }
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
                    new() { Code = "SP-DRY-001", Barcode = "893850120201", Name = "Gạo thơm ST25 Ông Cua túi 5kg", BaseUnit = "Túi", CostPrice = 160000m, SellingPrice = 195000m, HasBatches = true, IsFavorite = true },
                    new() { Code = "SP-DRY-002", Barcode = "893850120202", Name = "Gạo Nàng Thơm Chợ Đào túi 5kg", BaseUnit = "Túi", CostPrice = 110000m, SellingPrice = 135000m, HasBatches = true },
                    new() { Code = "SP-DRY-003", Barcode = "893850120203", Name = "Đậu xanh bóc vỏ túi 500g", BaseUnit = "Túi", CostPrice = 25000m, SellingPrice = 33000m },
                    new() { Code = "SP-DRY-004", Barcode = "893850120204", Name = "Đậu đen xanh lòng túi 500g", BaseUnit = "Túi", CostPrice = 28000m, SellingPrice = 36000m },
                    new() { Code = "SP-DRY-005", Barcode = "893850120205", Name = "Nếp cái hoa vàng gói 1kg", BaseUnit = "Gói", CostPrice = 32000m, SellingPrice = 42000m },
                    new() { Code = "SP-DRY-006", Barcode = "893850120206", Name = "Hạt sen sấy giòn ăn liền gói 250g", BaseUnit = "Gói", CostPrice = 55000m, SellingPrice = 72000m }
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
                    new() { Code = "SP-FRH-001", Barcode = "893850120301", Name = "Thịt ba chỉ bò Mỹ đông lạnh khay 500g", BaseUnit = "Khay", CostPrice = 85000m, SellingPrice = 115000m, HasBatches = true, IsFavorite = true },
                    new() { Code = "SP-FRH-002", Barcode = "893850120302", Name = "Ức gà phi lê tươi CP khay 500g", BaseUnit = "Khay", CostPrice = 38000m, SellingPrice = 52000m, HasBatches = true },
                    new() { Code = "SP-FRH-003", Barcode = "893850120303", Name = "Cá hồi Na Uy phi lê tươi khay 300g", BaseUnit = "Khay", CostPrice = 155000m, SellingPrice = 205000m, HasBatches = true },
                    new() { Code = "SP-FRH-004", Barcode = "893850120304", Name = "Thịt nạc dăm heo MeatDeli khay 400g", BaseUnit = "Khay", CostPrice = 65000m, SellingPrice = 86000m, HasBatches = true },
                    new() { Code = "SP-FRH-005", Barcode = "893850120305", Name = "Tôm thẻ chân trắng tươi sống khay 500g", BaseUnit = "Khay", CostPrice = 90000m, SellingPrice = 125000m, HasBatches = true },
                    new() { Code = "SP-FRH-006", Barcode = "893850120306", Name = "Sườn non heo tươi sạch MeatDeli khay 500g", BaseUnit = "Khay", CostPrice = 95000m, SellingPrice = 129000m, HasBatches = true }
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
                    new() { Code = "SP-SPC-001", Barcode = "893850120401", Name = "Nước mắm Nam Ngư Đệ Nhị chai 900ml", BaseUnit = "Chai", CostPrice = 22000m, SellingPrice = 28500m },
                    new() { Code = "SP-SPC-002", Barcode = "893850120402", Name = "Nước tương Maggi Đậm Đặc chai 700ml", BaseUnit = "Chai", CostPrice = 21000m, SellingPrice = 27000m },
                    new() { Code = "SP-SPC-003", Barcode = "893850120403", Name = "Dầu ăn Tường An Cooking Oil chai 1L", BaseUnit = "Chai", CostPrice = 42000m, SellingPrice = 53000m, IsFavorite = true },
                    new() { Code = "SP-SPC-004", Barcode = "893850120404", Name = "Bột ngọt Ajinomoto hạt lớn gói 454g", BaseUnit = "Gói", CostPrice = 31000m, SellingPrice = 39000m },
                    new() { Code = "SP-SPC-005", Barcode = "893850120405", Name = "Hạt nêm Knorr thịt thăn xương ống gói 900g", BaseUnit = "Gói", CostPrice = 63000m, SellingPrice = 79000m },
                    new() { Code = "SP-SPC-006", Barcode = "893850120406", Name = "Đường tinh luyện Biên Hòa gói 1kg", BaseUnit = "Gói", CostPrice = 24000m, SellingPrice = 30500m },
                    new() { Code = "SP-SPC-007", Barcode = "893850120407", Name = "Tương ớt Chinsu đượm vị cay chai 250g", BaseUnit = "Chai", CostPrice = 11000m, SellingPrice = 15000m }
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
                    new() { Code = "SP-CNF-001", Barcode = "893850120501", Name = "Bánh ChocoPie Orion truyền thống hộp 12 cái", BaseUnit = "Hộp", CostPrice = 46000m, SellingPrice = 58000m, IsFavorite = true },
                    new() { Code = "SP-CNF-002", Barcode = "893850120502", Name = "Bánh quy bơ Danisa hộp thiếc 454g", BaseUnit = "Hộp", CostPrice = 115000m, SellingPrice = 145000m },
                    new() { Code = "SP-CNF-003", Barcode = "893850120503", Name = "Kẹo mềm Sugus hương trái cây gói 120g", BaseUnit = "Gói", CostPrice = 14000m, SellingPrice = 19000m },
                    new() { Code = "SP-CNF-004", Barcode = "893850120504", Name = "Snack khoai tây Lay's vị tự nhiên gói 95g", BaseUnit = "Gói", CostPrice = 16000m, SellingPrice = 22000m },
                    new() { Code = "SP-CNF-005", Barcode = "893850120505", Name = "Rong biển sấy giòn Taokaenoi gói 32g", BaseUnit = "Gói", CostPrice = 21000m, SellingPrice = 28000m },
                    new() { Code = "SP-CNF-006", Barcode = "893850120506", Name = "Bánh gạo One One vị ngọt dịu gói 150g", BaseUnit = "Gói", CostPrice = 22000m, SellingPrice = 29000m }
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
                    new() { Code = "SP-FRZ-001", Barcode = "893850120601", Name = "Cá nục sốt cà Three Lady Cooks hộp 155g", BaseUnit = "Hộp", CostPrice = 15000m, SellingPrice = 20000m },
                    new() { Code = "SP-FRZ-002", Barcode = "893850120602", Name = "Xúc xích tiệt trùng Vissan heo gói 5 cây", BaseUnit = "Gói", CostPrice = 18000m, SellingPrice = 24000m },
                    new() { Code = "SP-FRZ-003", Barcode = "893850120603", Name = "Sủi cảo tôm thịt Cầu Tre gói 500g", BaseUnit = "Gói", CostPrice = 65000m, SellingPrice = 84000m, HasBatches = true },
                    new() { Code = "SP-FRZ-004", Barcode = "893850120604", Name = "Nem rán truyền thống CP gói 500g", BaseUnit = "Gói", CostPrice = 48000m, SellingPrice = 63000m, HasBatches = true },
                    new() { Code = "SP-FRZ-005", Barcode = "893850120605", Name = "Há cảo mini tôm thịt CP khay 300g", BaseUnit = "Khay", CostPrice = 39000m, SellingPrice = 51000m, HasBatches = true },
                    new() { Code = "SP-FRZ-006", Barcode = "893850120606", Name = "Bò viên gân đặc biệt Vissan gói 200g", BaseUnit = "Gói", CostPrice = 36000m, SellingPrice = 48000m, HasBatches = true }
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
                    new() { Code = "SP-CSM-001", Barcode = "893850120701", Name = "Dầu gội Clear Men mát lạnh sạch gàu chai 630g", BaseUnit = "Chai", CostPrice = 145000m, SellingPrice = 182000m, IsFavorite = true },
                    new() { Code = "SP-CSM-002", Barcode = "893850120702", Name = "Sữa tắm Lifebuoy bảo vệ vượt trội chai 800g", BaseUnit = "Chai", CostPrice = 135000m, SellingPrice = 169000m },
                    new() { Code = "SP-CSM-003", Barcode = "893850120703", Name = "Kem đánh răng Colgate Total bảo vệ 12h tuýp 200g", BaseUnit = "Tuýp", CostPrice = 32000m, SellingPrice = 42000m },
                    new() { Code = "SP-CSM-004", Barcode = "893850120704", Name = "Bàn chải đánh răng P/S than tre kháng khuẩn", BaseUnit = "Cây", CostPrice = 18000m, SellingPrice = 26000m },
                    new() { Code = "SP-CSM-005", Barcode = "893850120705", Name = "Sữa rửa mặt Nivea Men sáng da sạch nhờn tuýp 100g", BaseUnit = "Tuýp", CostPrice = 68000m, SellingPrice = 89000m },
                    new() { Code = "SP-CSM-006", Barcode = "893850120706", Name = "Nước súc miệng Listerine Cool Mint chai 500ml", BaseUnit = "Chai", CostPrice = 92000m, SellingPrice = 119000m }
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
                    new() { Code = "SP-CLN-001", Barcode = "893850120801", Name = "Nước giặt OMO Matic cửa trên túi 3.6kg", BaseUnit = "Túi", CostPrice = 175000m, SellingPrice = 219000m, IsFavorite = true },
                    new() { Code = "SP-CLN-002", Barcode = "893850120802", Name = "Nước xả vải Comfort một lần xả hương ban mai túi 3.2L", BaseUnit = "Túi", CostPrice = 165000m, SellingPrice = 209000m },
                    new() { Code = "SP-CLN-003", Barcode = "893850120803", Name = "Nước rửa chén Sunlight chanh tươi túi 2.1kg", BaseUnit = "Túi", CostPrice = 52000m, SellingPrice = 67000m },
                    new() { Code = "SP-CLN-004", Barcode = "893850120804", Name = "Nước lau sàn Sunlight hương hoa hạ chai 1kg", BaseUnit = "Chai", CostPrice = 28000m, SellingPrice = 36000m },
                    new() { Code = "SP-CLN-005", Barcode = "893850120805", Name = "Nước tẩy bồn cầu Vim diệt khuẩn đậm đặc chai 900ml", BaseUnit = "Chai", CostPrice = 33000m, SellingPrice = 43000m },
                    new() { Code = "SP-CLN-006", Barcode = "893850120806", Name = "Khăn giấy lụa rút Pulppy 2 lớp lốc 3 gói", BaseUnit = "Lốc", CostPrice = 42000m, SellingPrice = 55000m }
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
                    new() { Code = "SP-INS-001", Barcode = "893850120901", Name = "Mì Hảo Hảo tôm chua cay gói 75g", BaseUnit = "Gói", CostPrice = 3800m, SellingPrice = 4800m, IsFavorite = true },
                    new() { Code = "SP-INS-002", Barcode = "893850120902", Name = "Mì xào tương đen Samyang Hàn Quốc gói 140g", BaseUnit = "Gói", CostPrice = 23000m, SellingPrice = 31000m },
                    new() { Code = "SP-INS-003", Barcode = "893850120903", Name = "Phở bò Đệ Nhất gói 68g", BaseUnit = "Gói", CostPrice = 6800m, SellingPrice = 8500m },
                    new() { Code = "SP-INS-004", Barcode = "893850120904", Name = "Cháo yến sào gà nấm YenViet gói 50g", BaseUnit = "Gói", CostPrice = 9000m, SellingPrice = 12000m },
                    new() { Code = "SP-INS-005", Barcode = "893850120905", Name = "Miến Phú Hương vị sườn heo gói 58g", BaseUnit = "Gói", CostPrice = 9500m, SellingPrice = 12500m },
                    new() { Code = "SP-INS-006", Barcode = "893850120906", Name = "Hủ tiếu Nam Vang Cung Đình tô 80g", BaseUnit = "Tô", CostPrice = 13000m, SellingPrice = 17500m }
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
                    new() { Code = "SP-TEA-001", Barcode = "893850121001", Name = "Cà phê hòa tan Nescafe 3in1 Đậm Đà hộp 20 gói", BaseUnit = "Hộp", CostPrice = 51000m, SellingPrice = 64000m, IsFavorite = true },
                    new() { Code = "SP-TEA-002", Barcode = "893850121002", Name = "Cà phê sữa G7 Trung Nguyên hộp 18 gói", BaseUnit = "Hộp", CostPrice = 47000m, SellingPrice = 59000m },
                    new() { Code = "SP-TEA-003", Barcode = "893850121003", Name = "Trà túi lọc Lipton nhãn vàng hộp 100 gói", BaseUnit = "Hộp", CostPrice = 72000m, SellingPrice = 91000m },
                    new() { Code = "SP-TEA-004", Barcode = "893850121004", Name = "Trà lài Thái Nguyên đặc sản gói 200g", BaseUnit = "Gói", CostPrice = 45000m, SellingPrice = 60000m },
                    new() { Code = "SP-TEA-005", Barcode = "893850121005", Name = "Trà đào sả Cozy hộp 16 gói", BaseUnit = "Hộp", CostPrice = 28000m, SellingPrice = 37000m },
                    new() { Code = "SP-TEA-006", Barcode = "893850121006", Name = "Bột cacao nguyên chất Đắk Lắk hộp 250g", BaseUnit = "Hộp", CostPrice = 65000m, SellingPrice = 85000m }
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
                    new() { Code = "SP-BBY-001", Barcode = "893850121101", Name = "Tã dán Bobby sơ sinh XS gói 70 miếng", BaseUnit = "Gói", CostPrice = 135000m, SellingPrice = 168000m },
                    new() { Code = "SP-BBY-002", Barcode = "893850121102", Name = "Tã quần Huggies Dry Pants size M 60 miếng", BaseUnit = "Gói", CostPrice = 215000m, SellingPrice = 265000m, IsFavorite = true },
                    new() { Code = "SP-BBY-003", Barcode = "893850121103", Name = "Khăn ướt em bé Fressi Care không cồn gói 80 miếng", BaseUnit = "Gói", CostPrice = 24000m, SellingPrice = 33000m },
                    new() { Code = "SP-BBY-004", Barcode = "893850121104", Name = "Sữa bột Meiji số 0 nội địa Nhật lon 800g", BaseUnit = "Lon", CostPrice = 46000m, SellingPrice = 535000m, HasBatches = true },
                    new() { Code = "SP-BBY-005", Barcode = "893850121105", Name = "Bột ăn dặm Nestle Cerelac gạo lức sữa hộp 200g", BaseUnit = "Hộp", CostPrice = 58000m, SellingPrice = 74000m, HasBatches = true },
                    new() { Code = "SP-BBY-006", Barcode = "893850121106", Name = "Sữa tắm gội toàn thân Johnson's Baby chai 500ml", BaseUnit = "Chai", CostPrice = 110000m, SellingPrice = 139000m }
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
                    new() { Code = "SP-BAK-001", Barcode = "893850121201", Name = "Bột mì đa dụng Hoa Ngọc Lan gói 1kg", BaseUnit = "Gói", CostPrice = 19000m, SellingPrice = 25000m },
                    new() { Code = "SP-BAK-002", Barcode = "893850121202", Name = "Bột chiên giòn Tài Ký gói 150g", BaseUnit = "Gói", CostPrice = 8000m, SellingPrice = 11500m },
                    new() { Code = "SP-BAK-003", Barcode = "893850121203", Name = "Ngũ cốc sấy giòn Calbee Nhật Bản túi 750g", BaseUnit = "Túi", CostPrice = 165000m, SellingPrice = 215000m, IsFavorite = true },
                    new() { Code = "SP-BAK-004", Barcode = "893850121204", Name = "Yến mạch nguyên chất Quaker Oats hộp 500g", BaseUnit = "Hộp", CostPrice = 55000m, SellingPrice = 73000m },
                    new() { Code = "SP-BAK-005", Barcode = "893850121205", Name = "Men nở khô Mauripan hộp 5 gói", BaseUnit = "Hộp", CostPrice = 16000m, SellingPrice = 22000m },
                    new() { Code = "SP-BAK-006", Barcode = "893850121206", Name = "Hạt chia Organic Úc giàu Omega-3 gói 500g", BaseUnit = "Gói", CostPrice = 120000m, SellingPrice = 155000m }
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
                    new() { Code = "SP-EGG-001", Barcode = "893850121301", Name = "Trứng gà sạch Ba Huân vỉ 10 quả", BaseUnit = "Vỉ", CostPrice = 26000m, SellingPrice = 33000m, HasBatches = true, IsFavorite = true },
                    new() { Code = "SP-EGG-002", Barcode = "893850121302", Name = "Trứng vịt tươi Ba Huân vỉ 10 quả", BaseUnit = "Vỉ", CostPrice = 32000m, SellingPrice = 39000m, HasBatches = true },
                    new() { Code = "SP-EGG-003", Barcode = "893850121303", Name = "Trứng cút tươi sạch vỉ 30 quả", BaseUnit = "Vỉ", CostPrice = 21000m, SellingPrice = 28000m, HasBatches = true },
                    new() { Code = "SP-EGG-004", Barcode = "893850121304", Name = "Đậu phụ trắng Meko miếng tươi 300g", BaseUnit = "Miếng", CostPrice = 8000m, SellingPrice = 11000m, HasBatches = true },
                    new() { Code = "SP-EGG-005", Barcode = "893850121305", Name = "Đậu hũ non trứng Ichiban lốc 2 cây", BaseUnit = "Lốc", CostPrice = 14000m, SellingPrice = 19000m, HasBatches = true },
                    new() { Code = "SP-EGG-006", Barcode = "893850121306", Name = "Trứng gà so thảo dược Dabaco vỉ 10 quả", BaseUnit = "Vỉ", CostPrice = 38000m, SellingPrice = 48000m, HasBatches = true }
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
                    new() { Code = "SP-FRU-001", Barcode = "893850121401", Name = "Táo Envy New Zealand khay 4 quả", BaseUnit = "Khay", CostPrice = 85000m, SellingPrice = 115000m, HasBatches = true, IsFavorite = true },
                    new() { Code = "SP-FRU-002", Barcode = "893850121402", Name = "Cam sành Tiền Giang mọng nước kg", BaseUnit = "Kg", CostPrice = 18000m, SellingPrice = 25000m, HasBatches = true },
                    new() { Code = "SP-FRU-003", Barcode = "893850121403", Name = "Nho đen không hạt Autumn Royal Mỹ khay 500g", BaseUnit = "Khay", CostPrice = 95000m, SellingPrice = 135000m, HasBatches = true },
                    new() { Code = "SP-FRU-004", Barcode = "893850121404", Name = "Xoài cát Hòa Lộc Tiền Giang loại 1 kg", BaseUnit = "Kg", CostPrice = 55000m, SellingPrice = 75000m, HasBatches = true },
                    new() { Code = "SP-FRU-005", Barcode = "893850121405", Name = "Chuối tiêu hồng thơm chín cây nải kg", BaseUnit = "Kg", CostPrice = 16000m, SellingPrice = 22000m, HasBatches = true },
                    new() { Code = "SP-FRU-006", Barcode = "893850121406", Name = "Dưa hấu ruột đỏ Long An quả 2-3kg", BaseUnit = "Quả", CostPrice = 28000m, SellingPrice = 39000m, HasBatches = true }
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
                    new() { Code = "SP-HOU-001", Barcode = "893850121501", Name = "Màng bọc thực phẩm PE Sunhouse cuộn 100m", BaseUnit = "Cuộn", CostPrice = 42000m, SellingPrice = 56000m },
                    new() { Code = "SP-HOU-002", Barcode = "893850121502", Name = "Túi rác sinh học tự hủy Opec cuộn 3 màu 1kg", BaseUnit = "Cuộn", CostPrice = 36000m, SellingPrice = 48000m },
                    new() { Code = "SP-HOU-003", Barcode = "893850121503", Name = "Giấy bạc nướng thực phẩm Goodfoil cuộn 5m", BaseUnit = "Cuộn", CostPrice = 24000m, SellingPrice = 32000m },
                    new() { Code = "SP-HOU-004", Barcode = "893850121504", Name = "Miếng rửa chén bọt biển 2 mặt Scotch-Brite lốc 3 miếng", BaseUnit = "Lốc", CostPrice = 18000m, SellingPrice = 25000m },
                    new() { Code = "SP-HOU-005", Barcode = "893850121505", Name = "Pin kiềm AA Panasonic Alkaline vỉ 4 viên", BaseUnit = "Vỉ", CostPrice = 38000m, SellingPrice = 49000m },
                    new() { Code = "SP-HOU-006", Barcode = "893850121506", Name = "Bật lửa an toàn cán dài châm bếp Bic cây", BaseUnit = "Cây", CostPrice = 18000m, SellingPrice = 25000m }
                }
            }
        };
    }
}
