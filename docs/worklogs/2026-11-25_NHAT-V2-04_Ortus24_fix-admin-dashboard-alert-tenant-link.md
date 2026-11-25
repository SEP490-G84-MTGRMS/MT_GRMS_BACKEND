# WORKLOG: NHAT-V2-04 - Sửa lỗi điều hướng 404 bảng Khách hàng cần chú ý trên Admin Dashboard

- **Ngày thực hiện**: 2026-11-25
- **Người thực hiện**: Ortus24 / AI Agent
- **Nhánh Git (Branch)**: `refactor/admin-dashboard-tenant-alert-link`
- **Mã tính năng liên kết**: `NHAT-V2-04` (`fe-master-admin-integration`)
- **Loại thay đổi**: Bugfix

---

### 1. Mục Đích Thay Đổi (Why)
- Trước đó `UrgentAlertResponse` chỉ trả `Subdomain` (không trả `Id`), khiến `AdminDashboardPage` tạo liên kết `/admin/tenants/{subdomain}` bị lỗi 404 khi gọi `GetByIdWithDetailsAsync` (vốn chỉ lọc `t.Id == id`).

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)
- `src/MT_GRMS.Application/Admin/Dtos/DashboardDtos.cs`: Bổ sung trường `Id` vào `UrgentAlertResponse`.
- `src/MT_GRMS.Application/Admin/Services/AdminDashboardService.cs`: Truyền `Id: t.Id` trong `GetUrgentAlertsAsync`.
- `src/MT_GRMS.Infrastructure/Repositories/TenantRepository.cs`: Hỗ trợ tra cứu `GetByIdWithDetailsAsync` theo cả `Id` và `Subdomain` để tương thích ngược các đường dẫn cũ.
- `tests/MT_GRMS.UnitTests/AdminDashboardServiceTests.cs`, `tests/MT_GRMS.UnitTests/Admin/AdminDashboardApiControllerTests.cs`: Cập nhật và bổ sung assert kiểm tra trường `Id`.

### 3. API Endpoints & Contract
- **Method & Route**: `GET /api/admin/dashboard/urgent-alerts`
- **Response Data (DTO)**: `UrgentAlertResponse` bổ sung thuộc tính `id`.
- **Database Migration**: Không có.
- **Breaking Changes**: Không có (chỉ mở rộng thêm trường `id` trong JSON).

### 4. Kiểm Thử & Xác Nhận (Verification)
- [x] Đã chạy `dotnet build MT_GRMS_BACKEND.sln -c Release`: Thành công (0 lỗi).
- [x] Đã chạy `dotnet test -c Release` cho `AdminDashboardServiceTests` & `AdminDashboardApiControllerTests`: Đạt 100%.

### 5. Ghi Chú Cho Manager Khi Duyệt & Merge (Manager Merge Notes)
- **Mức độ rủi ro**: Thấp
- **File trọng yếu cần review**: `AdminDashboardService.cs`, `TenantRepository.cs`
- **Hành động sau Merge**: Khởi động lại dịch vụ API để áp dụng trường `id` mới trên Dashboard.
