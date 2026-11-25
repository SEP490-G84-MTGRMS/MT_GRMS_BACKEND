# CHANGELOG - MT_GRMS_BACKEND

Toàn bộ các thay đổi của repository Backend được tổng hợp tại đây theo từng phiên bản. Chi tiết từng commit xem tại [`docs/worklogs/`](./docs/worklogs/).

---

## [Unreleased] - 2026-11-15

### Changed
- Bổ sung `[Authorize(Roles = RoleConstants.StoreOwnerOrAdmin)]` cho `DELETE /api/branches/{id}` và `GET /api/tenant/subscriptions/history`; chuẩn hóa phản hồi trung tính `200 OK` cho `POST /api/Auth/forgot-password` nhằm chống dò tài khoản (OWASP). [Worklog](./docs/worklogs/2026-11-24_QA-NHAT_AI_fix-rbac-and-auth-enumeration.md)
- Đồng bộ kiểm tra dữ liệu nhập admin cho cửa hàng, gói dịch vụ, hàng mẫu, Excel và cấu hình nền tảng. [Worklog](./docs/worklogs/2026-11-23_ADMIN-VALIDATION_Codex_admin-input-boundaries.md)
- Áp dụng quyền Quản lý nhân viên theo gói cho API ca làm, chấm công, lịch làm, duyệt yêu cầu và bảng lương.
- Yêu cầu tên chi nhánh có ít nhất 2 ký tự sau khi bỏ khoảng trắng hai đầu khi thêm mới hoặc cập nhật chi nhánh.
- Giữ đúng mã gian hàng cho chi nhánh chính và danh mục mẫu khi đăng ký trong phiên của một gian hàng khác.
- Tách truy vấn tải nhiều danh sách và giảm dữ liệu, số lần duyệt trong truy vấn tồn kho.
- Đồng bộ cột cấu hình chi nhánh và bảng thông báo khi chạy API bằng profile local.
- Xử lý cảnh báo nullable ở email, OTP và mã vạch.
- Xử lý cảnh báo nullable và async trong các luồng ca làm, phiếu lương, nhập hàng và in hóa đơn.
- EF Core ghi trực tiếp giá trị enum của phiếu chuyển kho, yêu cầu chuyển kho và bản chốt thuế khi tạo bản ghi.
- Thêm lệnh Development để dựng lại schema và nạp dữ liệu mẫu vào PostgreSQL local `mtgrms_demo` trong cùng một transaction.
- Resolve the `dev` merge in `DbInitializer.cs` by keeping the wallet branch's scenario seeder and incorporating `dev` schema updates. [worklog: docs/worklogs/2026-11-17_WALLET-MERGE_AI_resolve-initializer.md]
- Đồng bộ email Admin và chủ cửa hàng mẫu sang miền mtgrms.vn. [worklog: docs/worklogs/2026-11-16_DEMO-SEED_AI_scenario-fixtures.md]
- Tập trung seed trực tiếp trong DbInitializer.cs, thay seed demo cũ bằng bộ 8 kịch bản SaaS có owner, nhân viên, chi nhánh và dữ liệu vận hành, nạp idempotent không ghi đè dữ liệu cũ. Thêm lệnh Development `--seed-demo-only true`. [worklog: docs/worklogs/2026-11-16_DEMO-SEED_AI_scenario-fixtures.md]
- Dashboard Admin bỏ dữ liệu mẫu, thêm tổng hợp mua gói và điểm bán theo kỳ UTC+7, sửa trạng thái cảnh báo và cách đếm giao dịch mua gói. [worklog: docs/worklogs/2026-11-16_ADMIN-DASHBOARD_AI_real-data.md]
- Hợp nhất yêu cầu nạp VietQR và giao dịch ví vào `WalletTransactions`. Giữ hợp đồng API, chuyển dữ liệu đơn cũ qua đối soát và ngăn webhook cộng trùng số dư. [worklog: docs/worklogs/2026-11-16_WALLET_AI_merge-topup-orders.md]

