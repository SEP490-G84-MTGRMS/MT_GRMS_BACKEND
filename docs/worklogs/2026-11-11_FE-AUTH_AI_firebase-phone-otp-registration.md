# WORKLOG: [FE-AUTH] - Tích Hợp Xác Thực Số Điện Thoại Bằng Google Firebase Phone Authentication

- **Ngày thực hiện**: 2026-11-11
- **Người thực hiện**: AI Assistant
- **Mã tính năng liên kết**: FE-AUTH / `firebase-phone-otp-registration`

## 1. Mục tiêu & Lý do thay đổi
- Tích hợp **Google Firebase Phone Authentication** (hỗ trợ miễn phí 10 tin nhắn SMS thật mỗi ngày từ tổng đài Google, kèm Invisible reCAPTCHA chống spam) vào luồng đăng ký mở cửa hàng mới.
- Bổ sung kiểm tra trùng số điện thoại trên Backend trước khi kích hoạt gửi SMS và xác thực `FirebaseIdToken` trong `RegisterTenantAsync`.

## 2. Danh sách các file đã thêm mới / chỉnh sửa
### Backend (`MT_GRMS_BACKEND`)
- `src/MT_GRMS.Application/Auth/Dtos/AuthDtos.cs`: Thêm `SendRegistrationOtpRequest`, `SendRegistrationOtpResponse`, `VerifyRegistrationOtpRequest` và bổ sung `FirebaseIdToken`, `OtpCode` vào `RegisterTenantRequest`.
- `src/MT_GRMS.Application/Auth/IAuthService.cs` & `AuthService.cs`: Triển khai kiểm tra trùng số điện thoại trước khi gửi SMS, xác thực `FirebaseIdToken` (kiểm tra hạn dùng và khớp claim `phone_number` chuẩn E.164) cùng cơ chế Dev Fallback OTP.
- `src/MT_GRMS.Api/Controllers/AuthController.cs`: Bổ sung `POST /api/auth/register/send-otp` và `POST /api/auth/register/verify-otp`.
- `tests/MT_GRMS.UnitTests/UserAuthenticationTests.cs`: Bổ sung unit test kiểm thử xác thực OTP khi đăng ký cửa hàng.

### Frontend (`MT_GRMS_FRONTEND`)
- `src/config/firebase.ts`: Khởi tạo Firebase App & Auth (`auth.languageCode = "vi"`), `RecaptchaVerifier` (invisible) và hàm `sendFirebasePhoneOtp`.
- `src/features/auth/components/RegistrationOtpModal.tsx`: Component Modal nhập 6 chữ số OTP chuẩn KiotViet Style, tự động chuyển ô, hỗ trợ dán 6 số, đếm ngược 60s gửi lại mã.
- `src/features/auth/authTypes.ts` & `src/features/auth/authService.ts`: Bổ sung `sendRegistrationOtp` và truyền `firebaseIdToken` khi gọi API `/auth/register`.
- `src/pages/auth/RegisterPage.tsx`: Kết nối luồng bấm "ĐĂNG KÝ MỞ CỬA HÀNG NGAY" -> gửi SMS OTP qua Google Firebase -> mở Modal xác thực 6 số -> hoàn tất tạo cửa hàng.

## 3. Kết quả kiểm thử
- Build Backend & Unit Tests (`dotnet test`): PASS (19/19).
- Build Frontend (`npm run build`): PASS.

