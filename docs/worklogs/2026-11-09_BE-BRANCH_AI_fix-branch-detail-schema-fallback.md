# Worklog: Sửa Lỗi 500 BranchProducts & Tối Ưu Fallback Thống Kê Chi Nhánh

- **Ngày thực hiện**: 2026-11-09
- **Mã task / Phân hệ**: BE-BRANCH / Store Management (Quản lý chi nhánh)
- **Tác giả**: AI Assistant
- **Trạng thái**: Hoàn thành (Verified)

---

## 1. Mục Đích (Why)
- Khi người dùng bấm vào xem thông tin và thông số chi nhánh tại màn hình Cửa hàng, API `GET /api/branches/{id}/detail` trả về lỗi `500 Internal Server Error` do bảng `BranchProducts` trong cơ sở dữ liệu PostgreSQL chưa được tạo (`42P01: relation "BranchProducts" does not exist`).
- Cần tự động đồng bộ lược đồ bảng `BranchProducts` vào cơ sở dữ liệu khi khởi động, đồng thời nâng cấp cơ chế fallback thông minh: nếu chi nhánh chưa có danh mục catalog `BranchProducts` riêng biệt thì tự động lấy số liệu từ bảng tồn kho `BranchStocks` và danh sách nhân sự `Employees` mà không làm sập API.

---

## 2. Chi Tiết Thay Đổi (What)

### 2.1. `src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`
- Thêm phương thức `EnsureBranchProductsSchemaAsync`:
  - Thực thi câu lệnh SQL an toàn `CREATE TABLE IF NOT EXISTS "BranchProducts"` với đầy đủ các trường: `Id`, `TenantId`, `BranchId`, `ProductId`, `SellingPriceOverride`, `Status`, `ShelfLocation`, `CreatedAt`, `UpdatedAt`, `IsDeleted`, `CreatedBy`.
  - Tạo chỉ mục tăng tốc độ truy vấn: `IX_BranchProducts_BranchId` và `IX_BranchProducts_ProductId`.
- Bổ sung gọi `await EnsureBranchProductsSchemaAsync(context, logger);` vào phương thức khởi tạo chính `InitializeAsync`.

### 2.2. `src/MT_GRMS.Infrastructure/Repositories/BranchRepository.cs`
- Nâng cấp phương thức `GetBranchDetailAsync`:
  - Bọc khối truy vấn catalog `BranchProducts` trong `try-catch` để xử lý ngoại lệ an toàn nếu bảng đang trong quá trình khởi tạo.
  - Bổ sung cơ chế Fallback thông minh: khi `totalProducts == 0` nhưng `branchStocks.Count > 0`, tự động trích xuất các SKU và danh sách sản phẩm gần đây (`recentProducts`) trực tiếp từ `BranchStocks`.
  - Bọc khối đếm và trích xuất nhân sự `Employees` trong `try-catch` phòng thủ.

---

## 3. Điểm Nhấn Kỹ Thuật (Technical Highlights)
- **DDL Idempotent**: Khởi tạo bảng với cú pháp an toàn `IF NOT EXISTS`, không gây lỗi nếu bảng đã tồn tại sẵn trong môi trường thực thi.
- **Graceful Fallback**: Đảm bảo API `GET /api/branches/{id}/detail` luôn trả về mã HTTP `200 OK` kèm đầy đủ số liệu tồn kho, SKU, nhân sự thay vì văng exception làm gián đoạn trải nghiệm người dùng.

---

## 4. Kết Quả Kiểm Thử (Verification)
- **Build**: `dotnet build src/MT_GRMS.Api/MT_GRMS.Api.csproj` biên dịch thành công với 0 cảnh báo/lỗi.
- **API Test**:
  - `GET /api/branches/01a181dc-5902-71fe-ba6d-aeb9700b4743/detail` -> HTTP 200 OK.
    - `isHeadquarter`: `true`
    - `totalEmployees`: `6`
    - `recentEmployees`: 5 nhân viên đầy đủ họ tên, mã NV và trạng thái.
  - `PUT /api/branches/01a181dc-5902-71fe-ba6d-aeb9700b4743` -> HTTP 200 OK ("Cập nhật thông tin chi nhánh thành công.").

---

## 5. Ghi Chú Dành Cho Quản Lý Khi Merge (Manager Merge Notes)
- **Mức độ rủi ro**: Thấp (Low).
- **Tác động phụ**: Không có breaking change. Bảng `BranchProducts` tự động sinh khi backend khởi động lại.