### Added
- Cho chủ Gói Thường mua riêng tiện ích Quản lý nhân viên bằng Ví gian hàng, tính phí theo số ngày còn lại và kiểm tra quyền dùng ở API hồ sơ nhân viên. [worklog: docs/worklogs/2026-11-22_BE-STORE_AI_staff-management-addon.md]
- API Admin xem lịch sử mua gói dịch vụ theo tenant, phân trang và giới hạn truy cập Admin. Chi tiết tenant không trả danh sách chi nhánh. [worklog: docs/worklogs/2026-11-16_ADMIN-TENANT_AI_tenant-records.md]
- Khởi tạo danh mục sản phẩm, nhóm hàng, đơn vị quy đổi và gán chi nhánh chính từ MasterProducts đang bật trong transaction đăng ký cửa hàng. [worklog: docs/worklogs/2026-11-15_ADMIN-CATALOG_AI_new-tenant-default-data.md]
- **Kho Danh mục Hàng hóa Mẫu Toàn Cầu (Global FMCG Master Catalog)**: Xây dựng thực thể nền tảng `MasterProduct` & `MasterProductUnit`, bộ API quản trị cho Super Admin `AdminMasterCatalogController` (`/api/admin/master-catalog`), xử lý ClosedXML xuất template và import Excel hàng loạt (>5.000 SKU) cùng API tra cứu mã vạch công khai `GET /api/products/lookup-master` phục vụ auto-fill tức thì khi tiệm tạp hóa onboard [REQ-MC-01 -> REQ-MC-04] [worklog: docs/worklogs/2026-11-15_FMCG-MASTER-CATALOG_Ortus24_add-global-master-catalog.md].
- API Admin `GET /api/admin/audit-logs` đọc nhật ký thật, lọc hành động/từ khóa/thời gian Việt Nam, phân trang và che trường bí mật trong dữ liệu trước/sau. [worklog: docs/worklogs/2026-11-15_ADMIN-AUDIT_AI_audit-log-query.md]
- **Cấu hình Nền tảng Toàn cục SaaS (Global Platform Settings)**: Xây dựng thực thể Singleton `PlatformSetting` và bộ APIs quản trị `AdminSettingsController` (`GET/PUT /api/v1/admin/settings`) cùng API công khai `PlatformPublicConfigController` (`GET /api/v1/platform/public-config`). Thay thế triệt để các giá trị cấu hình hardcode trong `TenantBranchAddonController` (đơn giá chi nhánh 80k), `TenantStorageController` (đơn giá dung lượng 15k), `WalletService` (tài khoản Master VietQR) và `AuthService` (số ngày dùng thử 3 ngày). Hỗ trợ Bật/Tắt chế độ bảo trì toàn hệ thống [REQ-FE-ADMIN-05] [worklog: docs/worklogs/2026-11-15_REQ-FE-ADMIN-05_AI_global-platform-settings-integration.md].
- API Admin `POST /api/admin/tenants` tạo cửa hàng, tài khoản chủ sở hữu và chi nhánh chính trong một transaction, kiểm tra dữ liệu trùng và ghi audit log. Cửa hàng khởi tạo ở trạng thái `NO_PLAN`. [worklog: docs/worklogs/2026-11-15_ADMIN-TENANT_AI_create-tenant.md]

