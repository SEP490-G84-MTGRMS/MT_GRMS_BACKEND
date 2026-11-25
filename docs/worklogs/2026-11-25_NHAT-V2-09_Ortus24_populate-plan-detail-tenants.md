# WORKLOG: NHAT-V2-09 - Bổ sung danh sách khách hàng đăng ký gói dịch vụ trong PlanDetailResponse

- **Ngày thực hiện**: 2026-11-25
- **Người thực hiện**: Ortus24 / AI Agent
- **Nhánh Git (Branch)**: `refactor/admin-plan-and-tenant-details`
- **Mã tính năng liên kết**: `NHAT-V2-09` (`fe-master-admin-integration`)
- **Loại thay đổi**: Bugfix

---

### 1. Mục Đích Thay Đổi (Why)
- `SubscriptionPlanService.GetByIdAsync` trước đó trả cứng `Tenants: []` khiến trang `/admin/plans/:id` luôn báo "Chưa có khách hàng nào đăng ký gói này" dù `ActiveTenantCount > 0`.

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)
- `src/MT_GRMS.Application/Admin/Dtos/PlanDtos.cs`: Bổ sung trường `Status` cho `PlanTenantSummaryDto`.
- `src/MT_GRMS.Application/Admin/Repositories/ISubscriptionPlanRepository.cs`, `src/MT_GRMS.Infrastructure/Repositories/SubscriptionPlanRepository.cs`: Cài đặt `GetTenantsByPlanIdAsync` truy vấn danh sách tenant đang gắn gói kèm số chi nhánh, nhân sự và trạng thái thực tế.
- `src/MT_GRMS.Application/Admin/Services/SubscriptionPlanService.cs`: Nạp `Tenants` thực tế trong `GetByIdAsync`.
- `tests/MT_GRMS.UnitTests/Admin/SubscriptionPlanTests.cs`: Bổ sung test `SubscriptionPlanRepository_GetTenantsByPlanIdAsync_ReturnsMatchingTenantsWithStatus`.

### 3. API Endpoints & Contract
- **Method & Route**: `GET /api/admin/plans/{id}`
- **Database Migration**: Không có.
- **Breaking Changes**: Không có (`TenantService.GetByIdAsync` vẫn giữ ranh giới bảo mật không lộ `Branches` nội bộ theo `AdminTenantRecordsTests`).

### 4. Kiểm Thử & Xác Nhận (Verification)
- [x] Đã chạy `dotnet build MT_GRMS_BACKEND.sln -c Release`: Thành công (0 lỗi).
- [x] Đã chạy `dotnet test -c Release` cho `SubscriptionPlanTests` và `AdminTenantRecordsTests`: Đạt 100%.

### 5. Ghi Chú Cho Manager Khi Duyệt & Merge (Manager Merge Notes)
- **Mức độ rủi ro**: Thấp
- **File trọng yếu cần review**: `SubscriptionPlanRepository.cs`, `SubscriptionPlanService.cs`
- **Hành động sau Merge**: Khởi động lại dịch vụ API.
