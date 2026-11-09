# CHANGELOG - MT_GRMS_BACKEND

Toàn bộ các thay đổi của repository Backend được tổng hợp tại đây theo từng phiên bản. Chi tiết từng commit xem tại [`docs/worklogs/`](./docs/worklogs/).

---

## [Unreleased] - 2026-11-09

### Fixed
- Khắc phục lỗi `500 Internal Server Error` (`42P01: relation "BranchProducts" does not exist`) khi xem chi tiết chi nhánh: tự động khởi tạo bảng `"BranchProducts"` qua `EnsureBranchProductsSchemaAsync` trong `DbInitializer.cs`, bổ sung cơ chế fallback thống kê sản phẩm trực tiếp từ `BranchStocks` khi catalog rỗng, và phòng thủ try-catch cho nhân sự trong `BranchRepository.cs` [BE-BRANCH] [worklog: docs/worklogs/2026-11-09_BE-BRANCH_AI_fix-branch-detail-schema-fallback.md].

## [Unreleased] - 2026-11-08

### Added
- Tái cấu trúc mô hình 2 gói cước SaaS: `STANDARD` (99.000 đ/tháng) và `PRO` (249.000 đ/tháng), xóa bỏ gói miễn phí vĩnh viễn (0đ), áp dụng dùng thử 3 ngày Gói Pro full tính năng cho tài khoản mới [FE-09] [worklog: docs/worklogs/2026-11-08_FE-09_AI_subscription-tier-quota-redesign.md].
- Bổ sung 3 cột vào bảng `Tenants`: `ExtraBranches` (chi nhánh mua thêm), `HasStaffManagementAddon` (cờ mua thêm nhân viên cho gói thường), `SubscriptionStatus` (trạng thái 'TRIAL', 'ACTIVE', 'EXPIRED'), tự động đồng bộ schema DDL qua `DbInitializer.cs` [FE-09] [worklog: docs/worklogs/2026-11-08_FE-09_AI_subscription-tier-quota-redesign.md].
- Nâng cấp API tính tiền và gia hạn `POST /api/tenant/subscriptions/purchase`: hỗ trợ tính thêm chi nhánh cho Gói Pro (+80k/tháng), option quản lý nhân viên cho Gói Thường (+30k/tháng), tính VAT 10% và cộng dồn hạn mức chi nhánh [FE-09] [worklog: docs/worklogs/2026-11-08_FE-09_AI_subscription-tier-quota-redesign.md].
- Cập nhật hàm kiểm tra hạn mức `BranchService.GetBranchQuotaAsync`: tính quota động theo `Plan.MaxBranches + Tenant.ExtraBranches` [FE-09] [worklog: docs/worklogs/2026-11-08_FE-09_AI_subscription-tier-quota-redesign.md].
- Cập nhật API `GET /api/tenant/pricing-plans`: Lọc nghiêm ngặt và trả về 3 gói cước chuẩn: `TRIAL` (0 đ), `STANDARD` (99k) và `PRO` (249k), vô hiệu hóa các gói cũ từ DB và đảm bảo dữ liệu đồng nhất [FE-09] [worklog: docs/worklogs/2026-11-08_FE-09_AI_subscription-tier-quota-redesign.md].
- Bổ sung API `POST /api/tenant/subscriptions/trial` và xử lý dùng thử miễn phí 3 ngày Full tính năng Gói Pro khi đăng ký mới hoặc kích hoạt trải nghiệm [FE-09] [worklog: docs/worklogs/2026-11-08_FE-09_AI_subscription-tier-quota-redesign.md].
- **Khắc phục lỗi logic bypass Quota chi nhánh (`BranchService.cs`, `BranchRepository.cs`)**: Thay đổi cơ chế đếm hạn mức từ `CountActiveAsync` sang `CountTotalAsync` (đếm tất cả chi nhánh sở hữu `!IsDeleted`), triệt tiêu lỗ hổng tắt chi nhánh để tạo thêm chi nhánh không giới hạn; đồng thời bổ sung chốt chặn kiểm tra Quota khi kích hoạt lại chi nhánh tại `ToggleBranchStatusAsync` [FE-09] [worklog: docs/worklogs/2026-11-08_FE-09_AI_subscription-tier-quota-redesign.md].
- Bổ sung endpoint xóa chi nhánh `DELETE /api/branches/{id}` (Soft Delete) kết hợp bảo vệ chi nhánh chính `IsHeadquarter` không cho phép xóa, giúp giải phóng hạn mức chi nhánh đang sử dụng [FE-09] [worklog: docs/worklogs/2026-11-08_FE-09_AI_subscription-tier-quota-redesign.md].
- Bổ sung endpoint `GET /api/branches/{id}/detail` tổng hợp toàn diện thông tin cơ sở và các thông số hoạt động vận hành (SKU kinh doanh, tồn kho SOH, cảnh báo thiếu hàng, tổng nhân sự và đơn hàng) [FE-09] [worklog: docs/worklogs/2026-11-08_FE-09_AI_subscription-tier-quota-redesign.md].
- Tối ưu hóa `UpdateBranchAsync`: tự động đồng bộ gỡ cờ Trụ sở chính ở các chi nhánh khác khi người dùng thiết lập chi nhánh mới làm Trụ sở chính [FE-09] [worklog: docs/worklogs/2026-11-08_FE-09_AI_subscription-tier-quota-redesign.md].