### Fixed
- Khắc phục lỗi biên dịch `CS0234` thiếu migration `AddBranchProductAllowedSupplierIds` và lỗi `500 (42703: column p.ImageUrlsJson does not exist)` trên `/store-owner/dashboard` bằng cách đồng bộ toàn bộ `UpSql` mới và các cột còn thiếu trong `ApplyLocalSchemaRepairsAsync` khi chạy API bằng profile local. [worklog: docs/worklogs/2026-11-25_FIX-BUILD_AI_add-missing-branch-product-allowed-supplier-migration.md]
- Giữ chi nhánh duy nhất làm trụ sở chính và chặn thao tác ngừng hoạt động hoặc xóa chi nhánh đó. [worklog: docs/worklogs/2026-11-23_BE-BRANCH_AI_protect-only-branch.md]
- Bổ sung cột `OrderDetails.StockAllocationsJson` khi chạy API bằng profile local để khắc phục lỗi tải Tổng quan trên cơ sở dữ liệu chưa cập nhật schema.
- Áp dụng bổ sung schema `ExpenseVouchers.DueDate` khi chạy API bằng profile local để worker nhắc thanh toán đọc được phiếu chi trên cơ sở dữ liệu cũ.
- Kiểm tra tên, số điện thoại, mã số thuế, tỉnh/thành phố và địa chỉ trước khi lưu thông tin gian hàng. [worklog: docs/worklogs/2026-11-22_BE-STORE_AI_store-info-validation.md]
- Không áp điều kiện mua gói tenant cho Admin đã xác thực, sửa API quản trị trả 403 sau reset dữ liệu. [worklog: docs/worklogs/2026-11-16_ADMIN-AUTH_AI_subscription-bypass.md]
- Bổ sung CreatedAt và IsDeleted trong khởi tạo PlatformSettings để tránh lỗi PostgreSQL 23502 khi chạy backend. [worklog: docs/worklogs/2026-11-16_PLATFORM-SETTINGS_AI_fix-startup-seed.md]
- **Triệt tiêu toàn bộ cảnh báo EF Core Model Validation ([20601] & [10620]) & Query First Without OrderBy ([10103])**: Cấu hình `.HasSentinel(...)` cho các thuộc tính Enum có database default (`Product.Status`, `Employee.SalaryType`, `Employee.Status`, `Payslip.SalaryType`, `Payslip.Status`, `ScheduleApprovalRequest.Status`, `Transfer.Status`) và bổ sung `ValueComparer<List<string>>` cho `ScheduleApprovalRequest.EvidenceImageUrls`. Bổ sung điều kiện deterministic ordering `.OrderBy(x => x.Id)` và filter tenant cho các truy vấn `FirstOrDefaultAsync` trong `DbInitializer.cs`, `CashbookRepository.cs`, `PurchaseOrderRepository.cs`, `ShiftRepository.cs`, `TaxExportService.cs`, `WalletService.cs` và `OrderRepository.cs` [FIX-WARN] [worklog: docs/worklogs/2026-11-15_FIX-WARN_AI_fix-efcore-validation-warnings.md].

## [Unreleased] - 2026-11-14

### Added
- API `GET /api/store-owner/dashboard` tổng hợp số liệu thật về doanh số, P&L, nhập hàng, kho và nhân viên theo tenant/chi nhánh; kiểm tra role StoreOwner. [worklog: docs/worklogs/2026-11-14_FE10_AI_store-owner-dashboard-real-data.md]

### Changed
- Đồng bộ email Admin và chủ cửa hàng mẫu sang miền mtgrms.vn. [worklog: docs/worklogs/2026-11-16_DEMO-SEED_AI_scenario-fixtures.md]
- Dashboard Chủ cửa hàng hiển thị doanh thu theo 24 giờ Việt Nam khi chọn Hôm nay/Hôm qua; các kỳ dài hơn tiếp tục theo ngày. [worklog: docs/worklogs/2026-11-14_FE10_AI_store-owner-dashboard-real-data.md]
- Thu gọn luồng access/refresh dùng các trường hiện có trên Users, access mặc định 15 phút và refresh cố định 7 ngày. Lưu hash token mới, đồng bộ hạn cookie và hỗ trợ logout khi access hết hạn. [worklog: docs/worklogs/2026-11-14_AUTH-REFRESH_AI_user-token-flow.md]

