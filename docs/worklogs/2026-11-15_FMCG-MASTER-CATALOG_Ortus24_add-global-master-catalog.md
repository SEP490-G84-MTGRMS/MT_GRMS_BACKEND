# Worklog: 2026-11-15 — FMCG-MASTER-CATALOG — Xây dựng Kho Danh mục Hàng hóa Mẫu Toàn Cầu (Global FMCG Master Catalog)

- **Author**: @Ortus24
- **Task/Feature**: `global-master-catalog-fmcg`
- **Dự án**: `project/MT_GRMS_BACKEND`
- **Mức độ rủi ro (Risk Level)**: Low (Bổ sung 2 thực thể mới `MasterProduct` và `MasterProductUnit` thuộc Platform Scope, không ảnh hưởng Multi-tenancy filters của các bảng bán lẻ cốt lõi).

---

## 1. Mục đích (Why)
- Giải quyết bài toán Cold-Start của chủ tiệm tạp hóa khi mới onboard lên phần mềm SaaS MT-GRMS: Tiệm tạp hóa có 1.500 – 4.000 SKU, việc gõ thủ công từng tên, ảnh và quy cách mất hàng tuần.
- Xây dựng Thư viện Hàng hóa Mẫu Toàn Cầu (5.000+ mã vạch EAN-13 chuẩn hóa) cho phép Super Admin quản trị và import hàng loạt bằng ClosedXML.
- Cung cấp endpoint tra cứu barcode công khai (`GET /api/products/lookup-master?barcode=...`) giúp Tenant tự động nạp 100% Tên chuẩn, Ảnh sắc nét, Đơn vị cơ bản và Quy cách đóng gói (Thùng/Lốc/Gói/Lon) khi quét mã vạch.

---

## 2. Danh sách file & Nội dung sửa (What)
- **Tầng Domain (`MT_GRMS.Domain`)**:
  - `Entities/Catalog/MasterProduct.cs`: Thực thể sản phẩm mẫu toàn cục kế thừa `BaseEntity` (Barcode EAN-13, Name, Brand, CategoryName, BaseUnit, SuggestedRetailPrice, VatRate, ImageUrl, UsageCount, IsActive).
  - `Entities/Catalog/MasterProductUnit.cs`: Thực thể quy cách đa đơn vị kế thừa `BaseEntity` (UnitName, ConversionRate, Barcode, IsPurchaseUnit).
- **Tầng Infrastructure (`MT_GRMS.Infrastructure`)**:
  - `Persistence/Configurations/Catalog/MasterProductConfiguration.cs`: Cấu hình Fluent API bảng `MasterProducts`, Unique Index trên cột `Barcode` cho bản ghi active.
  - `Persistence/Configurations/Catalog/MasterProductUnitConfiguration.cs`: Cấu hình Fluent API bảng `MasterProductUnits` với khóa ngoại cascade tới `MasterProduct`.
  - `Persistence/AppDbContext.cs`: Khai báo `DbSet<MasterProduct>` và `DbSet<MasterProductUnit>` (độc lập với `BaseTenantEntity`).
  - `Repositories/MasterProductRepository.cs`: Xử lý truy vấn phân trang, tìm kiếm từ khóa, tính toán 4 chỉ số KPI và tăng lượt sử dụng `UsageCount`.
  - `Services/MasterCatalogExcelService.cs`: Sử dụng `ClosedXML` để xuất file template chuẩn hóa `Master_Catalog_Template.xlsx` và xử lý Import Excel hàng loạt (hỗ trợ chế độ Upsert ghi đè và parse cú pháp quy cách `Thùng:30:893...`).
  - `DependencyInjection.cs`: Đăng ký `IMasterProductRepository` và `IMasterCatalogExcelService`.
- **Tầng Application (`MT_GRMS.Application`)**:
  - `Admin/Dtos/MasterCatalogDtos.cs`: Định nghĩa đầy đủ DTOs cho Filter, Detail, Create, Update, KPIs, Excel Import và Barcode Suggestion.
  - `Admin/Repositories/IMasterProductRepository.cs`: Interface repository cho Master Catalog.
  - `Admin/Services/IMasterCatalogService.cs` & `MasterCatalogService.cs`: Tầng nghiệp vụ xử lý CRUD, phân trang PagedResult và tra cứu barcode.
  - `Admin/Services/IMasterCatalogExcelService.cs`: Interface cho dịch vụ xử lý Excel ClosedXML.
  - `DependencyInjection.cs`: Đăng ký `IMasterCatalogService`.
- **Tầng Presentation (`MT_GRMS.Api`)**:
  - `Controllers/AdminMasterCatalogController.cs`: Cung cấp trọn vẹn bộ API quản trị cho Super Admin (`RoleConstants.AdminOnly`): CRUD, KPIs, Brands, Categories, Tải template và Upload Excel.
  - `Controllers/ProductsController.cs`: Bổ sung endpoint `GET /api/products/lookup-master?barcode={barcode}` phục vụ auto-fill cho Tenant.

---

## 3. Kết quả Kiểm thử & Xác minh (Verification)
- Đã chạy `dotnet build MT_GRMS_BACKEND.sln`: Hoàn thành với **0 Error(s)**.
- Đã chạy `npm run build` trên `MT_GRMS_FRONTEND`: Hoàn thành với **0 lỗi TypeScript**.
- Đảm bảo an toàn 100% không vi phạm Multi-tenancy Global Query Filter của EF Core.
