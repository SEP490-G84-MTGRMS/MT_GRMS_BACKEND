# WORKLOG: BE-AUTH - Mặc định tắt xác thực 2 bước (2FA OTP Email) khi tạo tài khoản

- **Mã Task / Tag**: `[BE-AUTH]`
- **Ngày thực hiện**: 2026-11-13
- **Tác giả**: AI Assistant
- **Phạm vi thay đổi**: `MT_GRMS_BACKEND` (`src/MT_GRMS.Domain`, `src/MT_GRMS.Application`, `src/MT_GRMS.Infrastructure`)

---

## 1. Mục Tiêu & Lý Do Thay Đổi
- Theo yêu cầu nghiệp vụ: Khi người dùng đăng ký hoặc tạo tài khoản mới trong hệ thống (Chủ cửa hàng, Nhân viên, Thu ngân), tính năng **Xác thực 2 bước (2FA OTP gửi qua Email)** mặc định sẽ ở trạng thái **TẮT** (`IsTwoFactorEnabled = false`).
- Người dùng có thể đăng nhập trực tiếp bằng tên đăng nhập / số điện thoại và mật khẩu mà không bị bắt buộc phải nhập mã OTP xác thực 2 bước ngay từ đầu.
- Người dùng vẫn có thể chủ động vào phần Cài đặt tài khoản (Security Settings) để bật tính năng 2FA OTP bất kỳ lúc nào nếu có nhu cầu tăng cường bảo mật.

---

## 2. Chi Tiết Các File Đã Sửa
1. `src/MT_GRMS.Domain/Entities/Identity/User.cs`:
   - Đổi giá trị khởi tạo mặc định của `IsTwoFactorEnabled` từ `true` thành `false`.
2. `src/MT_GRMS.Application/Auth/AuthService.cs`:
   - Gán tường minh `IsTwoFactorEnabled = false` trong luồng đăng ký chủ cửa hàng (`RegisterStoreOwnerAsync`).
3. `src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`:
   - Cập nhật câu lệnh migration schema SQL: `DEFAULT false` và cập nhật các bản ghi chưa có giá trị về `false`.

---

## 3. Kết Quả Kiểm Thử
- Dự án `MT_GRMS.Domain`, `MT_GRMS.Application`, `MT_GRMS.Infrastructure` đã được build thành công (`dotnet build`) với 0 lỗi (0 Error).
- Các luồng đăng nhập `LoginAsync` và `SendLoginOtpAsync` khi `IsTwoFactorEnabled = false` sẽ cấp phiên đăng nhập trực tiếp ngay lập tức không gửi OTP.