### Added
- **Đồng bộ trường `IsTwoFactorEnabled` trong `UserInfo` DTO**: Bổ sung `IsTwoFactorEnabled` vào `UserInfo` record và gán từ entity `User.IsTwoFactorEnabled` tại các luồng xác thực (`Login`, `VerifyLoginOtp`, `GetMe`, `Register`), giúp Frontend và thanh điều hướng TopNav nhận diện chính xác trạng thái xác thực 2 bước của tài khoản thời gian thực [FEAT-AUTH] [worklog: docs/worklogs/2026-11-14_FEAT-AUTH_AI_sync-two-factor-auth-status-in-user-info.md].

## [Unreleased] - 2026-11-13

### Changed
- Đồng bộ email Admin và chủ cửa hàng mẫu sang miền mtgrms.vn. [worklog: docs/worklogs/2026-11-16_DEMO-SEED_AI_scenario-fixtures.md]
- **Mặc định tắt xác thực 2 bước (2FA OTP Email) khi tạo tài khoản**: Chuyển cờ `User.IsTwoFactorEnabled` mặc định thành `false` cho toàn bộ tài khoản mới đăng ký/tạo mới, giúp người dùng đăng nhập ngay sau mật khẩu mà không bị bắt buộc xác thực 2FA OTP qua email [BE-AUTH] [worklog: docs/worklogs/2026-11-13_BE-AUTH_AI_default-disable-two-factor-auth-on-registration.md].
- **Cập nhật nhận diện thương hiệu THViet cho Email OTP & Cấu hình**: Chuyển đổi tên thương hiệu hệ thống sang `THViet - QUẢN LÝ TẠP HÓA VIỆT`, cập nhật tiêu đề email `[THViet]`, Header logo thương hiệu, SenderName (`.env`, `EmailSettings.cs`, `appsettings.json`) và phóng to mã OTP lên 38px nổi bật trên nền xanh thương hiệu `#0F766E` [BRAND-THVIET] [worklog: docs/worklogs/2026-11-13_BRAND-THVIET_AI_rebrand-email-and-settings.md].
- **Giao diện Email OTP tối giản phong cách hiện đại (Minimalist NVIDIA Style)**: Tối ưu hóa HTML template trong `EmailService.cs`, chuyển sang layout nền trắng phẳng, căn lề trái, hiển thị mã OTP dạng badge thanh lịch và chân trang tinh gọn [AUTH-OTP] [worklog: docs/worklogs/2026-11-13_AUTH-OTP_AI_minimalist-email-template.md].

### Fixed
- Không áp điều kiện mua gói tenant cho Admin đã xác thực, sửa API quản trị trả 403 sau reset dữ liệu. [worklog: docs/worklogs/2026-11-16_ADMIN-AUTH_AI_subscription-bypass.md]
- **Sửa lỗi upload/xóa ảnh trả 500 trên PostgreSQL**: Chuẩn hóa câu lệnh khóa bản ghi tenant trong `MediaController` sử dụng đúng khóa chính vật lý `Tenants.TenantID` thay vì cột không tồn tại `Id`; bổ sung unit test xác nhận ánh xạ khóa tenant [MEDIA-UPLOAD] [worklog: docs/worklogs/2026-11-14_MEDIA-UPLOAD_AI_fix-tenant-lock-column.md].
- **Gửi Email OTP chạy nền không chặn luồng (Non-blocking Background Dispatch)**: Chuyển tác vụ gửi thư qua giao thức SMTP trong `EmailService.cs` sang background task (`Task.Run`), giúp API phản hồi tức thì (< 15ms) khi thông tin đăng nhập/đăng ký hợp lệ, popup modal OTP hiển thị ngay lập tức không bị chờ độ trễ mạng SMTP [AUTH-OTP] [worklog: docs/worklogs/2026-11-13_AUTH-OTP_AI_non-blocking-background-smtp-dispatch.md].
- **Khắc phục hiện tượng đơ lag khi gửi mã OTP Email (`EmailService.cs`)**: Tinh gọn nhánh xử lý fallback dev của `EmailService.cs`, phản hồi tức thì (< 100ms) và in mã OTP ra console khi cấu hình SMTP chưa hoàn tất ở môi trường local/dev, loại bỏ triệt để việc treo luồng mạng [AUTH-OTP] [worklog: docs/worklogs/2026-11-13_AUTH-OTP_AI_fix-email-otp-lag-fallback.md].

