# WORKLOG: FE-19 - Refactor Tax & Finance Backend, Live Neon DB Integration, and Merge Resolution

- **Ngày thực hiện**: 2026-10-30
- **Người thực hiện**: Antigravity AI Agent & Team
- **Nhánh Git (Branch)**: eature/refactor-tax-finance-backend
- **Mã tính năng liên kết**: FE-19 (Kế toán Thuế Hộ kinh doanh TT 152/2025/TT-BTC, Quản lý Tài chính, Két quỹ & Công nợ khách hàng)
- **Loại thay đổi**: Feature & Refactor & Integration

---

### 1. Mục Đích Thay Đổi (Why)
- Hợp nhất toàn diện các phân hệ Quản lý Tài chính, Thuế Hộ kinh doanh theo Thông tư 152/2025/TT-BTC, Két quỹ đa tầng và Công nợ khách hàng (FE-19) với kiến trúc 3NF nền tảng 34 bảng từ nhánh origin/dev.
- Kết nối CSDL Neon PostgreSQL trực tiếp (Live Database), hỗ trợ tự động áp dụng Migration (Database.MigrateAsync()) và nạp dữ liệu mẫu ban đầu (DatabaseSeeder).
- Chuyển đổi toàn bộ cấu hình mật, chuỗi kết nối và JWT sang tệp môi trường .env bảo mật cao.
- Giải quyết triệt để 7 tệp xung đột (merge conflicts) giữa origin/dev và feature branch mà không làm thất thoát bất kỳ chức năng nào của hai bên.

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)
- src/MT_GRMS.Domain/Entities/Catalog/Product.cs: Tích hợp chuẩn 3NF của dev, bổ sung TaxCategory (chuẩn NQ 204: STANDARD_1PCT vs NO_REDUCTION_1PCT), kèm alias tương thích ngược.
- src/MT_GRMS.Domain/Entities/Identity/User.cs: Hợp nhất Identity dev (BaseEntity<string>, Uuid7), bảo lưu 4 trường PIN bảo mật PBKDF2 (SecurityPinHash, SecurityPinSalt, FailedPinAttempts, PinLockedUntilUtc).
- src/MT_GRMS.Domain/Entities/Sales/Order.cs & OrderDetail.cs: Đồng bộ cấu trúc Sales.Order, bổ sung bóc tách thuế GTGT theo dòng hàng (AppliedVatRate, VatAmount, VatDiscountAmount).
- src/MT_GRMS.Infrastructure/Persistence/AppDbContext.cs: Tích hợp cả 7 phân hệ của dev cùng 13 bảng Tài chính, Thuế và Công nợ (tổng 49 bảng CSDL).
- src/MT_GRMS.Infrastructure/Persistence/DatabaseSeeder.cs: Chuẩn hóa dữ liệu mẫu, tự động truy vấn BranchID hợp lệ và kiểm tra ID danh mục/sản phẩm tránh xung đột khóa ngoại FK.
- src/MT_GRMS.Infrastructure/Persistence/Configurations/Finance/HouseholdTaxSnapshotConfiguration.cs: Chuẩn hóa ánh xạ cột AuditSignature cho chữ ký HMAC-SHA256.
- src/MT_GRMS.Infrastructure/Repositories/BranchRepository.cs & SupplierRepository.cs: Sửa lỗi EF Core LINQ Translation trên các trường [NotMapped] sang các cột vật lý trong DB.
- src/MT_GRMS.Api/Program.cs: Tích hợp EnvLoader.Load(), Legacy Timestamp Npgsql, JWT Authentication, MultiTenant Middleware, Auto-migration và Seed Data.

### 3. API Endpoints & Contract
- **GET** /api/health: Kiểm tra sức khỏe hệ thống và trạng thái kết nối DB.
- **POST** /api/auth/login: Xác thực tài khoản nhân viên / chủ hộ và cấp JWT Bearer Token.
- **GET** /api/v1/branches: Lấy danh sách chi nhánh (phân trang & bộ lọc).
- **GET** /api/tax/household/summary?year=2026&periodType=Quarter&periodNumber=3: Báo cáo thuế Hộ kinh doanh theo quý/năm (phương pháp khoán, doanh thu, thuế GTGT, TNCN).
- **GET** /api/tax/household/books: Truy xuất 4 sổ kế toán (S2b, S2c, S2d, S2e).
- **POST** /api/tax/household/snapshots/finalize: Chốt kỳ tính thuế và ký số HMAC-SHA256 với mã PIN chủ hộ.

### 4. Kiểm Thử & Xác Nhận (Verification)
- [x] Đã chạy dotnet build: Thành công 100% (0 lỗi, 0 cảnh báo).
- [x] Đã chạy dotnet test: 154/154 bài test đạt PASS (128 Unit Tests + 26 Integration Tests).
- [x] Đã kết nối và tự động khởi tạo 49 bảng trên CSDL Live Neon PostgreSQL.
- [x] Đã kiểm thử Swagger UI và gọi trực tiếp các API HTTP đạt mã 200 OK.

### 5. Ghi Chú Cho PM & Frontend
- Frontend (FE-19) kết nối Backend qua cổng 5232 hoặc base URL cấu hình trong .env.
- Toàn bộ dữ liệu doanh thu, thuế suất bóc tách 0.8% và 1.0% cùng 4 sổ kế toán đã được nạp sẵn dữ liệu mẫu thực tế trong Q3/2026.