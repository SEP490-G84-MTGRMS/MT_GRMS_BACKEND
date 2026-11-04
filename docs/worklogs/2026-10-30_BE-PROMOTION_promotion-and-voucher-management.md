# WORKLOG: FE-16 - Phát triển Backend Khuyến mại & Chiến dịch Voucher

- **Ngày thực hiện**: 2026-10-30
- **Người thực hiện**: @sonnguyenn10 / Antigravity AI Pair Programmer
- **Nhánh Git (Branch)**: `dev` / `feature/be16-promotion-voucher-management`
- **Mã tính năng liên kết**: FE-16 (Khuyến mại, Voucher & Loyalty)
- **Loại thay đổi**: Feature

---

### 1. Mục Đích Thay Đổi (Why)
- Hiện thực hóa đầy đủ phân hệ Khuyến mãi & Voucher (FE-16) cho chuỗi bán lẻ MT-GRMS:
  1. Chuẩn hóa CSDL 3NF khớp 100% sơ đồ ERD: Entity `Promotion` mở rộng các trường hạn mức, bổ sung bảng liên kết nhiều - nhiều `PromotionConditions` (quan hệ `eligible_for` mang `ProductRole`, `MinQty`), và bảng quản lý chi tiết từng mã voucher `VoucherCodes`.
  2. Toàn bộ ID thực thể chuẩn hóa sử dụng kiểu `Guid` (UUIDv7 chuẩn RFC 9562 sinh bằng `Uuid7.NewGuid()`).
  3. Cung cấp bộ tính toán chiết khấu tự động (Discount Engine) cho giỏ hàng POS: giảm giá % có chặn trần (`MaxDiscountAmount`), giảm tiền mặt cố định, Mua X tặng Y (`BUY_X_GET_Y`), và thẩm định/đổi mã voucher.
  4. Cung cấp trọn bộ REST API Controllers kết nối với màn hình quản trị của Store Owner và quầy thu ngân POS.

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)
- **Domain**:
  - `src/MT_GRMS.Domain/Entities/Sales/Promotion.cs`: Mở rộng thuộc tính `MinOrderValue`, `MaxDiscountAmount`, `UsageLimit`, `UsedCount`, `TargetBranches`, `BudgetLimit`, `TotalDiscountGiven`, `VoucherPrefix`, `TotalVoucherQuantity`, và navigations.
  - `src/MT_GRMS.Domain/Entities/Sales/PromotionCondition.cs`: Entity biểu diễn quan hệ $M:N$ `eligible_for` (`PromotionId`, `ProductId`, `ProductRole`, `MinQty`, `DiscountValue`, `Note`).
  - `src/MT_GRMS.Domain/Entities/Sales/VoucherCode.cs`: Entity lưu trữ từng mã voucher phát hành (`Code`, `Status`, `CustomerId`, `OrderId`, `OrderCode`, `UsedAt`).
- **Infrastructure**:
  - `src/MT_GRMS.Infrastructure/Persistence/Configurations/Sales/PromotionConfiguration.cs`: Cập nhật mapping Fluent API.
  - `src/MT_GRMS.Infrastructure/Persistence/Configurations/Sales/PromotionConditionConfiguration.cs`: Mapping Fluent API bảng `PromotionConditions`.
  - `src/MT_GRMS.Infrastructure/Persistence/Configurations/Sales/VoucherCodeConfiguration.cs`: Mapping Fluent API bảng `VoucherCodes`.
  - `src/MT_GRMS.Infrastructure/Persistence/AppDbContext.cs`: Đăng ký `DbSet<PromotionCondition>` và `DbSet<VoucherCode>`.
  - `src/MT_GRMS.Infrastructure/Repositories/PromotionRepository.cs`: Kế thừa `GenericRepository`, hỗ trợ phân trang, lọc status, eager loading và atomic status update.
  - `src/MT_GRMS.Infrastructure/DependencyInjection.cs`: Đăng ký `IPromotionRepository`.
- **Application**:
  - `src/MT_GRMS.Application/Promotions/Dtos/`: `PromotionDtos.cs`, `VoucherDtos.cs`, `CartEvaluationDtos.cs`.
  - `src/MT_GRMS.Application/Promotions/Validators/`: `PromotionValidators.cs`, `VoucherValidators.cs` (FluentValidation).
  - `src/MT_GRMS.Application/Promotions/IPromotionRepository.cs`, `IPromotionService.cs`.
  - `src/MT_GRMS.Application/Promotions/PromotionService.cs`: Nghiệp vụ CRUD, đánh giá giỏ hàng POS, sinh mã batch và redeem.
  - `src/MT_GRMS.Application/DependencyInjection.cs`: Đăng ký `IPromotionService`.