## [Unreleased] - 2026-11-07

### Added
- Bổ sung 3 thuộc tính mới vào bảng `Branches`: `Email` (email thông báo), `ManagerName` (bỏ `[NotMapped]`, lưu chính thức vào DB), và `IsHeadquarter` (đánh dấu cơ sở chính), đồng thời tự động cập nhật DDL trong `DbInitializer.cs` và ánh xạ đầy đủ trong `BranchService.cs` [BE-BRANCH] [worklog: docs/worklogs/2026-11-07_BE-BRANCH_AI_fix-branch-requests-deserialization.md].

### Fixed
- Sửa lỗi `400 Bad Request` ("The JSON value could not be converted to MT_GRMS.Application.Branches.Dtos.UpdateBranchRequest. Path: $.name") khi gọi API Tạo mới chi nhánh (`POST /api/branches`) hoặc Cập nhật chi nhánh (`PUT /api/branches/{id}`): chuyển đổi `CreateBranchRequest`, `UpdateBranchRequest` và `UpdateBranchStatusRequest` từ C# positional `record` sang POCO `class` có constructor mặc định và các getter/setter an toàn, giúp `System.Text.Json` model binding chuẩn xác dữ liệu JSON gửi từ Frontend React [BE-BRANCH] [worklog: docs/worklogs/2026-11-07_BE-BRANCH_AI_fix-branch-requests-deserialization.md].

## [Unreleased] - 2026-11-06

