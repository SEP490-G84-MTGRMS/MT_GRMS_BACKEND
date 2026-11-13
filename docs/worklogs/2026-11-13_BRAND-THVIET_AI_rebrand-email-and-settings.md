# Worklog: Cập Nhật Nhận Diện Thương Hiệu THViet Cho Phân Hệ Email & Cấu Hình

- **Ngày thực hiện**: 2026-11-13
- **Mã Task/FE**: BRAND-THVIET
- **Tác giả**: AI Assistant
- **Mức độ rủi ro**: Low (Chỉ cập nhật tên thương hiệu và nội dung mẫu email gửi người dùng)

---

## 1. Mục đích (Why)
- Đồng bộ nhận diện thương hiệu mới của dự án từ tên cũ **MT-GRMS** sang thương hiệu chính thức: **THViet - QUẢN LÝ TẠP HÓA VIỆT** theo đúng Brand Guidelines (bảng màu `#0F766E`, `#14B8A6`, `#0F172A`, `#DC2626`).
- Đảm bảo toàn bộ email giao dịch gửi người dùng (Email OTP đăng nhập 2FA, OTP kích hoạt cửa hàng, OTP khôi phục mật khẩu) hiển thị tên gửi, tiêu đề email và header logo chuẩn xác theo nhận diện THViet.

---

## 2. Chi Tiết Tệp Đã Sửa (What)

### 1. `project/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/Services/EmailService.cs`
- Cập nhật tiền tố tiêu đề email từ `[MT-GRMS]` sang `[THViet]`.
- Cập nhật mô tả nội dung: `"...trên nền tảng THViet..."`.
- Cập nhật khối Brand Header trong HTML email tối giản:
  ```html
  <span style='font-size:30px;font-weight:900;letter-spacing:-0.5px;color:#0F766E;'>TH</span><span style='font-size:30px;font-weight:900;color:#14B8A6;'>Viet</span>
  <div style='font-size:12px;font-weight:700;color:#0F172A;letter-spacing:2px;margin-top:2px;text-transform:uppercase;'>QUẢN LÝ TẠP HÓA VIỆT</div>
  ```
- Nâng cấp khối mã OTP cỡ lớn nổi bật (`font-size: 38px`, `font-weight: 800`, `letter-spacing: 10px`, màu nền xanh thương hiệu `#0F766E`, chữ trắng tương phản `#ffffff`, bo tròn 8px kèm đổ bóng).
- Cập nhật chân trang bản quyền: `© THViet Corporation. All rights reserved.` và `Hệ thống Quản lý Tạp hóa Việt - THViet`.

### 2. `project/MT_GRMS_BACKEND/.env` & `appsettings.json` & `EmailSettings.cs`
- Cập nhật cấu hình người gửi mặc định `EmailSettings__SenderName` và `SenderName` sang `"THViet - Quản Lý Tạp Hóa Việt"`.
- Cập nhật `SenderEmail` mặc định sang `noreply.thviet@gmail.com`.
- Cập nhật `appsettings.Development.json` `Smtp:FromName` sang `"THViet - Quản Lý Tạp Hóa Việt"`.
- Cập nhật fallback trong `SmtpSupplierOrderEmailSender.cs` sang `"THViet"`.

---

## 3. Chi Tiết Kỹ Thuật (Technical Highlights)
- Giữ nguyên API contract và logic gửi nền non-blocking.
- Toàn bộ 23/23 tests `UserAuthenticationTests` pass 100%.

---

## 4. Kết Quả Kiểm Thử (Verification)
- `dotnet build src/MT_GRMS.Api/MT_GRMS.Api.csproj -c Release` $\rightarrow$ **Succeeded (0 Errors)**.
- `dotnet test tests/MT_GRMS.UnitTests/MT_GRMS.UnitTests.csproj -c Release --filter "FullyQualifiedName~UserAuthenticationTests"` $\rightarrow$ **23 Passed (0 Failed)**.

---

## 5. Ghi Chú Cho Manager Khi Duyệt & Merge (Manager Merge Notes)
- Rủi ro: Thấp (Low).
- Hậu merge: Không cần cấu hình lại database.

