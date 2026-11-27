# WORKLOG: NHAT-V2-11 - Gia cố bảo mật luồng Quên mật khẩu (Chống dò Email 404, Giới hạn OTP & Mật khẩu mạnh)

- **Ngày thực hiện**: 2026-11-25
- **Người thực hiện**: Ortus24 / AI Agent
- **Nhánh Git (Branch)**: `refactor/forgot-password-otp-security`
- **Mã tính năng liên kết**: `NHAT-V2-11` (`forgot-password-email-otp`)
- **Loại thay đổi**: Bugfix / Security

---

### 1. Mục Đích Thay Đổi (Why)
- Trước đó `POST /api/Auth/verify-otp` và `POST /api/Auth/reset-password` trả `404 NotFound` khi email không tồn tại (lộ thông tin tài khoản ở Bước 2), không giới hạn số lần đoán sai OTP 6 chữ số và cho phép đặt mật khẩu yếu (`12345678`).

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)
- `src/MT_GRMS.Api/Controllers/AuthController.cs`: Chuẩn hóa `KeyNotFoundException` tại `VerifyOtp` và `ResetPassword` về `400 BadRequest` trung tính.
- `src/MT_GRMS.Application/Auth/AuthService.cs`: So sánh OTP bằng `CryptographicOperations.FixedTimeEquals`, giới hạn tối đa 5 lần nhập sai OTP (hủy mã khi vượt ngưỡng) và áp dụng chuẩn mật khẩu mạnh (`8–64` ký tự, chữ hoa, chữ thường, số, ký tự đặc biệt, không khoảng trắng).
- `tests/MT_GRMS.UnitTests/Controllers/AuthControllerTests.cs`, `tests/MT_GRMS.UnitTests/UserAuthenticationTests.cs`: Cập nhật và bổ sung kiểm thử giới hạn 5 lần sai OTP + từ chối mật khẩu yếu.

### 3. API Endpoints & Contract
- **Method & Route**: `POST /api/Auth/verify-otp`, `POST /api/Auth/reset-password`
- **Database Migration**: Không có.
- **Breaking Changes**: Không có (trả `400` thay vì `404` khi email không tồn tại).

### 4. Kiểm Thử & Xác Nhận (Verification)
- [x] Đã chạy `dotnet build MT_GRMS_BACKEND.sln -c Release`: Thành công (0 lỗi).
- [x] Đã chạy `dotnet test -c Release` cho `UserAuthenticationTests` và `AuthControllerTests`: Đạt 100%.

### 5. Ghi Chú Cho Manager Khi Duyệt & Merge (Manager Merge Notes)
- **Mức độ rủi ro**: Thấp
- **File trọng yếu cần review**: `AuthController.cs`, `AuthService.cs`
- **Hành động sau Merge**: Khởi động lại dịch vụ API.