### Fixed
- Khắc phục lỗi lặp liên tục `42P01: relation "OutboxMessages" does not exist` từ worker `SupplierOrderDispatchWorker`: chuẩn hóa thứ tự thực thi câu lệnh DDL trong `DbInitializer.cs` bằng cách đưa các lệnh `CREATE TABLE IF NOT EXISTS` cho 4 bảng (`OutboxMessages`, `SupplierDispatchPackages`, `SupplierDispatchPackageItems`, `PurchaseOrderRevisions`) lên trước các lệnh `ALTER TABLE`, loại bỏ xung đột phụ thuộc bảng khi khởi động [BUG-DB] [worklog: docs/worklogs/2026-11-07_BUG-DB_AI_fix-outboxmessages-ddl-order.md].
- Khắc phục lỗi `42P01: relation "BranchAttendancePolicies" does not exist` và `relation "ShiftConfigs" does not exist` khi khởi động Backend sau khi merge code phân hệ Nhân sự (HR): tự động khởi tạo an toàn và lũy đẳng (`CREATE TABLE IF NOT EXISTS`) cho toàn bộ 6 bảng HR (`BranchAttendancePolicies`, `ShiftConfigs`, `Employees`, `EmployeeAllowances`, `EmployeeSchedules`, `Payslips`, `CashbookTransaction`) và cập nhật các cột mới còn thiếu trong `DbInitializer.cs` [BUG-DB] [worklog: docs/worklogs/2026-11-06_BUG-DB_AI_fix-missing-hr-attendance-tables.md].
- Khắc phục lỗi `fail: Microsoft.EntityFrameworkCore.Database.Command[20102]` khi khởi động Backend: tự động phát hiện các cột PIN bảo mật đã tồn tại trong bảng `Users` và đồng bộ vào bảng lịch sử `__EFMigrationsHistory`, ngăn chặn hoàn toàn việc chạy lại lệnh `ALTER TABLE "Users" ADD "FailedPinAttempts"` gây trùng lặp cột [BUG-DB] [worklog: docs/worklogs/2026-11-06_BUG-DB_AI_fix-duplicate-column-migration-error.md].
- Khắc phục toàn bộ các cảnh báo Model Validation `warn: Microsoft.EntityFrameworkCore.Model.Validation[10622]` (QueryFilter tương tác với Required Navigation) và `[20601]` (Sentinel Value cho enum mặc định `CustomerDebtStatus` và `SupplierStatus`), làm sạch 100% console log khi khởi động ứng dụng [BUG-DB] [worklog: docs/worklogs/2026-11-06_BUG-DB_AI_fix-duplicate-column-migration-error.md].

### Changed
- Cải thiện API cập nhật thông tin gian hàng `PUT /api/tenant/store-info`: hỗ trợ cơ chế fallback `X-Tenant-Id` header và an toàn null-safety khi lưu tên chuỗi cửa hàng, bảo vệ toàn vẹn dữ liệu khách thuê [FE-STORE] [worklog: docs/worklogs/2026-11-06_FE-STORE_AI_tenant-store-update-enhancement.md].
- Bổ sung API `GET /api/tenant/subscriptions/history` và DTO `SubscriptionOrderHistoryDto`: trả về danh sách lịch sử hợp đồng và giao dịch gói dịch vụ của gian hàng (kết hợp các giao dịch mua/gia hạn từ `WalletTransactions` và gói khởi tạo ban đầu), chấm dứt tình trạng frontend phải dùng dữ liệu giả định [FE-STORE] [worklog: docs/worklogs/2026-11-06_FE-STORE_AI_tenant-store-update-enhancement.md].

### Security
- Chuẩn hóa an toàn bảo mật cấu hình: di chuyển toàn bộ credentials (ConnectionStrings, JWT Key, Cloudinary Secrets, SePay ApiKey, Gmail SMTP) từ `appsettings.json` sang `.env` (được bảo vệ bởi `.gitignore`), ngăn chặn triệt để nguy cơ rò rỉ secret key khi push mã nguồn lên Git [BE-CONFIG] [worklog: docs/worklogs/2026-11-06_FE-AUTH_AI_forgot-password-email-otp.md].

### Added
- Chuẩn hóa đồng bộ duy nhất một tiền tố route `/api` (loại bỏ hoàn toàn `/v1`) trên toàn bộ 24 Controller trong hệ thống (`TenantStore`, `Branches`, `Wallet`, `Inventory`, `Categories`, `Admin...`), làm sạch và triệt tiêu trùng lặp endpoint trên Swagger UI [BE-API] [worklog: docs/worklogs/2026-11-06_FE-AUTH_AI_forgot-password-email-otp.md].
- Triển khai luồng khôi phục mật khẩu hoàn chỉnh qua Email OTP: bổ sung các API `POST /api/auth/forgot-password`, `POST /api/auth/verify-otp`, `POST /api/auth/reset-password`, dịch vụ `IEmailService` gửi email HTML SMTP (kèm Dev Console Logger fallback), sinh OTP ngẫu nhiên 6 chữ số với thời hạn 10 phút, và chuẩn hóa tiền tố route toàn hệ thống về `/api` (loại bỏ `/v1`) [FE-AUTH] [worklog: docs/worklogs/2026-11-06_FE-AUTH_AI_forgot-password-email-otp.md].

