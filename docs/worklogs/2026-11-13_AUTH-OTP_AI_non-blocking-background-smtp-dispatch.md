# Worklog: Chuyển Gửi Email OTP Qua SMTP Sang Chế Độ Chạy Nền (Non-Blocking Background Dispatch)

- **Ngày thực hiện**: 2026-11-13
- **Mã Task/FE**: AUTH-OTP
- **Tác giả**: AI Assistant
- **Mức độ rủi ro**: Low (Không thay đổi API contract, không thay đổi database hay bảo mật xác thực OTP)

---

## 1. Mục đích (Why)
- **Vấn đề thực tế**: Khi người dùng nhập đúng thông tin đăng nhập/đăng ký tài khoản, hệ thống cần gửi mã Email OTP 2FA. Nếu cấu hình SMTP thực tế (như Gmail SMTP `smtp.gmail.com:587`), việc thiết lập kết nối socket TCP, đàm phán TLS/SSL handshake và truyền tải thư qua SMTP tiêu tốn từ 1.5 đến 4 giây.
- **Hệ quả**: Trong khoảng thời gian này, luồng HTTP request bị chặn đứng (blocked await), nút đăng nhập hiển thị hiệu ứng xoay "ĐANG GỬI MÃ EMAIL OTP..." và popup modal nhập mã OTP bị hoãn lại, khiến người dùng cảm giác hệ thống bị lag hoặc đơ.
- **Giải pháp**: Nếu thông tin tài khoản/mật khẩu hợp lệ:
  1. Backend tạo mã OTP, lưu phiên thử thách bảo mật trong memory/store.
  2. Lệnh truyền gửi thư qua giao thức mạng SMTP được chuyển sang chạy nền (`Task.Run`) phi chặn (fire-and-forget background execution có xử lý try-catch và log đầy đủ).
  3. API `POST /api/auth/login-otp` và `/api/auth/register-otp` phản hồi thành công `200 OK` tức thì (< 15ms).
  4. Frontend nhận kết quả ngay trong 1 khung hình và lập tức bật popup modal để người dùng sẵn sàng nhập OTP trong khi email đang được chuyển tới hộp thư.

---

## 2. Chi Tiết Tệp Đã Sửa (What)

### `project/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/Services/EmailService.cs`
- **Sửa hàm**: `SendOtpEmailInternalAsync`
- **Thay đổi**:
  - Không `await client.SendMailAsync(mail)` trực tiếp trên luồng chính của HTTP Request.
  - Chuyển việc tạo `SmtpClient` và gửi `mail` vào `_ = Task.Run(async () => { ... })` chạy nền trên threadpool với timeout 15 giây và bắt ngoại lệ (catch error log) an toàn.
  - Trả về `Task.FromResult(true)` ngay lập tức sau khi kích hoạt tác vụ gửi nền, đảm bảo caller (`AuthService.SendLoginOtpAsync`, `SendRegistrationOtpAsync`, `SendPasswordResetOtpAsync`) hoàn tất phản hồi trong tích tắc.
  - Giữ nguyên log in ra Console ở môi trường Development để lập trình viên và kiểm thử viên có thể đối chiếu mã OTP ngay lập tức.

---

## 3. Chi Tiết Kỹ Thuật (Technical Highlights)
- **API Contracts**: Không thay đổi Request/Response DTO (`SendLoginOtpResponse`, `SendRegistrationOtpResponse`).
- **Database/Entities**: Không ảnh hưởng CSDL hay migrations.
- **Bảo mật & Rate Limit**: Toàn bộ cơ chế kiểm tra mật khẩu, rate limit chống brute-force, hash so khớp mã OTP trong 5 phút đều được bảo toàn 100%. Nếu mật khẩu sai, hệ thống vẫn phản hồi lỗi `401 Unauthorized` ngay lập tức và tuyệt đối không mở popup hay gửi OTP.
- **Tương thích Unit Tests**: Toàn bộ 23/23 tests thuộc suite `UserAuthenticationTests` trong `MT_GRMS.UnitTests` đều pass (sử dụng in-memory `FakeEmailService` và `SuccessEmailService`).

---

## 4. Kết Quả Kiểm Thử (Verification)
- **Build**: `dotnet build src/MT_GRMS.Api/MT_GRMS.Api.csproj -c Release` $\rightarrow$ **Succeeded 0 Errors, 0 Warnings**.
- **Unit Tests**:
  ```bash
  dotnet test tests/MT_GRMS.UnitTests/MT_GRMS.UnitTests.csproj -c Release --filter "FullyQualifiedName~UserAuthenticationTests"
  ```
  $\rightarrow$ **Passed! - Failed: 0, Passed: 23, Skipped: 0, Total: 23 (Duration: 263 ms)**.

---

## 5. Ghi Chú Cho Manager Khi Duyệt & Merge (Manager Merge Notes)
- **Rủi ro**: Thấp (Low).
- **File cần review**: `src/MT_GRMS.Infrastructure/Services/EmailService.cs`.
- **Hậu merge**: Không cần chạy migration hoặc chỉnh sửa cấu hình `appsettings.json`.

