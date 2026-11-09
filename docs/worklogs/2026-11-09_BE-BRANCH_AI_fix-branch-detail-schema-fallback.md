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

### 2.3. `src/MT_GRMS.Infrastructure/Services/WalletService.cs`
- Khắc phục lỗi `InvalidOperationException`: *"The configured execution strategy 'NpgsqlRetryingExecutionStrategy' does not support user-initiated transactions. Use the execution strategy returned by 'DbContext.Database.CreateExecutionStrategy()' to execute all the operations in the transaction as a retriable unit."* khi tiếp nhận IPN Webhook từ cổng thanh toán SePay (`ProcessSepayWebhookAsync`).
- Bọc toàn bộ khối transaction cập nhật số dư ví và tạo bản ghi lịch sử `WalletTransaction` bên trong `strategy.ExecuteAsync(...)`, đảm bảo tuân thủ nghiêm ngặt cơ chế retry connection của Npgsql và EF Core 8.

---

## 3. Điểm Nhấn Kỹ Thuật (Technical Highlights)
- **DDL Idempotent**: Khởi tạo bảng với cú pháp an toàn `IF NOT EXISTS`, không gây lỗi nếu bảng đã tồn tại sẵn trong môi trường thực thi.
- **Graceful Fallback**: Đảm bảo API `GET /api/branches/{id}/detail` luôn trả về mã HTTP `200 OK` kèm đầy đủ số liệu tồn kho, SKU, nhân sự thay vì văng exception làm gián đoạn trải nghiệm người dùng.
- **Resilient Payment Transactions**: Toàn bộ thao tác cộng tiền vào Ví qua SePay IPN Webhook được bảo vệ bởi Retrying Execution Strategy của Npgsql, chống lỗi ngắt kết nối mạng và đảm bảo tính nguyên tử (Atomicity).

---

## 4. Kết Quả Kiểm Thử (Verification)
- **Build**: `dotnet build src/MT_GRMS.Api/MT_GRMS.Api.csproj` biên dịch thành công với 0 cảnh báo/lỗi.
- **API Test**:
  - `GET /api/branches/01a181dc-5902-71fe-ba6d-aeb9700b4743/detail` -> HTTP 200 OK.
  - `PUT /api/branches/01a181dc-5902-71fe-ba6d-aeb9700b4743` -> HTTP 200 OK ("Cập nhật thông tin chi nhánh thành công.").
- **Kiểm thử tích hợp End-to-End (Tạo tài khoản -> Nạp ví -> Mua gói)**:
  - `POST /api/auth/register`: Đăng ký thành công tài khoản chủ cửa hàng mới và cấp AccessToken.
  - `POST /api/tenant/wallet/topup-order`: Tạo đơn nạp 1.000.000 đ thành công, sinh cú pháp chuyển khoản chuẩn Napas247.
  - `POST /api/webhooks/sepay`: Xử lý Webhook SePay cộng 1.000.000 đ vào Ví thành công qua Execution Strategy.
  - `POST /api/tenant/subscriptions/purchase`: Mua Gói Thường 6 tháng + Addon quản lý nhân viên (851.400 đ gồm VAT 10%), trừ tiền ví khả dụng chính xác từ 1.000.000 đ xuống còn 148.600 đ.
  - Gói cước cập nhật sang `STANDARD`, ngày hết hạn hợp đồng cộng thêm 185 ngày (~6 tháng), ghi nhận đầy đủ 2 giao dịch trong Sổ cái và 2 bản ghi lịch sử hợp đồng.

---

## 5. Ghi Chú Dành Cho Quản Lý Khi Merge (Manager Merge Notes)
- **Mức độ rủi ro**: Thấp (Low).
- **Tác động phụ**: Không có breaking change. Bảng `BranchProducts` tự động sinh khi backend khởi động lại.
