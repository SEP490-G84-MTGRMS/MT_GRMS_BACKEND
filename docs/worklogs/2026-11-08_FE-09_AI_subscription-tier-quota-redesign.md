# Worklog: Tái Cấu Trúc Gói Cước SaaS MT-GRMS (Gói Dùng Thử 0k, Gói Thường 99k & Gói Pro 249k)

- **Ngày thực hiện**: 2026-11-08
- **Tác giả**: AI Assistant (Pair Programming)
- **Mã Task / Change**: FE-09 / `subscription-tier-quota-redesign`

---

## 1. Mục Đích (Why)
- Bổ sung thẻ riêng cho gói dùng thử: **Gói Dùng Thử (TRIAL)** nằm phía bên trái ngoài cùng (3 ngày trải nghiệm miễn phí 0 đ).
- Chuẩn hóa danh mục gói cước thành 3 thẻ đối xứng:
  1. **Gói Dùng Thử (TRIAL)**: 0 đ / 3 ngày, 1 chi nhánh kiêm kho, mở sẵn quản lý nhân viên trải nghiệm cho tài khoản mới.
  2. **Gói Thường (STANDARD)**: 99.000 đ/tháng, 1 chi nhánh kiêm kho, 1 tài khoản chủ shop. Option Quản lý & Phân quyền nhân viên +30.000 đ/tháng.
  3. **Gói Pro (PRO)**: 249.000 đ/tháng, mặc định sẵn 2 chi nhánh kiêm kho, full tính năng nhân viên không giới hạn. Mua thêm chi nhánh: +80.000 đ/chi nhánh/tháng.
- Đồng bộ công thức tính Quota chi nhánh động: `MaxBranches = Plan.MaxBranches + Tenant.ExtraBranches`.

---

## 2. Danh Sách File & Nội Dung Đã Thay Đổi (What)

1. `src/MT_GRMS.Domain/Entities/SaaS/Tenant.cs`:
   - Bổ sung các thuộc tính: `ExtraBranches` (int, default 0), `HasStaffManagementAddon` (bool, default false), `SubscriptionStatus` (string, default "TRIAL").
2. `src/MT_GRMS.Infrastructure/Persistence/Configurations/SaaS/TenantConfiguration.cs`:
   - Cấu hình EF Core mapping cho các cột mới của Entity Tenant.
3. `src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`:
   - Bổ sung hàm `EnsureTenantSubscriptionColumnsAsync` tự động DDL schema idempotent (`ALTER TABLE "Tenants" ADD COLUMN IF NOT EXISTS...`) và vô hiệu hóa tất cả gói cước cũ (`UPDATE "SubscriptionPlans" SET "IsActive" = false WHERE "PlanCode" NOT IN ('TRIAL', 'STANDARD', 'PRO')`).
   - Cập nhật Seed Data: Nạp 3 gói `TRIAL` (0 đ), `STANDARD` (99.000 đ) và `PRO` (249.000 đ), đánh dấu vô hiệu hóa các gói cũ (`FREE`, `SUPPORT`, `PREMIUM`...).
4. `src/MT_GRMS.Application/Admin/Dtos/TenantDtos.cs`:
   - Cập nhật record `PurchaseSubscriptionRequest`: thêm `bool HasStaffManagementAddon = false`, giữ `ExtraWarehouses = 0` làm optional parameter.
