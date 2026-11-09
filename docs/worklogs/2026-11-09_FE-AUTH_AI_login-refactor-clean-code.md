# Worklog: Tối Giản Hóa & Tái Cấu Trúc LoginAsync (Clean Code & Role Enum)

- **Ngày thực hiện**: 2026-11-09
- **Mã Task/Tính năng**: FE-AUTH / Refactor LoginAsync
- **Tác giả**: AI Assistant (Pair Programming với User)
- **Dự án**: `project/MT_GRMS_BACKEND`

---

## 1. Mục Đích (Why)
- Đáp ứng yêu cầu của User: Loại bỏ logic rườm rà, loại bỏ triệt để hơn 30 dòng `if...else` soi chuỗi username/email tạm bợ.
- Rút gọn hàm `LoginAsync` từ gần 140 dòng xuống chỉ còn 40 dòng trong sáng, dễ đọc và tuân thủ nguyên lý Single Responsibility Principle (SRP).
- Tách các đoạn logic chuyên biệt ra 3 hàm private chuyên trách:
  1. `VerifyPasswordAsync(user, requestPassword)`
  2. `ResolveTenant(user, tenantCode)` / `ResolveTenantId(user, tenantCode)`
  3. `ResolveUserRole(user, tenantUser)`

---

## 2. Danh Sách Tệp & Nội Dung Thay Đổi (What)
1. **`src/MT_GRMS.Application/Auth/AuthService.cs`**:
   - Tinh gọn `LoginAsync`: Chia rõ ràng 5 bước: (1) Validation đầu vào $\rightarrow$ (2) Lấy User $\rightarrow$ (3) `VerifyPasswordAsync` $\rightarrow$ (4) `ResolveTenant` & `ResolveUserRole` $\rightarrow$ (5) Sinh Tokens và cập nhật Session CSDL.
   - Thêm `VerifyPasswordAsync`: Gom toàn bộ việc xác thực mật khẩu PBKDF2, SHA256 fallback, Plaintext fallback, Demo backdoor và tự động nâng cấp hash.
   - Thêm `ResolveTenant`: Chuẩn hóa logic trích xuất `TenantId` và `TenantUser` liên kết.
   - Thêm `ResolveUserRole`: Xác định vai trò chuẩn hóa dựa trên `UserRole` enum (`Admin`, `StoreOwner`, `Staff`), xóa sạch logic `uName.Contains("NHANVIEN")` cũ.
   - Tái sử dụng `ResolveTenant` và `ResolveUserRole` tại `RefreshTokenAsync` và `GetCurrentUserAsync` để đảm bảo DRY 100%.

---

## 3. Kết Quả Kiểm Thử (Verification)
- `dotnet build`: **0 Error(s)**.
- `dotnet test tests/MT_GRMS.IntegrationTests/`: **47/47 tests PASS (100%)**.
- Kiểm tra thực tế: API `POST /api/auth/login` chạy mượt mà với tài khoản Chủ cửa hàng (`StoreOwner`), Quản trị viên (`Admin`), và Nhân viên (`Staff`).

---

## 4. Ghi Chú Cho Manager Khi Duyệt & Merge (Manager Merge Notes)
- **Mức độ rủi ro**: Thấp (Low).
- Tái cấu trúc nội bộ hoàn toàn tương thích ngược với API contracts, không làm thay đổi response schema.
