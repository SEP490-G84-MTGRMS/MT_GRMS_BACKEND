# WORKLOG: NHAT-V2-06 - Đồng bộ danh mục gói dịch vụ giữa Admin và Store Owner

- **Ngày thực hiện**: 2026-11-25
- **Người thực hiện**: Ortus24 / AI Agent
- **Nhánh Git (Branch)**: `refactor/subscription-plan-store-sync`
- **Mã tính năng liên kết**: `NHAT-V2-06` / `REQ-01` (`subscription-tier-quota-redesign`)
- **Loại thay đổi**: Bugfix

---

### 1. Mục Đích Thay Đổi (Why)
- `TenantStoreController.GetPricingPlans` trước đó lọc cứng chỉ giữ `TRIAL | STANDARD | PRO` (ẩn mọi gói mới Admin tạo) và tự chèn lại gói mặc định khi Admin chuyển `STANDARD`/`PRO` sang `INACTIVE`.
- `SubscriptionPlanService` ép cứng `MaxStorageGb = 2` cho `STANDARD` và `PRO` đè lên cấu hình Admin.
- `TenantSubscriptionService` chặn mua gói mới khác `STANDARD`/`PRO` và vẫn cho mua gói đang `IsActive == false`.

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)
- `src/MT_GRMS.Application/Admin/Services/SubscriptionPlanService.cs`: Bỏ dòng ghi đè cứng `MaxStorageGb = 2` cho `STANDARD`/`PRO` (chỉ fallback `2` khi giá trị `<= 0`).
- `src/MT_GRMS.Api/Controllers/TenantStoreController.cs`: Trả về toàn bộ gói `ACTIVE` do Admin quản lý và không tự chèn lại gói đã có trong DB nhưng đang `INACTIVE`.
- `src/MT_GRMS.Infrastructure/Services/TenantSubscriptionService.cs`: Cho phép thanh toán các gói trả phí đang `IsActive == true` trong CSDL, chặn gói `INACTIVE` (`PLAN_INACTIVE`) và gán `SubscriptionPlanId`.
- `tests/MT_GRMS.UnitTests/Controllers/TenantStoreControllerTests.cs`: Bổ sung unit test kiểm tra gói mới `ENTERPRISE`, dung lượng tùy chỉnh và ẩn gói `STANDARD` khi `INACTIVE`.

### 3. API Endpoints & Contract
- **Method & Route**: `GET /api/tenant/pricing-plans`, `POST /api/tenant/subscriptions/purchase`
- **Database Migration**: Không có.
- **Breaking Changes**: Không có.

### 4. Kiểm Thử & Xác Nhận (Verification)
- [x] Đã chạy `dotnet build MT_GRMS_BACKEND.sln -c Release`: Thành công (0 lỗi).
- [x] Đã chạy `dotnet test -c Release` cho `SubscriptionPlanTests` và `TenantStoreControllerTests`: Đạt 100%.

### 5. Ghi Chú Cho Manager Khi Duyệt & Merge (Manager Merge Notes)
- **Mức độ rủi ro**: Thấp
- **File trọng yếu cần review**: `TenantStoreController.cs`, `SubscriptionPlanService.cs`, `TenantSubscriptionService.cs`
- **Hành động sau Merge**: Khởi động lại dịch vụ API để đồng bộ danh sách gói cước.
