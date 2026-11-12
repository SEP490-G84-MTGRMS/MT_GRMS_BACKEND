# WORKLOG: [FE-AUTH] - Tích Hợp Xác Thực Số Điện Thoại Bằng TextBee Android SMS Gateway

- **Ngày thực hiện**: 2026-11-11
- **Người thực hiện**: AI Assistant
- **Mã tính năng liên kết**: FE-AUTH / `textbee-sms-otp-registration`

## 1. Mục tiêu & Lý do thay đổi
- Tích hợp **TextBee Android SMS Gateway** (gọi REST API tới thiết bị Android của cửa hàng để gửi tin nhắn SMS OTP thực tế tới số điện thoại chủ cửa hàng, kèm cơ chế Dev Fallback khi chưa kết nối thiết bị) vào luồng đăng ký mở cửa hàng mới.
- Bỏ hoàn toàn phụ thuộc vào Google Firebase (tránh phát sinh chi phí gói Blaze và rắc rối Invisible reCAPTCHA), sử dụng trực tiếp TextBee SMS Gateway cho toàn bộ quy trình SMS OTP.
- Kiểm tra trùng số điện thoại trên Backend trước khi kích hoạt gửi SMS và xác thực `OtpCode` trong `RegisterTenantAsync`.

## 2. Danh sách các file đã thêm mới / chỉnh sửa
### Backend (`MT_GRMS_BACKEND`)
- `src/MT_GRMS.Domain/Common/Interfaces/ISmsService.cs`: Giao diện dịch vụ SMS gửi OTP đăng ký.
- `src/MT_GRMS.Infrastructure/Services/TextBeeSettings.cs` & `TextBeeSmsService.cs`: Triển khai tích hợp TextBee Android SMS Gateway.
- `src/MT_GRMS.Application/Auth/Dtos/AuthDtos.cs`: Thêm `SendRegistrationOtpRequest`, `SendRegistrationOtpResponse`, `VerifyRegistrationOtpRequest` và bổ sung `OtpCode` vào `RegisterTenantRequest`.
- `src/MT_GRMS.Application/Auth/IAuthService.cs` & `AuthService.cs`: Triển khai kiểm tra trùng số điện thoại trước khi gửi SMS, gửi OTP qua TextBee SMS Gateway và xác thực `OtpCode` trong bộ nhớ có thời hạn (5 phút).
- `src/MT_GRMS.Api/Controllers/AuthController.cs`: Bổ sung `POST /api/auth/register/send-otp` và `POST /api/auth/register/verify-otp`.
- `tests/MT_GRMS.UnitTests/UserAuthenticationTests.cs`: Unit test kiểm thử xác thực OTP khi đăng ký cửa hàng.

### Frontend (`MT_GRMS_FRONTEND`)
- `src/features/auth/components/RegistrationOtpModal.tsx`: Component Modal nhập 6 chữ số OTP chuẩn KiotViet Style, tự động chuyển ô, hỗ trợ dán 6 số, đếm ngược 60s gửi lại mã, hỗ trợ hiển thị OTP dev fallback khi thiết bị gateway chưa online.
- `src/features/auth/authTypes.ts` & `src/features/auth/authService.ts`: Bổ sung `sendRegistrationOtp` và truyền `otpCode` khi gọi API `/auth/register`.
- `src/pages/auth/RegisterPage.tsx`: Kết nối luồng bấm "ĐĂNG KÝ MỞ CỬA HÀNG NGAY" -> gọi API backend gửi SMS OTP qua TextBee -> mở Modal xác thực 6 số -> hoàn tất tạo cửa hàng.

## 3. Kết quả kiểm thử
- Build Backend: PASS (0 errors, 0 warnings).
- Unit Tests Backend (`dotnet test`): PASS.
- Build Frontend (`npm run build`): PASS.
