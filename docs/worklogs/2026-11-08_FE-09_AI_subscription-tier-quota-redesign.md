# Worklog: Tái Cấu Trúc Gói Cước SaaS MT-GRMS (Gói Thường 99k & Gói Pro 249k)

- **Ngày thực hiện**: 2026-11-08
- **Tác giả**: AI Assistant (Pair Programming)
- **Mã Task / Change**: FE-09 / `subscription-tier-quota-redesign`

---

## 1. Mục Đích (Why)
- Xóa bỏ gói miễn phí vĩnh viễn (0đ), áp dụng cơ chế Dùng thử 3 ngày Full tính năng Gói Pro.
- Loại bỏ hoàn toàn khái niệm "Kho phụ" vì mỗi chi nhánh vật lý vừa là điểm bán POS vừa là kho lưu trữ hàng hóa tại chỗ.
- Chuẩn hóa danh mục gói cước thành 2 gói:
  - **Gói Thường (STANDARD)**: 99.000 đ/tháng, 1 chi nhánh kiêm kho, 1 tài khoản chủ shop. Option Quản lý & Phân quyền nhân viên +30.000 đ/tháng.
  - **Gói Pro (PRO)**: 249.000 đ/tháng, sẵn 2 chi nhánh kiêm kho, full tính năng nhân viên không giới hạn. Mua thêm chi nhánh: +80.000 đ/chi nhánh/tháng.
- Đồng bộ công thức tính Quota chi nhánh động: `MaxBranches = Plan.MaxBranches + Tenant.ExtraBranches`.

---

## 2. Danh Sách File & Nội Dung Đã Thay Đổi (What)

1. `src/MT_GRMS.Domain/Entities/SaaS/Tenant.cs`:
   - Bổ sung các thuộc tính: `ExtraBranches` (int, default 0), `HasStaffManagementAddon` (bool, default false), `SubscriptionStatus` (string, default "TRIAL").
2. `src/MT_GRMS.Infrastructure/Persistence/Configurations/SaaS/TenantConfiguration.cs`:
   - Cấu hình EF Core mapping cho các cột mới của Entity Tenant.
3. `src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`:
   - Bổ sung hàm `EnsureTenantSubscriptionColumnsAsync` tự động DDL schema idempotent (`ALTER TABLE "Tenants" ADD COLUMN IF NOT EXISTS...`) và vô hiệu hóa tất cả gói cước cũ (`UPDATE "SubscriptionPlans" SET "IsActive" = false WHERE "PlanCode" NOT IN ('STANDARD', 'PRO')`).
   - Cập nhật Seed Data: Nạp 2 gói `STANDARD` (99.000 đ) và `PRO` (249.000 đ), đánh dấu vô hiệu hóa các gói cũ (`FREE`, `SUPPORT`, `PREMIUM`...) và đồng bộ các tenant cũ sang gói chuẩn.
4. `src/MT_GRMS.Application/Admin/Dtos/TenantDtos.cs`:
   - Cập nhật record `PurchaseSubscriptionRequest`: thêm `bool HasStaffManagementAddon = false`, giữ `ExtraWarehouses = 0` làm optional parameter.
5. `src/MT_GRMS.Api/Controllers/TenantStoreController.cs`:
   - Cập nhật action `GetPricingPlans`: Lọc nghiêm ngặt chỉ trả về duy nhất 2 gói cước chuẩn (`STANDARD` và `PRO`), loại bỏ hoàn toàn các gói cũ và tự động fallback tạo cấu trúc gói mặc định nếu CSDL trống.
   - Nâng cấp action `PurchaseSubscription`:
     - Chuẩn hóa mã gói và tự động nạp `SubscriptionPlan` nếu chưa có trong DB.
     - Validate: Gói Thường không cho phép mua thêm chi nhánh.
     - Tính tiền: Gói Thường 99k/tháng (option NV +30k/tháng), Gói Pro 249k/tháng (chi nhánh thêm +80k/tháng), tính VAT 10%.
     - Kích hoạt gói: cập nhật `CurrentPlan`, `SubscriptionExpiresAt`, `SubscriptionStatus = "ACTIVE"`, cộng dồn `ExtraBranches` và bật `HasStaffManagementAddon`.
6. `src/MT_GRMS.Application/Branches/BranchService.cs`:
   - Cập nhật `GetBranchQuotaAsync`: tính hạn mức tối đa linh hoạt theo `Plan.MaxBranches + Tenant.ExtraBranches`.

---

## 3. Chi Tiết Kỹ Thuật (Technical Highlights)
- **API Endpoints**:
  - `POST /api/tenant/subscriptions/purchase`: Request nhận `PlanCode`, `Months`, `ExtraBranches`, `HasStaffManagementAddon`, `PaymentMethod`.
  - `GET /api/branches/quota`: Trả về hạn mức chính xác theo `MaxBranches + ExtraBranches`.
- **Database Schema**:
  - `Tenants`: thêm cột `ExtraBranches` (integer), `HasStaffManagementAddon` (boolean), `SubscriptionStatus` (varchar(30)).

---

## 4. Kết Quả Kiểm Thử (Verification)
- `dotnet build project/MT_GRMS_BACKEND/src/MT_GRMS.Api/MT_GRMS.Api.csproj` -> Build Succeeded (0 Errors).

---

## 5. Ghi Chú Cho Manager Khi Duyệt & Merge (Manager Merge Notes)
- **Mức độ rủi ro**: Thấp (Low).
- **Thao tác hậu merge**: Khởi động backend API, `DbInitializer` sẽ tự động áp dụng DDL bổ sung cột và nạp seed data 2 gói mới mà không cần can thiệp thủ công vào database.

