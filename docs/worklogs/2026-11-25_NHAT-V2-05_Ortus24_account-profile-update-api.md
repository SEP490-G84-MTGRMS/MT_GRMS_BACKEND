# WORKLOG: NHAT-V2-05 - Bổ sung API cập nhật hồ sơ cá nhân và đồng bộ số điện thoại trong UserInfo

- **Ngày thực hiện**: 2026-11-25
- **Người thực hiện**: Ortus24 / AI Agent
- **Nhánh Git (Branch)**: `refactor/account-profile-and-sidebar`
- **Mã tính năng liên kết**: `NHAT-V2-05` / `REQ-AUTH-15` (`auth-login-integration`)
- **Loại thay đổi**: Bugfix / Feature

---

### 1. Mục Đích Thay Đổi (Why)
- Trước đó `EditProfileModal` trên trang Tài khoản chỉ lưu state cục bộ (F5 mất dữ liệu), và `UserInfo` trả về từ `/api/auth/me` chưa bao gồm trường `Phone`.

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)
- `src/MT_GRMS.Application/Auth/Dtos/AuthDtos.cs`: Thêm `Phone` vào `UserInfo` và thêm `UpdateProfileRequest`.
- `src/MT_GRMS.Application/Auth/Validators/LoginRequestValidator.cs`: Thêm `UpdateProfileRequestValidator`.
- `src/MT_GRMS.Application/Auth/IAuthService.cs`, `src/MT_GRMS.Application/Auth/AuthService.cs`: Cài đặt `UpdateProfileAsync` (chuẩn hóa SĐT 10 số, chặn trùng SĐT `PHONE_EXISTS`) và trả `Phone` trong `UserInfo`.
- `src/MT_GRMS.Api/Controllers/AuthController.cs`: Thêm endpoint `PUT /api/auth/me/profile` (`[Authorize]`).
- `tests/MT_GRMS.UnitTests/UserAuthenticationTests.cs`: Bổ sung test `UpdateProfileAsync_UpdatesFullNameAndPhone_AndRejectsDuplicatePhone`.

### 3. API Endpoints & Contract
- **Method & Route**: `PUT /api/auth/me/profile`
- **Request Body (DTO)**: `UpdateProfileRequest(string FullName, string? Phone)`
- **Response Data (DTO)**: `ApiResponse<UserInfo>`
- **Database Migration**: Không có (dùng các cột `FullName`, `Phone`, `NormalizedPhone` sẵn có trên bảng `Users`).
- **Breaking Changes**: Không có.

### 4. Kiểm Thử & Xác Nhận (Verification)
- [x] Đã chạy `dotnet build MT_GRMS_BACKEND.sln -c Release`: Thành công (0 lỗi).
- [x] Đã chạy `dotnet test -c Release` cho `UserAuthenticationTests`: Đạt 100%.

### 5. Ghi Chú Cho Manager Khi Duyệt & Merge (Manager Merge Notes)
- **Mức độ rủi ro**: Thấp
- **File trọng yếu cần review**: `AuthService.cs`, `AuthController.cs`
- **Hành động sau Merge**: Khởi động lại dịch vụ API để kích hoạt `PUT /api/auth/me/profile`.