## [Unreleased] - 2026-11-12

### Added
- Tách đăng ký thường thành `NO_PLAN` và CTA HomePage thành Trial 72 giờ; bổ sung dấu Trial/paid bền vững, API trạng thái/kích hoạt Trial (`AuditLog` `TRIAL_ACTIVATED` + conditional update), `GET /api/tenant/subscriptions/access-status` cho mọi thành viên active của tenant, chặn hạ cấp từ `PRO` xuống `STANDARD` (`DOWNGRADE_NOT_ALLOWED`), tenant membership validation và backend subscription gate chặn cả `NO_PLAN`, Trial hết hạn và gói trả phí đã hết hạn (`SubscriptionExpiresAt <= UtcNow`) [HOMEPAGE-TRIAL] [worklog: docs/worklogs/2026-11-12_HOMEPAGE-TRIAL_AI_trial-onboarding.md].
- Bổ sung `TrialOnboardingBackfillService` (dry-run & backfill deterministic) và migration cho trạng thái Trial, paid và dữ liệu legacy `REVIEW_REQUIRED`; sửa `DbInitializer` không gán nhầm `CurrentPlan = "PRO"` cho tenant `NO_PLAN` và `TenantService` luôn trả `"Chưa kích hoạt"` cho tenant `NO_PLAN`; chuẩn hóa email/điện thoại cho account [HOMEPAGE-TRIAL].

### Security
- Chuẩn hóa validation authentication: giới hạn payload đăng nhập, password đăng ký mạnh 8–64 ký tự, email/phone/tên/OTP, bắt buộc đồng ý điều khoản và chặn malformed OTP trước khi tiêu hao lượt thử [AUTH-VALIDATION] [worklog: docs/worklogs/2026-11-13_AUTH-VALIDATION_AI_auth-input-validation.md].
- Bắt buộc Email OTP distributed (`Pending/Reserved/Consumed`) gắn `NormalizedEmail`, intent và request fingerprint; fail-closed ngoài Development; `IdempotencyFilter` scoped theo actor + operation + tenant/challenge trả RFC 7807 `ProblemDetails`; cấu hình `ForwardedHeadersOptions` trusted proxy và củng cố chống replay webhook SePay [HOMEPAGE-TRIAL].

- Chuẩn hóa hạn mức lưu trữ 2 GB cho gói STANDARD/PRO; bổ sung mua thêm dung lượng với giá 15.000 đ/GB/tháng, báo giá theo thời gian còn lại, VAT và thanh toán ví có idempotency [SUBSCRIPTION-STORAGE] [worklog: docs/worklogs/2026-11-12_SUBSCRIPTION-STORAGE_AI_storage-quota-addon.md].
- Thêm luồng mua chi nhánh riêng cho Gói PRO, báo giá theo thời gian còn lại và trừ Ví gian hàng mà không gia hạn hay tính lại tiền gói [SUBSCRIPTION-BRANCH].
- Theo dõi dung lượng ảnh theo tenant, chặn upload vượt hạn mức và hoàn trả quota khi xóa media [SUBSCRIPTION-STORAGE].

## [Unreleased] - 2026-11-11

