# Worklog: Vá lỗi RBAC và chống Account Enumeration (QA Audit cụm 15 màn hình Auth/Admin/Store)

- **Ngày:** 2026-11-24
- **Mã Task:** `QA-NHAT` (`NHAT-BR-01`, `NHAT-SUB-01`, `NHAT-AUTH-01`)
- **Người thực hiện:** AI Assistant
- **Tham chiếu báo cáo QA:** `outputs/bug/nhat/QA_Audit_Report_Nhat_15Screens.md`

## 1. Lý do thay đổi
Đợt kiểm thử live theo `QA_Team_Testing_Playbook_And_Prompt.md` phát hiện 3 vấn đề bảo mật ở tầng Controller Backend:
1. **`NHAT-BR-01` (CRITICAL):** `DELETE /api/branches/{id}` thiếu `[Authorize(Roles = RoleConstants.StoreOwnerOrAdmin)]`, cho phép nhân viên (`STAFF`) gọi trực tiếp xuống service xóa chi nhánh.
2. **`NHAT-SUB-01` (MAJOR):** `GET /api/tenant/subscriptions/history` thiếu `[Authorize(Roles = RoleConstants.StoreOwnerOrAdmin)]`, cho phép nhân viên (`STAFF`) xem toàn bộ lịch sử mua gói thuê bao của Chủ cửa hàng.
3. **`NHAT-AUTH-01` (MINOR):** `POST /api/Auth/forgot-password` trả `HTTP 404` khi email không tồn tại, gây lỗ hổng dò tài khoản (Account Enumeration theo chuẩn OWASP).

## 2. File đã sửa
- `src/MT_GRMS.Api/Controllers/BranchesController.cs`: Gắn `[Authorize(Roles = RoleConstants.StoreOwnerOrAdmin)]` lên `[HttpDelete("{id:guid}")]`.
- `src/MT_GRMS.Api/Controllers/TenantStoreController.cs`: Gắn `[Authorize(Roles = RoleConstants.StoreOwnerOrAdmin)]` lên `[HttpGet("subscriptions/history")]`.
- `src/MT_GRMS.Api/Controllers/AuthController.cs`: Trong `ForgotPassword`, trả `HTTP 200 OK` với thông báo trung tính khi bắt `KeyNotFoundException` (giữ nguyên `404` cho `VerifyOtp` và `ResetPassword`).

## 3. Kiểm chứng
- Chạy `dotnet build` và `dotnet test` trên các bộ unit test `BranchesControllerTests`, `TenantStoreControllerTests`, `AuthControllerTests`, `UserAuthenticationTests`.