5. `src/MT_GRMS.Api/Controllers/TenantStoreController.cs`:
   - Cập nhật action `GetPricingPlans`: Trả về đúng 3 gói cước chuẩn sắp xếp theo thứ tự `TRIAL` (1) -> `STANDARD` (2) -> `PRO` (3), loại bỏ hoàn toàn các gói cũ và tự động fallback tạo cấu trúc 3 gói mặc định nếu CSDL trống.
   - Nâng cấp action `PurchaseSubscription`:
     - Chuẩn hóa mã gói và tự động nạp `SubscriptionPlan` nếu chưa có trong DB.
     - Bổ sung luồng Dùng thử 3 ngày miễn phí (`isTrial` khi `Months == 0` hoặc `PaymentMethod == "TRIAL"` hoặc `PlanCode == "TRIAL"`): set thời hạn dùng thử 3 ngày (`DateTime.UtcNow.AddDays(3)`), mở khóa toàn bộ tính năng trải nghiệm (`CurrentPlan = "PRO"`, `SubscriptionStatus = "TRIAL"`).
     - Validate: Gói Thường không cho phép mua thêm chi nhánh.
     - Tính tiền: Gói Thường 99k/tháng (option NV +30k/tháng), Gói Pro 249k/tháng (chi nhánh thêm +80k/tháng), tính VAT 10%.
     - Kích hoạt gói: cập nhật `CurrentPlan`, `SubscriptionExpiresAt`, `SubscriptionStatus = "ACTIVE"`, cộng dồn `ExtraBranches` và bật `HasStaffManagementAddon`.
   - Thêm action `ActivateTrialSubscription` (`POST /api/tenant/subscriptions/trial`): hỗ trợ kích hoạt nhanh 3 ngày dùng thử miễn phí.
6. `src/MT_GRMS.Application/Auth/AuthService.cs`:
   - Khi tài khoản mới đăng ký thành công: tự động cấp quyền Dùng thử 3 ngày Full tính năng (`CurrentPlan = "PRO"`, `SubscriptionStatus = "TRIAL"`, `SubscriptionExpiresAt = DateTime.UtcNow.AddDays(3)`).
7. `src/MT_GRMS.Application/Branches/BranchService.cs`:
   - Cập nhật `GetBranchQuotaAsync`: tính hạn mức tối đa linh hoạt theo `Plan.MaxBranches + Tenant.ExtraBranches`.
   - **Khắc phục lỗ hổng bypass Quota**: Sửa `GetBranchQuotaAsync` chuyển từ đếm chi nhánh active sang đếm toàn bộ chi nhánh sở hữu (`_branchRepo.CountTotalAsync(!b.IsDeleted)`). Khắc phục triệt để lỗi tắt chi nhánh để tạo thêm chi nhánh mới không giới hạn.
   - **Chốt chặn an toàn tại `ToggleBranchStatusAsync`**: Kiểm tra Quota khi người dùng bật lại chi nhánh (`request.IsActive && !branch.IsActive`), ném ngoại lệ `QUOTA_EXCEEDED` nếu tổng số chi nhánh đang vượt quá hạn mức gói cước.
8. `src/MT_GRMS.Application/Branches/IBranchRepository.cs` & `src/MT_GRMS.Infrastructure/Repositories/BranchRepository.cs`:
   - Bổ sung hàm `CountTotalAsync(CancellationToken ct)` đếm tổng số chi nhánh thuộc tenant chưa bị xóa (`!b.IsDeleted`).

---

## 3. Chi Tiết Kỹ Thuật (Technical Highlights)
- **API Endpoints**:
  - `POST /api/tenant/subscriptions/purchase`: Request nhận `PlanCode`, `Months`, `ExtraBranches`, `HasStaffManagementAddon`, `PaymentMethod`.
  - `POST /api/tenant/subscriptions/trial`: Kích hoạt 3 ngày dùng thử miễn phí.
  - `GET /api/tenant/pricing-plans`: Lọc chuẩn xác 3 gói `TRIAL`, `STANDARD` và `PRO`.
  - `GET /api/branches/quota`: Trả về hạn mức chính xác theo `MaxBranches + ExtraBranches`.
- **Database Schema**:
  - `Tenants`: thêm cột `ExtraBranches` (integer), `HasStaffManagementAddon` (boolean), `SubscriptionStatus` (varchar(30)).

---

## 4. Kết Quả Kiểm Thử (Verification)
- `dotnet build project/MT_GRMS_BACKEND/src/MT_GRMS.Api/MT_GRMS.Api.csproj` -> Build Succeeded (0 Errors).

---

## 5. Ghi Chú Cho Manager Khi Duyệt & Merge (Manager Merge Notes)
- **Mức độ rủi ro**: Thấp (Low).
- **Thao tác hậu merge**: Khởi động backend API, `DbInitializer` sẽ tự động áp dụng DDL bổ sung cột và nạp seed data 3 gói mới mà không cần can thiệp thủ công vào database.