### Fixed
- Không áp điều kiện mua gói tenant cho Admin đã xác thực, sửa API quản trị trả 403 sau reset dữ liệu. [worklog: docs/worklogs/2026-11-16_ADMIN-AUTH_AI_subscription-bypass.md]
- Chặn `POST /api/auth/login` cấp phiên bỏ qua Email OTP; giới hạn chờ SMTP 5 giây và không trả/log mã fallback ở production [BE-AUTH] [worklog: docs/worklogs/2026-11-11_BE-AUTH_AI_close-login-otp-bypass-and-smtp-timeout.md].
- Khắc phục lỗi `DUPLICATE_KEY` (`23505: duplicate key value violates unique constraint "IX_Branches_TenantID_BranchCode"`) khi xác nhận OTP đăng ký mở cửa hàng mới: tinh chỉnh `AppDbContext.ApplyAuditAndTenantInfo` không ghi đè `TenantId` của chi nhánh khi thực thể đã mang `TenantId` của gian hàng mới được khởi tạo; ngăn `MultiTenantMiddleware` gán ép fallback tenant `tenant_mt_mart_01` cho các request đăng ký `/api/auth/register`; bổ sung `IgnoreQueryFilters()` và kiểm tra đồng bộ cả `UserName`, `Phone`, `Email` trong `UserRepository.ExistsByPhoneOrEmailAsync` [BE-AUTH] [worklog: docs/worklogs/2026-11-11_BE-AUTH_AI_fix-tenant-registration-branch-unique-constraint.md].

### Added
- Thêm cờ `Users.IsTwoFactorEnabled` mặc định bật cho tài khoản hiện có, phân nhánh đăng nhập theo cờ và API Settings xác nhận bật/tắt qua Email OTP [BE-AUTH] [worklog: docs/worklogs/2026-11-11_BE-AUTH_AI_close-login-otp-bypass-and-smtp-timeout.md].
- Chuyển đổi toàn diện xác thực OTP sang **Email OTP** cho cả luồng **Đăng nhập 2 lớp (2FA)** (`POST /api/auth/login/send-otp`, `POST /api/auth/login/verify-otp`) và **Đăng ký mở cửa hàng** (`POST /api/auth/register/send-otp`, `POST /api/auth/register`), tích hợp bảo vệ chống brute-force (tối đa 5 lần nhập sai, TTL 5 phút) và cơ chế Dev Console Fallback; gỡ bỏ hoàn toàn TextBee SMS Gateway (`ISmsService`, `TextBeeSmsService`, `TextBeeSettings`) [EMAIL-OTP] [worklog: docs/worklogs/2026-11-11_EMAIL-OTP_AI_email-otp-auth-flow.md].

## [Unreleased] - 2026-11-09

### Added
- Tinh gọn và tái cấu trúc hàm `LoginAsync` trong `AuthService.cs`: rút ngắn từ 140 dòng xuống 40 dòng, loại bỏ triệt để hơn 30 dòng `if...else` soi chuỗi username/email tạm bợ, tách thành 3 phương thức chuyên trách `VerifyPasswordAsync`, `ResolveTenant`, và `ResolveUserRole` dựa trên Enum `UserRole` [FE-AUTH] [worklog: docs/worklogs/2026-11-09_FE-AUTH_AI_login-refactor-clean-code.md].
- Chuẩn hóa hệ thống phân quyền với enum `UserRole` gồm 3 giá trị: `Admin`, `StoreOwner`, `Staff`; bổ sung helper `UserRoleNames` và các thuộc tính `Role` / `UserRole` trên `User`, `TenantUser`, `UserInfo` DTO; loại bỏ triệt để nguy cơ fallback sai về quyền `ADMIN` trong `AuthService` [FE-AUTH] [worklog: docs/worklogs/2026-11-09_FE-AUTH_AI_user-role-enum-standardization.md].
- Chuyển đổi toàn diện cơ chế xác thực sang HttpOnly Cookie: AccessToken và RefreshToken được lưu trong HttpOnly Cookie (chống XSS), loại bỏ token nhạy cảm khỏi response body, triển khai Token Rotation trong CSDL PostgreSQL bảng Users, bổ sung endpoint `GET /api/auth/me` và `POST /api/auth/refresh` đọc Cookie [FE-AUTH] [worklog: docs/worklogs/2026-11-09_FE-AUTH_AI_httponly-cookie-and-session-expiry.md].

