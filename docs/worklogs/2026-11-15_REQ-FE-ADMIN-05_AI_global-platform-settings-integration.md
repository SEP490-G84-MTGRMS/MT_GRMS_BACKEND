# WORKLOG: REQ-FE-ADMIN-05 - Cấu hình Nền tảng Toàn cục SaaS (/admin/settings)

- **Ngày thực hiện**: 2026-11-15
- **Người thực hiện**: AI Assistant
- **Nhánh Git (Branch)**: `dev`
- **Mã tính năng liên kết**: [REQ-FE-ADMIN-05] Cấu hình Nền tảng Toàn cục SaaS (/admin/settings)
- **Loại thay đổi**: Feature & Architectural Refactor

---

### 1. Mục Đích Thay Đổi (Why)
- **Giải quyết triệt để vấn đề Hardcode**: Trước đây, các chính sách kinh doanh cốt lõi của nền tảng SaaS MT-GRMS bị ghi đè cứng (hardcoded) trong mã nguồn hoặc file cấu hình `appsettings.json`, dẫn đến mỗi khi cần điều chỉnh chính sách thương mại hoặc thông tin CSKH thì phải sửa code và build lại:
  - Đơn giá mua thêm chi nhánh: Cố định `80.000 đ/tháng` tại `TenantBranchAddonController.cs`.
  - Đơn giá mua thêm dung lượng lưu trữ: Cố định `15.000 đ/GB/tháng` tại `TenantStorageController.cs`.
  - Số ngày dùng thử (Trial): Cố định `72 giờ (3 ngày)` tại `AuthService.cs`.
  - Tài khoản Master VietQR nhận tiền nạp ví & thanh toán gói: Fallback cứng tại `WalletService.cs`.
  - Thiếu hoàn toàn cơ chế Bật/Tắt chế độ bảo trì toàn hệ thống (Maintenance Mode) khi chuẩn bị phát hành phiên bản mới.
- **Xây dựng trung tâm điều hành toàn cục**: Cung cấp giao diện Master Admin `/admin/settings` chuyên nghiệp, bo tròn chuẩn KiotViet Design System (Full Pill `9999px`, Card `24px`, Monochrome SVG Icons, tĩnh không rung lắc) và hệ thống REST APIs kiểm soát toàn diện 4 nhóm nghiệp vụ:
  1. Chính sách thuê bao dùng thử (Trial Days) & Ân hạn (Grace Period) & Biểu phí Addon.
  2. Tài khoản ngân hàng thụ hưởng Master (Master VietQR Beneficiary).
  3. Cấu hình liên hệ & CSKH (Hotline, Email, Zalo OA).
  4. Bật/Tắt chế độ bảo trì hệ thống (Maintenance Mode) kèm thông điệp và thời gian dự kiến mở lại.

---

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)

#### Backend (MT_GRMS_BACKEND)
- `src/MT_GRMS.Domain/Entities/SaaS/PlatformSetting.cs`:
  - Thực thể Singleton cấp hệ thống (Root Entity ID = 1), kế thừa `BaseEntity<int>`, không bị ảnh hưởng bởi Multi-tenant Query Filter.
- `src/MT_GRMS.Infrastructure/Persistence/Configurations/SaaS/PlatformSettingConfiguration.cs`:
  - Cấu hình EF Core mapping bảng `"PlatformSettings"`, thiết lập giá trị mặc định, độ dài và kiểu dữ liệu.
- `src/MT_GRMS.Infrastructure/Persistence/AppDbContext.cs`:
  - Đăng ký `DbSet<PlatformSetting> PlatformSettings`.
- `src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`:
  - Bổ sung lệnh DDL `CREATE TABLE IF NOT EXISTS "PlatformSettings"` và seed tự động bản ghi ID 1 với giá trị mặc định chuẩn.
- `src/MT_GRMS.Application/Admin/Dtos/PlatformSettingDtos.cs`:
  - `PlatformSettingDto`, `UpdatePlatformSettingRequest`, `PlatformPublicConfigDto`.
- `src/MT_GRMS.Application/Admin/Validators/PlatformSettingValidators.cs`:
  - `UpdatePlatformSettingValidator` xác thực các trường: TrialDays (1-30), GracePeriodDays (1-60), Hotline, Email, MasterBank, AccountNumber.
- `src/MT_GRMS.Application/Admin/Services/IPlatformSettingService.cs`:
  - Hợp đồng dịch vụ: `GetSettingsAsync`, `UpdateSettingsAsync`, `GetPublicConfigAsync`.
- `src/MT_GRMS.Infrastructure/Services/PlatformSettingService.cs`:
  - Triển khai xử lý đọc/ghi và fallback an toàn nếu chưa có bản ghi trong CSDL.
