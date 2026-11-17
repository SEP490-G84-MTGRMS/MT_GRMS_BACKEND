# WORKLOG: FE-03, FE-12 - Catalog Pricing History, Branch Product Management, and SePay Webhook Integration

- **Ngày thực hiện**: 2026-11-16
- **Người thực hiện**: Son Nguyen Ba (@sonnguyenn10)
- **Nhánh Git (Branch)**: `feat/pos-v6`
- **Mã tính năng liên kết**: [FE-03 - Quản lý hàng hóa & Lịch sử giá](../../../DOCS_MT_GRMS/specs/FE-03/spec.md), [FE-12 - POS Checkout & SePay VietQR](../../../DOCS_MT_GRMS/specs/FE-12/spec.md)
- **Loại thay đổi**: Feature & Integration & Testing

---

### 1. Mục Đích Thay Đổi (Why)
- **Theo dõi lịch sử biến động giá (Price History)**: Bổ sung khả năng lưu vết chi tiết từng lần thay đổi giá bán/giá vốn của sản phẩm (bao gồm giá cũ, giá mới, lý do thay đổi, người thực hiện), phục vụ kiểm toán và hiển thị biểu đồ/bảng lịch sử giá trên màn hình quản lý hàng hóa.
- **Quản lý sản phẩm chi nhánh (Branch Product Management)**: Nâng cấp `BranchProductService` và repository để hỗ trợ thiết lập giá bán riêng cho từng chi nhánh, đồng bộ cấu hình đơn vị tính quy đổi, kiểm tra tính hợp lệ khi cập nhật hoặc xóa đơn vị tính con.
- **Tích hợp Webhook SePay VietQR**: Tinh gọn và chuẩn hóa luồng xử lý Webhook từ SePay (`PaymentsController`), xác thực an toàn, tự động kích hoạt thanh toán đơn hàng POS ngay khi tiền vào tài khoản.
- **Xử lý ngoại lệ tập trung**: Cập nhật `ExceptionHandlingMiddleware` để bắt và chuẩn hóa các lỗi nghiệp vụ như `KeyNotFoundException`, `InvalidOperationException`, đảm bảo trả về cấu trúc lỗi JSON đồng nhất cho Frontend.

---

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)
- `src/MT_GRMS.Api/Controllers/PaymentsController.cs`: Chuẩn hóa endpoint xử lý Webhook SePay, loại bỏ logic trùng lặp và tối ưu hóa phản hồi.
- `src/MT_GRMS.Api/Controllers/ProductsController.cs`: Cập nhật endpoint tra cứu sản phẩm và lịch sử giá.
- `src/MT_GRMS.Api/Middlewares/ExceptionHandlingMiddleware.cs`: Bổ sung xử lý lỗi tập trung cho các ngoại lệ nghiệp vụ.
- `src/MT_GRMS.Domain/Entities/Catalog/ProductPriceHistory.cs`: Thực thể lưu trữ lịch sử thay đổi giá hàng hóa.
- `src/MT_GRMS.Infrastructure/Persistence/Configurations/Catalog/ProductPriceHistoryConfiguration.cs`: Cấu hình Fluent API EF Core cho bảng `ProductPriceHistories`.
- `src/MT_GRMS.Application/Catalog/IProductPriceHistoryRepository.cs` & `ProductPriceHistoryRepository.cs`: Triển khai truy vấn lịch sử biến động giá theo sản phẩm.
- `src/MT_GRMS.Application/Catalog/CatalogService.cs` & `ProductDtos.cs`: Nghiệp vụ lưu vết giá khi tạo/sửa sản phẩm, mapping DTO lịch sử giá.
- `src/MT_GRMS.Application/Catalog/ProductUnitService.cs`: Quản lý đơn vị tính quy đổi, kiểm tra ràng buộc trước khi cập nhật/xóa đơn vị.
- `src/MT_GRMS.Application/Branches/BranchProductService.cs` & `BranchProductRepository.cs`: Quản lý sản phẩm theo chi nhánh, ghi đè giá bán chi nhánh và tra cứu barcode.
- `tests/MT_GRMS.UnitTests/Catalog/CatalogServiceTests.cs`: Unit test nghiệp vụ Catalog và Price History.
- `tests/MT_GRMS.UnitTests/Catalog/ProductUnitServiceTests.cs`: Unit test quản lý đơn vị quy đổi.
- `tests/MT_GRMS.UnitTests/BranchProductManagementTests.cs`: Unit test phân quyền và gán sản phẩm chi nhánh.

---

### 3. API Endpoints & Contract
- `POST /api/v1/payments/sepay-webhook`: Nhận thông báo biến động số dư và xác nhận thanh toán đơn POS từ SePay.
- `GET /api/v1/products/{id}/price-history`: Lấy lịch sử biến động giá của một mặt hàng.
- `GET /api/v1/branch-products/branch/{branchId}`: Lấy danh sách sản phẩm theo chi nhánh kèm giá ghi đè.

---

### 4. Kiểm Thử & Xác Nhận (Verification)
- [x] Đã tạo file worklog markdown trong thư mục `docs/worklogs/`.
- [x] Chạy biên dịch toàn bộ solution `MT_GRMS_BACKEND`: Thành công (0 lỗi, 0 cảnh báo).
- [x] Đã chạy unit test `MT_GRMS.UnitTests`: 1054/1054 tests PASS (100%).
- [x] Tuân thủ Clean Architecture: Controller không trỏ trực tiếp Domain Entity, giao tiếp qua Service và DTO.
- [x] Đã pull nhánh `dev` mới nhất và giải quyết toàn bộ xung đột (0 conflict).
