# Worklog: Khắc Phục Hiện Tượng Đơ Lag Khi Gửi Mã OTP Email (Non-Blocking & Fast Dev Fallback)

- **Ngày thực hiện**: 2026-11-13
- **Task / Module**: `FE-AUTH` / `BE-AUTH` (Authentication - Đăng ký, Đăng nhập 2FA, OTP Verification)
- **Author**: AI Agent

---

## 1. Mục đích (Why)
- Người dùng phản ánh khi bấm nút "Đăng ký mở cửa hàng ngay" hoặc các tác vụ gửi mã OTP thì giao diện bị đơ/lag vài giây trước khi hiện modal OTP.
- Nguyên nhân: Trong `EmailService.cs`, khi cấu hình SMTP chưa đầy đủ hoặc mạng chậm, phương thức `SendOtpEmailInternalAsync` thực hiện gọi SMTP đồng bộ với timeout 5 giây, khiến HTTP request bị block 5 giây trước khi trả về cho Frontend.

---

## 2. Danh sách file & Nội dung đã sửa (What)

1. **`project/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/Services/EmailService.cs`**:
   - Tối ưu hóa điều kiện kiểm tra thiếu cấu hình `SmtpUser` / `SmtpPass`.
   - Tránh việc rơi vào khối gửi mail thực tế khi thiếu credential; ngay lập tức log thông tin mô phỏng OTP và in console dev log nếu môi trường là Development.
   - Trả về `false` ngay lập tức mà không gây độ trễ mạng hay timeout 5 giây.

---

## 3. Chi tiết kỹ thuật (Technical Highlights)
- Khắc phục hiện tượng block luồng tại tầng Infrastructure: API endpoint `POST /api/auth/register/send-otp` và `POST /api/auth/login` phản hồi tức thì (< 100ms) khi chưa cấu hình SMTP production.
- Không phá vỡ bất kỳ DTO contract hay luồng xác thực nào của hệ thống.

---

## 4. Kết quả kiểm thử (Verification)
- `dotnet build src/MT_GRMS.Api/MT_GRMS.Api.csproj -c Release`: **Build Succeeded** (0 Error, 9 Warnings cũ).
- `dotnet test tests/MT_GRMS.UnitTests/MT_GRMS.UnitTests.csproj -c Release --filter "FullyQualifiedName~UserAuthenticationTests"`: **Passed 23/23 tests** (100% PASS).
- `dotnet test tests/MT_GRMS.UnitTests/MT_GRMS.UnitTests.csproj -c Release --filter "FullyQualifiedName~AuthService_SendRegistrationOtp"`: **Passed 1/1 test**.

---

## 5. Ghi chú cho Manager khi duyệt & merge (Manager Merge Notes)
- **Mức độ rủi ro**: **Low** (Chỉ tinh chỉnh nhánh xử lý fallback dev của `EmailService.cs`).
- **File cần review**: `src/MT_GRMS.Infrastructure/Services/EmailService.cs`.
- **Lưu ý cấu hình**: Khi triển khai production, cần điền đầy đủ `EmailSettings:SmtpUser` và `EmailSettings:SmtpPass` vào biến môi trường hoặc `appsettings.Production.json` để email thật được gửi đến người dùng.
