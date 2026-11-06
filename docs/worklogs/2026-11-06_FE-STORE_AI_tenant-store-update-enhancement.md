# Worklog: Cải Thiện API Cập Nhật Thông Tin Gian Hàng (Tenant Store Update)

- **Ngày thực hiện**: 2026-11-06
- **Mã task / Tính năng**: FE-STORE (Tenant Store Info Management)
- **Tác giả**: AI Coding Assistant
- **Mô tả ngắn**: Cải thiện API cập nhật thông tin gian hàng `PUT /api/tenant/store-info`, bổ sung cơ chế fallback `X-Tenant-Id` header và an toàn null-safety khi lưu tên chuỗi cửa hàng.

---

## 1. Mục đích (Why)
- Đảm bảo chủ gian hàng có thể cập nhật thông tin gian hàng (Tên chuỗi cửa hàng, Mã số thuế MST, Hotline CSKH, Tỉnh/Thành phố, Địa chỉ trụ sở chính) từ trang quản lý `/store-owner/subscriptions?tab=store-info`.
- Xử lý linh hoạt khi claims `tenant_id` từ JWT token chưa sẵn sàng bằng cách fallback lấy từ `X-Tenant-Id` header của middleware.
- Đảm bảo an toàn null-safety khi người dùng cập nhật thông tin, không làm văng exception nếu `request.Name` trống.

---

## 2. Danh sách tệp & Nội dung thay đổi (What)
- [`src/MT_GRMS.Api/Controllers/TenantStoreController.cs`](file:///d:/FPT_University/Final/Grade/new/project/MT_GRMS_BACKEND/src/MT_GRMS.Api/Controllers/TenantStoreController.cs):
  - Bổ sung kiểm tra fallback header `X-Tenant-Id` khi claim `tenant_id` từ token là `null` hoặc `"DEFAULT"` trong cả hai action `GetStoreInfo` và `UpdateStoreInfo`.
- [`src/MT_GRMS.Application/Admin/Services/TenantService.cs`](file:///d:/FPT_University/Final/Grade/new/project/MT_GRMS_BACKEND/src/MT_GRMS.Application/Admin/Services/TenantService.cs):
  - Bổ sung kiểm tra `!string.IsNullOrWhiteSpace(request.Name)` trước khi gán và trim `tenant.Name` để tránh lỗi `NullReferenceException`.

---

## 3. Chi tiết kỹ thuật (Technical Highlights)
- **API Endpoint**: `PUT /api/tenant/store-info`
- **Request Body**: `UpdateTenantRequest` (Name, TaxCode, Hotline, Address, City)
- **Response**: `ApiResponse<TenantDetailResponse>` trả về toàn bộ thông tin chi tiết của tenant đã được cập nhật, đồng bộ vào cơ sở dữ liệu PostgreSQL.

---

## 4. Kết quả kiểm thử (Verification)
- `dotnet build`: 0 Warning(s), 0 Error(s).
- `dotnet test`: 423/423 tests pass.

---

## 5. Ghi chú cho Manager (Manager Merge Notes)
- **Mức độ rủi ro**: Thấp (Low).
- **Lưu ý**: Thay đổi an toàn, tương thích ngược 100% với các client hiện hành.