### Fixed
- Không áp điều kiện mua gói tenant cho Admin đã xác thực, sửa API quản trị trả 403 sau reset dữ liệu. [worklog: docs/worklogs/2026-11-16_ADMIN-AUTH_AI_subscription-bypass.md]
- Khắc phục lỗi DB khi khởi động Backend sau merge (`column "DebtStatus"/"FaceDescriptor" does not exist`, `relation "ShiftZReports" does not exist`, duplicate key khi seed): chạy script tạo schema theo từng câu lệnh, tự bổ sung cột thiếu theo EF model (`SyncMissingColumnsAsync`), seed dùng `IgnoreQueryFilters()` để không trùng bản ghi soft-delete, sửa thêm phụ cấp nhân viên bằng `DbSet` [BUG-DB] [worklog: docs/worklogs/2026-11-10_BUG-DB_AI_fix-schema-drift-and-seed-after-merge.md].
- Sửa lỗi tạo tài khoản chủ cửa hàng mới (`RegisterTenantAsync` trong `AuthService.cs`) và kích hoạt dùng thử (`TenantStoreController.cs`, `BranchService.cs`) tự động gán `CurrentPlan = "PRO"` thay vì `"TRIAL"`: chuyển về `CurrentPlan = "TRIAL"` (Gói Dùng Thử 3 ngày, hạn mức 1 chi nhánh), đồng thời lưu `RefreshToken` vào CSDL ngay khi đăng ký để tránh lỗi `401 Unauthorized` khi làm mới phiên qua HttpOnly Cookie [FE-AUTH] [worklog: docs/worklogs/2026-11-09_FE-AUTH_AI_login-refactor-clean-code.md].
- Khắc phục lỗi `InvalidOperationException` (NpgsqlRetryingExecutionStrategy không hỗ trợ user-initiated transaction) khi xử lý Webhook SePay nạp tiền ví trong `WalletService.cs`: bọc toàn bộ khối transaction cập nhật số dư ví trong `_context.Database.CreateExecutionStrategy().ExecuteAsync()`, đảm bảo luồng nạp tiền và thanh toán gói dịch vụ hoạt động hoàn hảo 100% [BE-WALLET] [worklog: docs/worklogs/2026-11-09_BE-BRANCH_AI_fix-branch-detail-schema-fallback.md].
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
- Không áp điều kiện mua gói tenant cho Admin đã xác thực, sửa API quản trị trả 403 sau reset dữ liệu. [worklog: docs/worklogs/2026-11-16_ADMIN-AUTH_AI_subscription-bypass.md]
- Sửa lỗi `400 Bad Request` ("The JSON value could not be converted to MT_GRMS.Application.Branches.Dtos.UpdateBranchRequest. Path: $.name") khi gọi API Tạo mới chi nhánh (`POST /api/branches`) hoặc Cập nhật chi nhánh (`PUT /api/branches/{id}`): chuyển đổi `CreateBranchRequest`, `UpdateBranchRequest` và `UpdateBranchStatusRequest` từ C# positional `record` sang POCO `class` có constructor mặc định và các getter/setter an toàn, giúp `System.Text.Json` model binding chuẩn xác dữ liệu JSON gửi từ Frontend React [BE-BRANCH] [worklog: docs/worklogs/2026-11-07_BE-BRANCH_AI_fix-branch-requests-deserialization.md].

## [Unreleased] - 2026-11-06