- `src/MT_GRMS.Infrastructure/DependencyInjection.cs`:
  - Đăng ký DI Scoped `IPlatformSettingService -> PlatformSettingService`.
- `src/MT_GRMS.Api/Controllers/AdminSettingsController.cs`:
  - Controller quản trị Master Admin: `GET /api/v1/admin/settings`, `PUT /api/v1/admin/settings` (Role: ADMIN).
- `src/MT_GRMS.Api/Controllers/PlatformPublicConfigController.cs`:
  - Controller công khai: `GET /api/v1/platform/public-config` (AllowAnonymous) phục vụ trang chủ, email, footer.
- `src/MT_GRMS.Api/Controllers/TenantBranchAddonController.cs`:
  - Đọc đơn giá mua thêm chi nhánh từ `PlatformSettings` thay cho hằng số cứng.
- `src/MT_GRMS.Api/Controllers/TenantStorageController.cs`:
  - Đọc đơn giá dung lượng từ `PlatformSettings` thay cho hằng số cứng.
- `src/MT_GRMS.Infrastructure/Services/WalletService.cs`:
  - Đọc tài khoản thụ hưởng Master VietQR từ `PlatformSettings`.
- `src/MT_GRMS.Application/Auth/AuthService.cs`:
  - Đọc số ngày dùng thử mặc định từ `PlatformSettings` khi đăng ký gian hàng mới.
- `tests/MT_GRMS.UnitTests/Admin/AdminSettingsApiControllerTests.cs`:
  - 4 unit tests kiểm thử toàn diện Controller, Validator và Public Config.

#### Frontend (MT_GRMS_FRONTEND)
- `src/features/admin/types/settings.types.ts`:
  - Interfaces TypeScript: `PlatformSetting`, `UpdatePlatformSettingPayload`, `PlatformPublicConfig`.
- `src/features/admin/api/adminApi.ts`:
  - `settingsApi` (`getSettings`, `updateSettings`, `getPublicConfig`).
- `src/features/admin/components/settings/`:
  - `PlatformSettingsHeader.tsx`: Thanh tiêu đề hero, trạng thái chưa lưu (isDirty), nút Lưu & Hoàn tác.
  - `SubscriptionPolicyCard.tsx`: Thẻ cấu hình Trial, Grace, Addon chi nhánh & dung lượng.
  - `MasterBeneficiaryCard.tsx`: Thẻ cấu hình ngân hàng thụ hưởng Master VietQR & khung preview QR.
  - `SupportContactCard.tsx`: Thẻ cấu hình Hotline 1900 6522, Email hỗ trợ, Zalo OA.
  - `MaintenanceModeCard.tsx`: Thẻ gạt bật/tắt bảo trì hệ thống kèm khung xem trước banner cảnh báo.
  - `SettingsSummarySidebar.tsx`: Thẻ tóm tắt nhanh các chính sách đang áp dụng và nút lưu nhanh dính bên phải.
  - `index.ts`: Export toàn bộ subcomponents.
- `src/pages/admin/AdminSettingsPage.tsx`:
  - Trang chính kết nối API, quản lý form state, toast thông báo, xử lý validation phía client.
- `src/app/router.tsx`:
  - Bổ sung route `/admin/settings` trong `AdminLayout`.
- `src/layouts/AdminLayout/AdminSidebar.tsx`:
  - Bổ sung menu item "Cấu hình Nền tảng" kèm icon đơn sắc `Sliders`.

---

### 3. Kết Quả Kiểm Thử (Verification)
- **Unit Tests Backend**:
  - `dotnet test tests/MT_GRMS.UnitTests/MT_GRMS.UnitTests.csproj`: **1.051 tests passed, 0 failed** (bao gồm 4 unit tests mới cho `AdminSettingsApiControllerTests`).
- **Build Solution Backend**:
  - `dotnet build MT_GRMS_BACKEND.sln`: **0 Error(s)**, 33 Warnings thông thường.
- **Build Frontend**:
  - `npm run build` (`tsc -b && vite build`): Đạt 0 lỗi TypeScript, bundle thành công.

---

### 4. Ghi Chú Cho Lead
- CSDL tự động tạo bảng `"PlatformSettings"` và chèn dòng cấu hình mặc định (ID = 1) thông qua `DbInitializer.cs`. Không cần chạy migration thủ công trên môi trường Development.
- Bật Chế độ bảo trì (`isMaintenanceMode = true`) sẽ phát cờ toàn hệ thống qua `PlatformPublicConfigDto` để hiển thị banner cảnh báo tới toàn bộ người dùng mà không cần reload backend.
