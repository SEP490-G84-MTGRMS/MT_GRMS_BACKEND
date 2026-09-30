# WORKLOG: ARCH - Khởi Tạo Kiến Trúc Clean Architecture & Code-First Skeleton

- **Ngày thực hiện**: 2026-09-30
- **Người thực hiện**: Đặng Đỗ Minh Đức (Backend Lead) & Antigravity AI
- **Nhánh Git (Branch)**: `chore/arch-clean-code-first-setup`
- **Mã tính năng liên kết**: [Report-4 SDS](../../../Document/report/Report-4_Software_Design_Specification.md)
- **Loại thay đổi**: Architecture / Refactoring

---

### 1. Mục Đích Thay Đổi (Why)
- Chuẩn hóa cấu trúc Backend theo Clean Architecture 4 tầng (.NET 8).
- Thiết lập quy tắc cốt lõi: Domain Entity được đóng gói, chỉ Service và DbContext được chạm vào. Controller bên ngoài chỉ làm việc 100% qua DTO.
- Chuyển đổi cơ chế Database sang EF Core Code-First với Fluent API.
- Sửa lỗi nghiêm trọng trong bộ lọc đa người thuê `Global Query Filter` của `AppDbContext.cs`.
- Tạo module mẫu chuẩn `Catalog / Product` (Empty Skeleton) để các thành viên khác nhân bản.

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)
- `src/MT_GRMS.Domain/Enums/RetailEnums.cs`: Bổ sung `TenantStatus`, `PlanTier`, `CustomerTier`.
- `src/MT_GRMS.Infrastructure/Persistence/Configurations/ProductConfiguration.cs`: [MỚI] Khung mẫu Fluent API Code-First cho bảng `products`.
- `src/MT_GRMS.Infrastructure/Persistence/AppDbContext.cs`: Tự động nạp configurations từ assembly và cấu hình bộ lọc Tenant dynamic evaluation.
- `src/MT_GRMS.Application/Catalog/Dtos/ProductDtos.cs`: Định nghĩa DTO `ProductCreateRequest` và `ProductResponse`.
- `src/MT_GRMS.Application/Catalog/ICatalogService.cs`: Interface chuẩn chỉ nhận DTO và trả DTO.
- `src/MT_GRMS.Application/Catalog/CatalogService.cs`: Khung mẫu Service rỗng ném `NotImplementedException` với dependency injection sẵn sàng.
- `src/MT_GRMS.Api/Controllers/ProductsController.cs`: [MỚI] Controller mẫu chỉ nhận DTO, gọi Service và trả `ApiResponse<ProductResponse>`.
- `README.md`: Cập nhật toàn diện mô tả thiết kế kiến trúc, luồng dữ liệu và hướng dẫn CLI Code-First.

### 3. API Endpoints & Contract
- `GET /api/v1/products`: Lấy danh sách sản phẩm.
- `GET /api/v1/products/{id}`: Lấy chi tiết sản phẩm theo ID.
- `POST /api/v1/products`: Thêm mới sản phẩm (Body: `ProductCreateRequest`).

### 4. Kiểm Thử & Xác Nhận (Verification)
- [x] Đã chạy `dotnet build MT_GRMS_BACKEND.sln`: Succeeded (0 Warning, 0 Error).
- [x] Đã chạy `dotnet test MT_GRMS_BACKEND.sln`: Passed 100%.

### 5. Ghi Chú Cho PM & Frontend
- Frontend có thể tham khảo DTO contract `ProductResponse` và `ProductCreateRequest` để dựng Mock API hoặc gọi trực tiếp khi test.
- Nhóm Backend dựa vào module mẫu `Catalog` này để triển khai các tính năng FE-01 đến FE-26.