### Fixed
- Không áp điều kiện mua gói tenant cho Admin đã xác thực, sửa API quản trị trả 403 sau reset dữ liệu. [worklog: docs/worklogs/2026-11-16_ADMIN-AUTH_AI_subscription-bypass.md]
- Khắc phục lỗi lặp liên tục `42P01: relation "OutboxMessages" does not exist` từ worker `SupplierOrderDispatchWorker`: chuẩn hóa thứ tự thực thi câu lệnh DDL trong `DbInitializer.cs` bằng cách đưa các lệnh `CREATE TABLE IF NOT EXISTS` cho 4 bảng (`OutboxMessages`, `SupplierDispatchPackages`, `SupplierDispatchPackageItems`, `PurchaseOrderRevisions`) lên trước các lệnh `ALTER TABLE`, loại bỏ xung đột phụ thuộc bảng khi khởi động [BUG-DB] [worklog: docs/worklogs/2026-11-07_BUG-DB_AI_fix-outboxmessages-ddl-order.md].
- Khắc phục lỗi `42P01: relation "BranchAttendancePolicies" does not exist` và `relation "ShiftConfigs" does not exist` khi khởi động Backend sau khi merge code phân hệ Nhân sự (HR): tự động khởi tạo an toàn và lũy đẳng (`CREATE TABLE IF NOT EXISTS`) cho toàn bộ 6 bảng HR (`BranchAttendancePolicies`, `ShiftConfigs`, `Employees`, `EmployeeAllowances`, `EmployeeSchedules`, `Payslips`, `CashbookTransaction`) và cập nhật các cột mới còn thiếu trong `DbInitializer.cs` [BUG-DB] [worklog: docs/worklogs/2026-11-06_BUG-DB_AI_fix-missing-hr-attendance-tables.md].
- Khắc phục lỗi `fail: Microsoft.EntityFrameworkCore.Database.Command[20102]` khi khởi động Backend: tự động phát hiện các cột PIN bảo mật đã tồn tại trong bảng `Users` và đồng bộ vào bảng lịch sử `__EFMigrationsHistory`, ngăn chặn hoàn toàn việc chạy lại lệnh `ALTER TABLE "Users" ADD "FailedPinAttempts"` gây trùng lặp cột [BUG-DB] [worklog: docs/worklogs/2026-11-06_BUG-DB_AI_fix-duplicate-column-migration-error.md].
- Khắc phục toàn bộ các cảnh báo Model Validation `warn: Microsoft.EntityFrameworkCore.Model.Validation[10622]` (QueryFilter tương tác với Required Navigation) và `[20601]` (Sentinel Value cho enum mặc định `CustomerDebtStatus` và `SupplierStatus`), làm sạch 100% console log khi khởi động ứng dụng [BUG-DB] [worklog: docs/worklogs/2026-11-06_BUG-DB_AI_fix-duplicate-column-migration-error.md].

### Changed
- Đồng bộ email Admin và chủ cửa hàng mẫu sang miền mtgrms.vn. [worklog: docs/worklogs/2026-11-16_DEMO-SEED_AI_scenario-fixtures.md]
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
- Đồng bộ email Admin và chủ cửa hàng mẫu sang miền mtgrms.vn. [worklog: docs/worklogs/2026-11-16_DEMO-SEED_AI_scenario-fixtures.md]
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
- Không áp điều kiện mua gói tenant cho Admin đã xác thực, sửa API quản trị trả 403 sau reset dữ liệu. [worklog: docs/worklogs/2026-11-16_ADMIN-AUTH_AI_subscription-bypass.md]
- Khắc phục lỗi lặp liên tục `42P01: relation "OutboxMessages" does not exist` từ worker `SupplierOrderDispatchWorker`: chuẩn hóa thứ tự thực thi câu lệnh DDL trong `DbInitializer.cs` bằng cách đưa các lệnh `CREATE TABLE IF NOT EXISTS` cho 4 bảng (`OutboxMessages`, `SupplierDispatchPackages`, `SupplierDispatchPackageItems`, `PurchaseOrderRevisions`) lên trước các lệnh `ALTER TABLE`, loại bỏ xung đột phụ thuộc bảng khi khởi động [BUG-DB] [worklog: docs/worklogs/2026-11-07_BUG-DB_AI_fix-outboxmessages-ddl-order.md].

- Lỗ hổng cô lập người thuê đa tầng (Multi-Tenant Isolation) trong `AppDbContext.cs`: chuyển sang đánh giá động runtime trong `HasQueryFilter`.
- Biên dịch 0 Warning, 0 Error.
