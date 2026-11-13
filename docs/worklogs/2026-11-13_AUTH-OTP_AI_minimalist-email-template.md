# Worklog: Chuẩn Hóa Mẫu Giao Diện Email OTP Theo Phong Cách Tối Giản (Minimalist NVIDIA/Apple Style)

- **Ngày thực hiện**: 2026-11-13
- **Mã Task/FE**: AUTH-OTP
- **Tác giả**: AI Assistant
- **Mức độ rủi ro**: Low (Chỉ thay đổi định dạng HTML của email, không ảnh hưởng logic xác thực hay CSDL)

---

## 1. Mục đích (Why)
- **Yêu cầu người dùng**: Chuyển đổi định dạng email gửi mã OTP từ phong cách card màu cũ (có gradient xanh đậm, khung viền xám, alert đỏ nặng nề) sang phong cách tối giản, thanh lịch, tinh tế giống mẫu email của NVIDIA / Apple / Linear.
- **Mục tiêu**:
  - Nền trắng phẳng (`#ffffff`), không bọc khung thẻ xám hay đổ bóng rườm rà.
  - Căn lề trái toàn bộ văn bản (Left-aligned) chuẩn phong cách email thông báo bảo mật hiện đại.
  - Brand title `MT-GRMS.` tối giản, font chữ hệ thống sắc nét (`-apple-system`, `Segoe UI`, `Roboto`).
  - Khối hiển thị mã OTP dạng badge/button nổi bật với tông màu thương hiệu `#0f766e`, chữ số to rõ ràng và khoảng cách ký tự (letter-spacing) thoáng đãng, dễ đọc và sao chép.
  - Chân trang (Footer) thu gọn tinh gọn với các liên kết chính sách bảo mật, hỗ trợ và bản quyền.

---

## 2. Chi Tiết Tệp Đã Sửa (What)

### `project/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/Services/EmailService.cs`
- **Sửa hàm**: `BuildOtpHtmlBody`
- **Thay đổi chi tiết**:
  - Thay thế toàn bộ layout bảng/card phức tạp trước đây bằng cấu trúc HTML tối giản, sạch sẽ.
  - Thêm thẻ `meta name='viewport'` chuẩn responsive cho thiết bị di động.
  - Loại bỏ các khối màu đỏ rực rỡ, thay bằng ghi chú bảo mật trang nhã ở phần dưới đường kẻ mờ (`border-top: 1px solid #f3f4f6`).

---

## 3. Chi Tiết Kỹ Thuật (Technical Highlights)
- **Khả năng tương thích Email Client**: Sử dụng inline CSS chuẩn, font chữ fallback native tương thích với Gmail, Outlook, Apple Mail trên cả desktop và mobile.
- **An toàn mã hóa**: Dùng `WebUtility.HtmlEncode(fullName)` để chống injection.
- **Unit Tests**: Suite `UserAuthenticationTests` trong `MT_GRMS.UnitTests` chạy thông suốt (23/23 tests Passed).

---

## 4. Kết Quả Kiểm Thử (Verification)
- **Build**: `dotnet build src/MT_GRMS.Api/MT_GRMS.Api.csproj -c Release` $\rightarrow$ **0 Error(s)**.
- **Unit Tests**:
  ```bash
  dotnet test tests/MT_GRMS.UnitTests/MT_GRMS.UnitTests.csproj -c Release --filter "FullyQualifiedName~UserAuthenticationTests"
  ```
  $\rightarrow$ **Passed! - Failed: 0, Passed: 23, Skipped: 0, Total: 23 (Duration: 261 ms)**.

---

## 5. Ghi Chú Cho Manager Khi Duyệt & Merge (Manager Merge Notes)
- **Rủi ro**: Low.
- **File cần review**: `src/MT_GRMS.Infrastructure/Services/EmailService.cs`.
