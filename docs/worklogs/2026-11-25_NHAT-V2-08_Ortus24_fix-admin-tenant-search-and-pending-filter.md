# WORKLOG: NHAT-V2-08 - Bổ sung tìm kiếm Email/Tên chủ tiệm và bộ lọc trạng thái PENDING cho Quản lý Tenant

- **Ngày thực hiện**: 2026-11-25
- **Người thực hiện**: Ortus24 / AI Agent
- **Nhánh Git (Branch)**: `refactor/admin-tenant-search-filter-export`
- **Mã tính năng liên kết**: `NHAT-V2-08` (`fe-master-admin-integration`)
- **Loại thay đổi**: Bugfix

---

### 1. Mục Đích Thay Đổi (Why)
- `TenantRepository.GetPagedAsync` trước đó chưa tìm kiếm `keyword` trên `Email`/`FullName`/`Phone` của chủ tiệm và thiếu nhánh lọc trạng thái `PENDING` / `NO_PLAN`.

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)
- `src/MT_GRMS.Infrastructure/Repositories/TenantRepository.cs`: Mở rộng tìm kiếm từ khóa sang `TenantUsers.User` (`Email`, `FullName`, `Phone`) và thêm `case "PENDING" / "NO_PLAN"`.
- `src/MT_GRMS.Application/Admin/Services/TenantService.cs`: Chuẩn hóa `NO_PLAN` / `PENDING` về trạng thái `"PENDING"` trong `ComputeStatus`.
- `tests/MT_GRMS.UnitTests/Admin/TenantManagementTests.cs`: Bổ sung test `TenantRepository_GetPagedAsync_SearchesOwnerEmailAndFiltersPending`.

### 3. API Endpoints & Contract
- **Method & Route**: `GET /api/admin/tenants`
- **Database Migration**: Không có.
- **Breaking Changes**: Không có.

### 4. Kiểm Thử & Xác Nhận (Verification)
- [x] Đã chạy `dotnet build MT_GRMS_BACKEND.sln -c Release`: Thành công (0 lỗi).
- [x] Đã chạy `dotnet test -c Release` cho `TenantManagementTests`: Đạt 100%.

### 5. Ghi Chú Cho Manager Khi Duyệt & Merge (Manager Merge Notes)
- **Mức độ rủi ro**: Thấp
- **File trọng yếu cần review**: `TenantRepository.cs`, `TenantService.cs`
- **Hành động sau Merge**: Khởi động lại dịch vụ API.
