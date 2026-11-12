# WORKLOG: BE-AUTH - Chặn đường vòng OTP và giảm thời gian chờ SMTP

- **Ngày thực hiện**: 2026-11-11
- **Người thực hiện**: AI Agent
- **Mã tính năng liên kết**: auth-login-integration, REQ-AUTH-08
- **Loại thay đổi**: Bugfix

## Mục đích
`POST /api/auth/login` trước đây cấp phiên ngay sau mật khẩu, bỏ qua OTP. Giao diện chờ đồng bộ SMTP trước khi mở modal OTP nên có thể đợi tới 15 giây.

## File thay đổi
- `src/MT_GRMS.Api/Controllers/AuthController.cs`: tuyến login trả challenge OTP, chỉ verify-otp cấp cookie; báo 503 khi email không khả dụng.
- `src/MT_GRMS.Application/Auth/AuthService.cs`: không trả mã OTP dev ở production; hủy challenge khi gửi email thất bại.
- `src/MT_GRMS.Infrastructure/Services/EmailService.cs`: timeout SMTP 5 giây, chỉ log mã fallback ở Development.
- `tests/MT_GRMS.IntegrationTests/AuthControllerTests.cs`: kiểm tra login trả challenge thay vì phiên.
- `src/MT_GRMS.Domain/Entities/Identity/User.cs`, `src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`: thêm trạng thái 2FA theo tài khoản, mặc định bật và backfill khi thiếu cột.
- `src/MT_GRMS.Application/Auth/Dtos/AuthDtos.cs`, `src/MT_GRMS.Application/Auth/AuthService.cs`, `src/MT_GRMS.Api/Controllers/AuthController.cs`: nhánh login theo cờ 2FA, cấp cookie ngay chỉ khi tắt; API trạng thái, challenge và confirm qua Email OTP cho Settings.
- `MT_GRMS_FRONTEND/src/pages/store/components/AccountProfileTab.tsx`, `src/features/auth/authService.ts`: công tắc 2FA tải trạng thái backend, yêu cầu mật khẩu và Email OTP trước khi cập nhật.
- `tests/MT_GRMS.UnitTests/UserAuthenticationTests.cs`: cập nhật trường hợp login trực tiếp cho tài khoản tắt 2FA.

## API và xác minh
- `POST /api/auth/login` trả `SendLoginOtpResponse`; client phải gọi `POST /api/auth/login/verify-otp` mới nhận cookie.
- Không có migration. Đây là breaking change với client gọi `/login` và giả định nhận phiên ngay.
- `dotnet build src/MT_GRMS.Api/MT_GRMS.Api.csproj -c Release --no-restore -v quiet`: PASS (21 warnings đã có ở module khác).
- Nhóm test `AuthController_Login_Should_`: 3/3 PASS, gồm trường hợp production không gửi được email trả 503.
- Nhóm test `UserAuthenticationTests`: 20/20 PASS; frontend `npm.cmd run build`: PASS.
- Build Debug không hoàn tất do DLL của API đang bị tiến trình khác giữ; build Release đã thành công.

## Ghi chú
- Rủi ro trung bình: xác nhận SMTP hoạt động trước khi triển khai production; khi gửi lỗi API trả 503 thay vì cấp phiên.
- Màn hình hiện dùng hộp thoại trình duyệt để nhập mật khẩu và OTP. Cần thay bằng modal riêng nếu muốn trải nghiệm hoàn chỉnh hơn.