- **Api**:
  - `src/MT_GRMS.Api/Controllers/PromotionsController.cs`: Endpoints `/api/v1/promotions` (kèm seeder trigger `POST /seed-sample-data`).
  - `src/MT_GRMS.Api/Controllers/VouchersController.cs`: Endpoints `/api/v1/vouchers`.
- **Infrastructure**:
  - `src/MT_GRMS.Infrastructure/Persistence/DatabaseSeeder.cs`: Tự động nạp sẵn 6 bộ dữ liệu mẫu thực tế cho `Promotions` & `VoucherCodes`.
- **Unit Tests**:
  - `tests/MT_GRMS.UnitTests/PromotionManagementTests.cs`: 6 tests CRUD, GUIDv7, duplicate code, tenant isolation, seeder idempotency.
  - `tests/MT_GRMS.UnitTests/VoucherManagementTests.cs`: 6 tests sinh mã, validate, redeem atomic.
  - `tests/MT_GRMS.UnitTests/PromotionEvaluationTests.cs`: 5 tests tính % chặn trần, tiền cố định, Buy X Get Y quà tặng, kết hợp voucher.

### 3. API Endpoints & Contract
- `GET /api/v1/promotions`: Danh sách khuyến mãi phân trang (`PromotionFilterQuery`).
- `GET /api/v1/promotions/{id}`: Chi tiết khuyến mãi kèm danh sách điều kiện sản phẩm.
- `POST /api/v1/promotions`: Tạo mới chương trình khuyến mãi.
- `PUT /api/v1/promotions/{id}`: Cập nhật chương trình khuyến mãi.
- `PATCH /api/v1/promotions/{id}/status`: Bật/Tắt trạng thái kích hoạt `IsActive`.
- `DELETE /api/v1/promotions/{id}`: Xóa mềm chương trình khuyến mãi.
- `POST /api/v1/promotions/evaluate-cart`: Tính toán chiết khấu và quà tặng giỏ hàng bán lẻ POS.
- `POST /api/v1/promotions/seed-sample-data`: Nạp dữ liệu mẫu khởi tạo cho tenant hiện tại.
- `GET /api/v1/vouchers`: Danh sách các đợt phát hành voucher (phân trang).
- `GET /api/v1/vouchers/{id}`: Chi tiết đợt voucher.
- `POST /api/v1/vouchers`: Tạo mới đợt voucher & tự động sinh mã ban đầu.
- `POST /api/v1/vouchers/{id}/generate-codes`: Sinh thêm mã voucher hàng loạt theo tiền tố.
- `GET /api/v1/vouchers/{id}/codes`: Lấy danh sách mã voucher con của đợt.
- `POST /api/v1/vouchers/validate`: Kiểm tra tính hợp lệ của mã voucher cho đơn hàng POS.
- `POST /api/v1/vouchers/redeem`: Đánh dấu mã voucher đã được sử dụng khi chốt đơn.

### 4. Kiểm Thử & Xác Nhận (Verification)
- [x] Đã chạy `dotnet build MT_GRMS_BACKEND.sln`: Thành công (0 lỗi, 0 cảnh báo).
- [x] Đã chạy `dotnet test MT_GRMS_BACKEND.sln`: Đạt 100% (145/145 tests PASS, 17/17 tests mới viết đều PASS).
- [x] Tuân thủ Clean Architecture: Controller chỉ nhận/trả DTOs, không expose Domain Entity ra ngoài.
- [x] Cô lập dữ liệu đa người thuê (Multi-Tenancy) và ID toàn bộ dùng GUIDv7 (`Uuid7.NewGuid()`).
- [x] Đã nạp sẵn dữ liệu mẫu thực tế hỗ trợ kiểm thử API qua Swagger/Postman ngay lập tức.

### 5. Ghi Chú Cho PM & Frontend
- Frontend đã có sẵn các DTOs tại `src/features/promotion/types/promotion.types.ts`, các trường dữ liệu và tên enum (`ORDER_DISCOUNT`, `BUY_X_GET_Y`, `FIXED_AMOUNT`, `PERCENT`, `VOUCHER`) hoàn toàn tương thích 1:1.
- Màn hình POS thanh toán (`PosPaymentPanel`) có thể gọi ngay `POST /api/v1/vouchers/validate` để kiểm tra mã và hiển thị số tiền giảm trực tiếp trên hóa đơn.

