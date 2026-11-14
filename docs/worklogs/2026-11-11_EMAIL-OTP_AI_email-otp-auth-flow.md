# Worklog: Chuyển Đổi Toàn Diện Sang Email OTP (Đăng Nhập 2FA & Đăng Ký Cửa Hàng)

- **Ngày thực hiện**: 2026-11-11
- **Mã Task**: `EMAIL-OTP` (`auth-login-integration`)
- **Tác giả**: `AI`
- **Trạng thái**: Hoàn thành (Verified PASS)

---

## 1. Lý do thay đổi
- Gỡ bỏ hoàn toàn cơ chế gửi SMS qua TextBee Android Gateway (`TextBeeSmsService`, `TextBeeSettings`, `ISmsService`) do phụ thuộc thiết bị Android vật lý và lỗi xác thực `AUTH_INVALID`.
- Chuyển 100% sang xác thực mã OTP 6 chữ số qua **Email** (`IEmailService` / SMTP với Dev Console Fallback) cho cả hai luồng cốt lõi:
  1. **Đăng nhập 2 bước bảo mật (Login 2FA)**: Kiểm tra mật khẩu -> Gửi mã OTP 6 số tới Email tài khoản -> Xác thực OTP -> Cấp phát HttpOnly Cookie session.
  2. **Đăng ký cửa hàng mới (Register OTP)**: Gửi mã OTP kích hoạt tới địa chỉ Email của chủ cửa hàng -> Xác thực OTP -> Khởi tạo Tenant, Branch, StoreOwner.

---

## 2. Danh sách tệp đã thay đổi
- **Xóa bỏ**:
  - `src/MT_GRMS.Application/Common/Interfaces/ISmsService.cs`
  - `src/MT_GRMS.Infrastructure/Services/TextBeeSettings.cs`
  - `src/MT_GRMS.Infrastructure/Services/TextBeeSmsService.cs`
- **Cập nhật**:
  - `src/MT_GRMS.Application/Common/Interfaces/IEmailService.cs`: Bổ sung `SendLoginOtpAsync` và `SendRegistrationOtpAsync`.
  - `src/MT_GRMS.Infrastructure/Services/EmailService.cs`: Triển khai gửi email HTML chuẩn thương hiệu KiotViet Blue (`#0066FF`) và cơ chế Dev Console Fallback khi chưa cấu hình SMTP.
  - `src/MT_GRMS.Application/Auth/Dtos/AuthDtos.cs`: Thêm `SendLoginOtpResponse`, `VerifyLoginOtpRequest`, cập nhật `SendRegistrationOtpResponse`.
  - `src/MT_GRMS.Application/Auth/IAuthService.cs` & `src/MT_GRMS.Application/Auth/AuthService.cs`:
    - Triển khai `SendLoginOtpAsync` và `VerifyLoginOtpAsync` với bộ nhớ tạm `LoginOtpStore` (TTL 5 phút, chống brute-force tối đa 5 lần thử sai, One-Time-Use).
    - Cập nhật `SendRegistrationOtpAsync` và `VerifyRegistrationOtpAsync` sang xác thực theo Email.
  - `src/MT_GRMS.Api/Controllers/AuthController.cs`: Bổ sung endpoints `POST /api/auth/login/send-otp` và `POST /api/auth/login/verify-otp`.
  - `src/MT_GRMS.Infrastructure/DependencyInjection.cs`: Gỡ đăng ký `ISmsService` / `TextBeeSettings`.
  - `tests/MT_GRMS.UnitTests/UserAuthenticationTests.cs`: Bổ sung unit test kiểm thử luồng Login 2FA Email OTP, chống brute-force 5 lần và Đăng ký Email OTP.

---

## 3. Kết quả kiểm thử
- `dotnet test --filter "FullyQualifiedName~UserAuthenticationTests|FullyQualifiedName~AuthControllerTests" -c Release`:
  - `MT_GRMS.UnitTests`: **20/20 Passed (0 Failed)**.
  - `MT_GRMS.IntegrationTests`: **7/7 Passed (0 Failed)**.

