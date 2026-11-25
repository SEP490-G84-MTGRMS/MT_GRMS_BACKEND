# Worklog: Băm một chiều SHA-256 cho mã `PasswordResetOtp` lưu trong CSDL (`NHAT-V2-11`)

- **Ngày thực hiện:** 2026-11-25
- **Nhánh:** `fix/retest-nhat-v2-remaining`
- **Tác giả:** Ortus24

## 1. Bối cảnh & Nguyên nhân
Trong báo cáo kiểm thử lại lần 2 (`Retest_Nhat_2026-11-25_Lan_2.md`), toàn bộ phản hồi API khôi phục mật khẩu đã đồng nhất trung tính, tuy nhiên trong `AuthService.ForgotPasswordAsync` vẫn lưu trực tiếp chuỗi 6 chữ số OTP dạng thuần vào cột `Users.PasswordResetOtp`.

## 2. Thay đổi đã thực hiện
- `src/MT_GRMS.Application/Auth/AuthService.cs`:
  - Bổ sung `HashPasswordResetOtp(string userId, string otp)` băm `SHA256("pwd-reset:{userId}:{otp}")` lấy 120-bit (15 bytes) mã hóa Base64Url 20 ký tự (vừa khít cột `PasswordResetOtp varchar(20)` hiện hữu mà không cần tạo EF Migration mới).
  - `ForgotPasswordAsync` lưu giá trị băm vào `user.PasswordResetOtp`, chỉ gửi mã 6 chữ số gốc qua email.
  - `VerifyOtpAsync` so sánh hằng thời gian (`CryptographicOperations.FixedTimeEquals`) trên giá trị băm.
- `tests/MT_GRMS.UnitTests/UserAuthenticationTests.cs`:
  - Cập nhật các ca kiểm thử khôi phục mật khẩu xác nhận `user.PasswordResetOtp != otpCode` (không lưu dạng thuần) và khớp đúng hàm băm.

## 3. Kết quả kiểm thử
- `dotnet test -c Release --filter "FullyQualifiedName~UserAuthenticationTests|FullyQualifiedName~AuthControllerTests"`: PASS 71/71.
