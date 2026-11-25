# WORKLOG: NHAT-V2-07 - Sửa lỗi KPI Lượt mua gói thành công trên Admin Dashboard luôn hiển thị 0

- **Ngày thực hiện**: 2026-11-25
- **Người thực hiện**: Ortus24 / AI Agent
- **Nhánh Git (Branch)**: `refactor/admin-dashboard-purchase-kpi`
- **Mã tính năng liên kết**: `NHAT-V2-07` (`fe-master-admin-integration`)
- **Loại thay đổi**: Bugfix

---

### 1. Mục Đích Thay Đổi (Why)
- `TenantRepository.GetSuccessfulUpgradeCountAsync` trước đó chỉ đếm `Type == "UPGRADE_PLAN"`, bỏ sót toàn bộ giao dịch mua mới và gia hạn gói lưu `Type == "PURCHASE_PLAN"`, khiến thẻ KPI "Lượt mua gói thành công" luôn trả về `0`.

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)
- `src/MT_GRMS.Infrastructure/Repositories/TenantRepository.cs`: Đếm các giao dịch `!IsDeleted`, `Status == "SUCCESS"` thuộc `PURCHASE_PLAN` hoặc `UPGRADE_PLAN` (không tính `TOPUP` và `PURCHASE_ADDON`).
- `tests/MT_GRMS.UnitTests/AdminDashboardServiceTests.cs`: Bổ sung test `TenantRepository_GetSuccessfulUpgradeCountAsync_CountsPurchaseAndUpgradePlanOnly`.

### 3. API Endpoints & Contract
- **Method & Route**: `GET /api/admin/dashboard/kpis`
- **Database Migration**: Không có.
- **Breaking Changes**: Không có.

### 4. Kiểm Thử & Xác Nhận (Verification)
- [x] Đã chạy `dotnet build MT_GRMS_BACKEND.sln -c Release`: Thành công (0 lỗi).
- [x] Đã chạy `dotnet test -c Release` cho `AdminDashboardServiceTests`: Đạt 100%.

### 5. Ghi Chú Cho Manager Khi Duyệt & Merge (Manager Merge Notes)
- **Mức độ rủi ro**: Thấp
- **File trọng yếu cần review**: `TenantRepository.cs`
- **Hành động sau Merge**: Khởi động lại dịch vụ API.
