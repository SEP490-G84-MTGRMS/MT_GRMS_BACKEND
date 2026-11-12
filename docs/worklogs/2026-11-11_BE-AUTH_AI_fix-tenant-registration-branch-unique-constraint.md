# Worklog: Sửa lỗi vi phạm ràng buộc duy nhất khi xác thực mã OTP đăng ký mở cửa hàng

**Ngày thực hiện**: 2026-11-11  
**Mã Task**: `BE-AUTH`  
**Tác giả**: `AI Agent (Antigravity)`  
**Dự án**: `MT_GRMS_BACKEND`  

---

## 1. Bối cảnh & Nguyên nhân gốc (Root Cause Analysis)

- **Hiện tượng**: Khi người dùng nhập đúng mã Email OTP 6 chữ số để xác nhận đăng ký mở cửa hàng mới, giao diện hiển thị thông báo lỗi:
  `"Dữ liệu bị trùng lặp trong hệ thống (vi phạm ràng buộc duy nhất)."`
- **Truy vết lỗi**:
  - Tại PostgreSQL Docker container, log báo lỗi:
    `ERROR: duplicate key value violates unique constraint "IX_Branches_TenantID_BranchCode"`
    `DETAIL: Key ("TenantID", "BranchCode")=(tenant_mt_mart_01, BR-01) already exists.`
  - Trong luồng xử lý của `MultiTenantMiddleware`, đối với các request chưa đăng nhập (anonymous) như `/api/auth/register`, middleware tự động gán fallback tenant là `tenant_mt_mart_01`.
  - Khi phương thức `RegisterStoreOwnerAsync` tạo chi nhánh mặc định `BR-01` cho gian hàng mới với `branch.TenantId = tenantId` (ví dụ `tenant_123123zcc_...`), tại `AppDbContext.ApplyAuditAndTenantInfo()`, logic cũ đã tự động ghi đè:
    ```csharp
    if (!string.IsNullOrWhiteSpace(tenantId))
    {
        entry.Entity.TenantId = tenantId; // Đè thành "tenant_mt_mart_01"!
    }
    ```
  - Kết quả là `branch.TenantId` bị ép thành `tenant_mt_mart_01`, trong khi `tenant_mt_mart_01` đã có chi nhánh mang mã `BR-01`. Do đó phát sinh lỗi `23505 Duplicate Key` trên bảng `Branches`.
  - Ngoài ra, `UserRepository.ExistsByPhoneOrEmailAsync` chưa kiểm tra `u.UserName` trùng số điện thoại và chưa dùng `IgnoreQueryFilters()` cho các bản ghi bị soft-delete.

---

## 2. Giải pháp kỹ thuật đã triển khai

1. **`AppDbContext.cs`**:
   - Tinh chỉnh `ApplyAuditAndTenantInfo()`: Không tự ý ghi đè `TenantId` nếu thực thể đã có sẵn `TenantId` hợp lệ, trừ trường hợp người dùng nội bộ đã xác thực (CurrentUserId != Guid.Empty) thao tác để ngăn chặn IDOR.
2. **`MultiTenantMiddleware.cs`**:
   - Bỏ qua việc ép fallback tenant `tenant_mt_mart_01` đối với endpoint đăng ký `/api/auth/register` để tránh làm sai lệch ngữ cảnh gian hàng mới.
3. **`UserRepository.cs`**:
   - Bổ sung `IgnoreQueryFilters()` và kiểm tra đồng thời cả `u.UserName`, `u.Phone` và `u.Email` trong `ExistsByPhoneOrEmailAsync`.
4. **`ExceptionHandlingMiddleware.cs`**:
   - Bổ sung mapping thông điệp thân thiện theo `pgEx.ConstraintName` thay vì thông báo lỗi database chung chung.

---

## 3. Kết quả kiểm thử

- `dotnet test MT_GRMS_BACKEND\tests\MT_GRMS.UnitTests` -> **20/20 Passed (100%)**.
- `dotnet test MT_GRMS_BACKEND\tests\MT_GRMS.IntegrationTests` -> **7/7 Passed (100%)**.
- Health check `GET /api/health` -> HTTP 200 OK.
