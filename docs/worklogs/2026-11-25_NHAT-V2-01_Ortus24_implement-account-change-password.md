# Worklog: Triển khai API Đổi mật khẩu tài khoản đang đăng nhập (`NHAT-V2-01` / `REQ-AUTH-14`)

- **Ngày thực hiện:** 2026-11-25
- **Mã task / Change:** `auth-login-integration` (`REQ-AUTH-14`, `T-PWD-01..04`, Bug `NHAT-V2-01`)
- **Người thực hiện:** Ortus24 (cùng AI Agent)

## 1. Bối cảnh & Nguyên nhân gốc
- Trên màn hình `/store-owner/account`, hộp thoại `ChangePasswordModal.tsx` trước đó chỉ kiểm tra độ dài `>= 6` ký tự ở phía trình duyệt rồi bật thông báo thành công giả mà không gọi bất kỳ API Backend nào.
- Backend `AuthController` chưa có endpoint cho phép người dùng đã đăng nhập tự đổi mật khẩu của chính mình.

## 2. Các thay đổi đã thực hiện (Backend)
- **`src/MT_GRMS.Application/Auth/Dtos/AuthDtos.cs`**: Bổ sung `ChangePasswordRequest(CurrentPassword, NewPassword)`.
- **`src/MT_GRMS.Application/Auth/Validators/LoginRequestValidator.cs`**: Bổ sung `ChangePasswordRequestValidator` kiểm tra mật khẩu hiện tại không rỗng và mật khẩu mới đạt chuẩn mạnh (`8–64` ký tự, gồm chữ hoa, chữ thường, chữ số, ký tự đặc biệt, không khoảng trắng).
- **`src/MT_GRMS.Application/Auth/IAuthService.cs` & `AuthService.cs`**: Bổ sung `ChangePasswordAsync(userId, request)`:
  - Kiểm tra tài khoản đang hoạt động và xác minh `CurrentPassword` (nếu sai ném `ArgumentException` với mã `INVALID_CURRENT_PASSWORD` để trả HTTP 400 thay vì 401).
  - Chặn mật khẩu mới trùng mật khẩu cũ (`PASSWORD_SAME_AS_OLD`).
  - Băm mật khẩu mới bằng `IPasswordHasher`, xoay mới `SecurityStamp` để thu hồi tức thì mọi token trên các thiết bị khác thông qua `SessionTokenValidator`, đồng thời gọi `IssueLoginSessionAsync` để phát hành phiên và `RefreshToken` mới cho thiết bị hiện tại.
- **`src/MT_GRMS.Api/Controllers/AuthController.cs`**: Bổ sung endpoint `POST /api/auth/me/change-password` (`[Authorize]`), lấy `userId` từ JWT claims và cập nhật cặp HttpOnly cookies (`access_token`, `refresh_token`) mới trên response.
- **Unit Tests**: Bổ sung test cases trong `tests/MT_GRMS.UnitTests/UserAuthenticationTests.cs` và `tests/MT_GRMS.UnitTests/Controllers/AuthControllerTests.cs`.

## 3. Ghi chú cho Tech Lead / Reviewer
- Phiên trên máy hiện tại được giữ liền mạch nhờ cặp HttpOnly cookies mới mang `security_stamp` mới; mọi thiết bị khác bị từ chối ngay ở request kế tiếp do lệch `security_stamp` hoặc lệch `RefreshToken`.