### Refactored
- Tái cấu trúc hợp nhất `DatabaseSeeder.cs` vào `DbInitializer.cs`: di chuyển toàn bộ dữ liệu mẫu Thuế HKD (NQ 204), doanh thu mẫu Q3 (385 triệu VNĐ), sổ quỹ (`FinanceAccounts`: Till, Safe, Bank), công nợ khách hàng FIFO và ma trận nhà cung cấp (PO Matrix) vào các hàm mô-đun trong `DbInitializer.cs`. Xóa bỏ hoàn toàn tệp `DatabaseSeeder.cs` và tinh gọn `Program.cs` chỉ gọi một điểm khởi tạo duy nhất `await DbInitializer.InitializeAsync(dbContext, logger);` [BE-DB] [worklog: docs/worklogs/2026-11-06_BE-DB_AI_consolidate-database-seeders.md].

## [Unreleased] - 2026-11-05

### Added
- Triển khai API đăng ký tài khoản chủ cửa hàng nguyên tử `POST /api/v1/auth/register` (hỗ trợ cả `/api/auth/register`), tự động tạo `Tenant`, `User` (Role `STORE_OWNER`), `TenantUser` và `Branch` mặc định trong một Database Transaction duy nhất [BE-AUTH] [worklog: docs/worklogs/2026-11-05_BE-AUTH_AI_tenant-registration-and-modular-seeder.md].
- Cơ chế nạp dữ liệu mẫu dạng mô-đun lũy đẳng (Modular Idempotent Seeder) trong `DbInitializer.cs`: kiểm tra độc lập từng bảng, loại bỏ early return, sửa lỗi trùng lặp số điện thoại `IX_Users_Phone` và băm mật khẩu mẫu bằng BCrypt [BE-DB].
- Cấu hình Kestrel tự động nhận biến môi trường `$PORT` trên Render/Cloud, kích hoạt Swagger UI trong môi trường Production và tự động redirect trang chủ `/` sang `/swagger` trên cùng 1 cổng duy nhất [BE-DEPLOY] [worklog: docs/worklogs/2026-11-05_BE-DEPLOY_AI_render-swagger-single-port.md].
- Cơ chế khởi tạo CSDL tự động chống sập trên Cloud: Tự động phát hiện cơ sở dữ liệu mới và khởi tạo toàn bộ 34 bảng trực tiếp từ DbContext model, khắc phục triệt để lỗi `relation "Users" does not exist` khi chạy trên database trống [BE-DEPLOY].

## [Unreleased] - 2026-11-04

### Added
- Tích hợp cổng thanh toán SePay và Ví gian hàng: thêm thực thể `WalletTransaction`, `WalletTopupOrder`, cột `Tenants.WalletBalance`, API nạp ví VietQR động, Webhook SePay Idempotent và khấu trừ ví khi mua gói cước [FE-WALLET] [worklog: docs/worklogs/2026-11-04_FE-WALLET_AI_sepay-wallet-integration.md].

### Changed
- Cấu hình explicit bridge network mtgrms_network và PGWEB_DATABASE_URL trong docker-compose.yml, triệt tiêu hoàn toàn lỗi crash-loop DNS lookup postgres trên container mtgrms-pgweb (cổng 8081) [BE-DOCKER] [worklog: docs/worklogs/2026-11-04_BE-DOCKER_AI_fix-pgweb-network-crashloop.md].
- Cấu hình `.gitignore` bỏ qua toàn bộ thư mục `**/Migrations/`, `AppDbContextModelSnapshot.cs` và untrack các migration cũ khỏi Git nhằm triệt tiêu hoàn toàn lỗi merge conflicts khi hợp nhất mã nguồn giữa các nhánh [BE-CONFIG] [worklog: docs/worklogs/2026-11-03_BE-CONFIG_AI_ignore-migrations-to-prevent-merge-conflicts.md].
- Nâng cấp `DbInitializer.cs` với cơ chế fallback tự động `EnsureCreatedAsync()`, đảm bảo khởi tạo CSDL 34 bảng và dữ liệu mẫu an toàn kể cả khi repository không còn track migrations.

