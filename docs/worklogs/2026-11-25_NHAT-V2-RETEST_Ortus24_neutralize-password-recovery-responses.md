# Worklog: Chuẩn hóa phản hồi trung tính tuyệt đối cho API Quên mật khẩu & OTP (`NHAT-V2-11` / `NHAT-AUTH-01`)

- **Ngày thực hiện:** 2026-11-25
- **Nhánh:** `fix/retest-nhat-v2-remaining`
- **Tác giả:** Ortus24

## 1. Bối cảnh & Nguyên nhân
Trong đợt retest (`Retest_Nhat_2026-11-25.md`), luồng khôi phục mật khẩu đã đạt giới hạn 5 lần đoán OTP và kiểm tra mật khẩu mạnh, nhưng nội dung chuỗi thông báo HTTP vẫn khác nhau giữa các nhánh:
- `POST /api/auth/forgot-password`: Trả 2 chuỗi `message` khác nhau giữa tài khoản tồn tại và không tồn tại.
- `POST /api/auth/verify-otp` và `POST /api/auth/reset-password`: Trả các `errorCode` / `message` khác nhau giữa tài khoản không tồn tại, OTP chưa yêu cầu, OTP hết hạn và OTP nhập sai còn X lần thử.

## 2. Thay đổi đã thực hiện
- `src/MT_GRMS.Api/Controllers/AuthController.cs`:
  - Đồng nhất 100% thông điệp `200 OK` tại `ForgotPassword` (`NeutralForgotPasswordMessage`) bất kể email có tồn tại hay không.
  - Đồng nhất 100% thông điệp lỗi `400 BadRequest` (`ErrorCode = "OTP_INVALID"`, `Message = "Mã xác thực OTP không hợp lệ hoặc đã hết hạn."`) tại `VerifyOtp` và `ResetPassword` cho mọi trạng thái tài khoản / OTP, chỉ giữ riêng lỗi định dạng mật khẩu yếu (`WEAK_PASSWORD`) được kiểm tra trước khi truy vấn tài khoản.
- `tests/MT_GRMS.UnitTests/Controllers/AuthControllerTests.cs`:
  - Bổ sung kiểm tra đối chiếu `ErrorCode` và `Message` đồng nhất tuyệt đối giữa các trạng thái `KeyNotFoundException`, `OTP_INVALID`, `OTP_EXPIRED`.

## 3. Kết quả kiểm thử
- `dotnet test -c Release --filter "FullyQualifiedName~UserAuthenticationTests|FullyQualifiedName~AuthControllerTests"`: PASS 100%.
