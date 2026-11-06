# WORKLOG: [FE-AUTH] - Khôi Phục Mật Khẩu Qua Email OTP & Chuẩn Hóa Route /api

- **Ngày thực hiện**: 2026-11-06
- **Người thực hiện**: AI Coding Assistant
- **Nhánh Git (Branch)**: `dev`
- **Mã tính năng liên kết**: FE-AUTH / `forgot-password-email-otp`
- **Loại thay đổi**: Feature & Route Standardization

---

### 1. Mục Đích Thay Đổi (Why)
- Thay thế màn hình khôi phục mật khẩu mô phỏng (Mock UI) bằng luồng thực tế an toàn 3 bước:
  1. Gửi mã OTP 6 chữ số qua Email (có thời hạn 10 phút, lưu mã vào bảng `Users`).
  2. Xác thực mã OTP trước khi cho phép thiết lập mật khẩu mới.
  3. Cập nhật mật khẩu mới băm PBKDF2/SHA256 vào database và hủy mã OTP.
- Hỗ trợ gửi email HTML thực tế qua SMTP (Gmail/SendGrid) cùng cơ chế Dev Console Logger thông minh (in mã OTP ra terminal khi chưa cấu hình SMTP để dev/tester test ngay mà không cần cấu hình phức tạp).
- Chuẩn hóa toàn bộ tiền tố route về `/api` (loại bỏ `/v1`), đồng bộ với kiến trúc backend hiện tại và tinh gọn cấu hình proxy.

---

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)

#### Backend (`project/MT_GRMS_BACKEND`):
- `src/MT_GRMS.Application/Auth/Dtos/AuthDtos.cs`: Thêm 3 DTOs `ForgotPasswordRequest`, `VerifyOtpRequest`, `ResetPasswordRequest`.
- `src/MT_GRMS.Application/Common/Interfaces/IEmailService.cs`: Tạo interface gửi email OTP `IEmailService`.
- `src/MT_GRMS.Infrastructure/Services/EmailSettings.cs`: Cấu hình SMTP (Host, Port, User, Pass, Sender).
- `src/MT_GRMS.Infrastructure/Services/EmailService.cs`: Triển khai gửi email HTML OTP bằng `System.Net.Mail` kèm Dev Console Logger fallback.
- `src/MT_GRMS.Application/Auth/IAuthService.cs` & `AuthService.cs`: Thêm 3 phương thức xử lý OTP và đặt lại mật khẩu mới.
- `src/MT_GRMS.Api/Controllers/AuthController.cs`: Thêm 3 API endpoints `POST /api/auth/forgot-password`, `POST /api/auth/verify-otp`, `POST /api/auth/reset-password`.
- `src/MT_GRMS.Api/appsettings.json`: Thêm block `EmailSettings`.
- `src/MT_GRMS.Infrastructure/DependencyInjection.cs`: Đăng ký `EmailSettings` và `IEmailService`.
- `tests/MT_GRMS.UnitTests/UserAuthenticationTests.cs`: Bổ sung 4 unit tests kiểm thử luồng forgot password (100% PASS).
- `tests/MT_GRMS.IntegrationTests/PnLReportDatabaseIntegrationTests.cs` & `HouseholdTaxDatabaseIntegrationTests.cs`: Thêm `using MT_GRMS.Domain.Entities.Sales;`.

#### Frontend (`project/MT_GRMS_FRONTEND`):
- `.env`: Chuyển `VITE_API_BASE_URL` về `http://localhost:5232/api` (bỏ `/v1`).
- `src/api/httpClient.ts`: Đổi baseURL mặc định về `/api`.
- `vite.config.ts`: Tinh gọn proxy chuyển tiếp `/api` thẳng tới port `5232`.
- `src/features/auth/authService.ts`: Cập nhật `requestPasswordReset`, `verifyOtp`, `resetPassword` gọi backend API thực tế.
- `src/pages/auth/ForgotPasswordPage.tsx`: Xóa bỏ mã hardcoded `889966`, thêm countdown 60s đếm ngược gửi lại mã OTP, kết nối xác thực OTP thực tế.

---

### 3. Thay Đổi Kỹ Thuật Quan Trọng (Technical Highlights)
- **API Contracts**:
  - `POST /api/auth/forgot-password` (Body: `{ email }`) -> Trả về HTTP 200 OK
  - `POST /api/auth/verify-otp` (Body: `{ email, otpCode }`) -> Trả về HTTP 200 OK
  - `POST /api/auth/reset-password` (Body: `{ email, otpCode, newPassword }`) -> Trả về HTTP 200 OK
- **Database**:
  - Không cần migration (tận dụng 2 cột `PasswordResetOtp` và `OtpExpiry` có sẵn trong bảng `Users`).
- **Breaking Changes**: Không có.

---

### 4. Kết Quả Kiểm Thử (Verification)
- [x] Backend Build: `dotnet build MT_GRMS_BACKEND.sln` thành công (0 Lỗi, 0 Cảnh báo).
- [x] Backend Tests: `dotnet test MT_GRMS_BACKEND.sln` đạt **467/467 tests PASS (100%)** (423 Unit Tests, 44 Integration Tests).
- [x] Frontend Build: `npm run build` thành công (0 Lỗi TypeScript).

---

### 5. Chỉ Dẫn Cho Manager Khi Duyệt & Merge (Manager Merge Notes)
- **Mức độ rủi ro (Risk Level)**: Thấp (Low).
- **Hành động hậu merge**:
  - Tùy chọn: Điền tài khoản Gmail (`SmtpUser` và `SmtpPass`) vào `appsettings.json` hoặc biến môi trường trên Render nếu muốn gửi email thực tế đến hòm thư người dùng. Nếu để trống, hệ thống tự động in mã OTP ra Terminal/Console.