## [0.1.1] - 2026-10-27

### Added

- Chuẩn hóa toàn bộ 34 Domain Entities và EF Core DbContext/Configurations mapping khớp 100% CSDL 34 bảng (7 phân hệ chuẩn 3NF) từ `MT_GRMS_Full_Database.sql` [FE-ENTITIES].
- Tách biệt `BaseEntity` (Global SaaS Root) và `BaseTenantEntity` (Tenant-scoped Multi-tenancy Isolation) [FE-ENTITIES].
- Tích hợp bộ sinh `Uuid7` chuẩn RFC 9562 tuần tự theo thời gian thay thế UUIDv4 ngẫu nhiên nhằm triệt tiêu phân mảnh chỉ mục B+Tree (Page Splits) [FE-ENTITIES].
- Chuẩn hóa HTTP Status Codes: 200 OK, 400 Bad Request, 401 Unauthorized đóng gói qua `ApiResponse<T>` [BE-AUTH].
- Loại bỏ hoàn toàn fallback mock ngầm, xác thực nghiêm ngặt 100% dựa trên database PostgreSQL [BE-AUTH].
- Hỗ trợ route kép `[Route("api/v1/[controller]")]` và `[Route("api/[controller]")]` trên `AuthController` tương thích frontend [BE-AUTH].
- Bổ sung xác thực mật khẩu tài khoản trong `AuthService` và mã lỗi 401 qua `ExceptionHandlingMiddleware` [BE-AUTH].
- Mở rộng `UserInfo` record với thuộc tính ánh xạ `Role => RoleName` [BE-AUTH].
- Bổ sung bộ kiểm thử đơn vị `UserAuthenticationTests` (10/10 tests PASS) [BE-AUTH].
- Triển khai hạ tầng PostgreSQL 16 qua Docker Compose (`docker-compose.yml`, `init-db.sql`).
- Tối giản hóa thực thể và bảng `users` (`Id` string Uid, `Username`, `Password`) phục vụ đăng nhập độc lập.
- Khởi tạo EF Core Initial Migration `InitialCreate_UsersOnly` và seed data `admin` [BE-AUTH].
- Triển khai endpoints `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/logout` [BE-AUTH].
- Cung cấp `IAuthService` và `AuthService` xử lý xác thực tenant và sinh JWT token / Refresh Token.

## [0.1.0] - 2026-09-30

### Added

- Khung mẫu cấu hình Code-First Fluent API: `ProductConfiguration.cs`.
- Mẫu DTOs `ProductCreateRequest`, `ProductResponse`.
- Mẫu Service Interface `ICatalogService` & Service `CatalogService`.
- Controller mẫu mỏng `ProductsController` chỉ giao tiếp bằng DTO.
- Thư mục quản lý worklog nội bộ: `docs/worklogs/`.

### Fixed
- Khắc phục lỗi lặp liên tục `42P01: relation "OutboxMessages" does not exist` từ worker `SupplierOrderDispatchWorker`: chuẩn hóa thứ tự thực thi câu lệnh DDL trong `DbInitializer.cs` bằng cách đưa các lệnh `CREATE TABLE IF NOT EXISTS` cho 4 bảng (`OutboxMessages`, `SupplierDispatchPackages`, `SupplierDispatchPackageItems`, `PurchaseOrderRevisions`) lên trước các lệnh `ALTER TABLE`, loại bỏ xung đột phụ thuộc bảng khi khởi động [BUG-DB] [worklog: docs/worklogs/2026-11-07_BUG-DB_AI_fix-outboxmessages-ddl-order.md].

- Lỗ hổng cô lập người thuê đa tầng (Multi-Tenant Isolation) trong `AppDbContext.cs`: chuyển sang đánh giá động runtime trong `HasQueryFilter`.
- Biên dịch 0 Warning, 0 Error.
