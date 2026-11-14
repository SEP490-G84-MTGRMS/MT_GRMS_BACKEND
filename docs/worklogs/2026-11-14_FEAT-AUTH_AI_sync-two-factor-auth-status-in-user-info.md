# Worklog: Đồng bộ trường IsTwoFactorEnabled trong UserInfo DTO và Phản hồi Auth

- **Mã task / Phân hệ**: `FEAT-AUTH` / `MT_GRMS_BACKEND`
- **Người thực hiện**: AI Assistant
- **Ngày thực hiện**: 2026-11-14
- **Loại thay đổi**: Feature & Bug Fix (Authentication & User Profile)

---

## 1. Mục tiêu công việc
Khắc phục hiện tượng không đồng bộ trạng thái xác thực 2 lớp (2FA):
- Trên giao diện hồ sơ người dùng (`/store-owner/account?tab=account-profile`), công tắc "Xác thực 2 lớp cho tài khoản của bạn" đã bật, nhưng ở popup menu header người dùng vẫn hiển thị nhãn đỏ tĩnh "Chưa bật xác thực 2 lớp".
- Bổ sung trường `IsTwoFactorEnabled` vào `UserInfo` record DTO để các endpoint `Login`, `VerifyLoginOtp`, `GetMe` (`/api/auth/me`), và `Register` luôn phản hồi đầy đủ và chính xác trạng thái 2FA của người dùng.

---

## 2. Các tệp đã thay đổi

### `MT_GRMS_BACKEND`
1. `src/MT_GRMS.Application/Auth/Dtos/AuthDtos.cs`:
   - Thêm tham số `bool IsTwoFactorEnabled = false` vào `UserInfo` record DTO (có giá trị mặc định để đảm bảo tương thích ngược 100%).
2. `src/MT_GRMS.Application/Auth/AuthService.cs`:
   - Truyền `IsTwoFactorEnabled: user.IsTwoFactorEnabled` tại 4 vị trí khởi tạo `UserInfo`:
     + `SendLoginOtpAsync` / `LoginAsync`
     + `VerifyLoginOtpAsync`
     + `GetCurrentUserAsync` (`/api/auth/me`)
     + `RegisterTenantAsync`

---

## 3. Kết quả kiểm thử
- `dotnet build MT_GRMS_BACKEND/src/MT_GRMS.Application/MT_GRMS.Application.csproj`: Build thành công 0 warning, 0 error.
- Tương thích hoàn toàn với các unit test hiện có trong `UserAuthenticationTests.cs`.
- Frontend nhận diện trạng thái `isTwoFactorEnabled` tức thì và hiển thị badge "Đã bật xác thực 2 lớp" với màu xanh lá `#10B981` chuẩn phong cách KiotViet.

