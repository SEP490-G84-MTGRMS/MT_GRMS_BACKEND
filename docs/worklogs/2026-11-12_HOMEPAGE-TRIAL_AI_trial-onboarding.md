# Worklog — Homepage Trial Onboarding

## Mục tiêu

Tách đăng ký thường khỏi Trial: đăng ký thường tạo tenant `NO_PLAN`; CTA “Dùng thử” mới kích hoạt Trial đúng 72 giờ và mỗi account/tenant chỉ được dùng một lần.

## Thay đổi chính

- Thêm `TrialConsumedAt`, `TrialStartedAt`, `TrialExpiresAt`, `FirstPaidAt` và normalized identity (kèm filtered unique index `NormalizedPhone`).
- Thiết kế OTP challenge phân tán (`Pending -> Reserved -> Consumed`) gắn `NormalizedEmail`, intent, request fingerprint, TTL/counter, fail-closed ở Production và trả reservation về `Pending` khi rollback.
- Chuẩn hóa `IdempotencyFilter` scope theo actor + operation + tenant/challenge (kể cả anonymous `/api/auth/register` với `IgnoreQueryFilters()`), trả RFC 7807 `ProblemDetails` có `code` và `traceId`.
- Thêm `trial-status`, activation nguyên tử (`Users` trước `Tenants`, `Serializable` + conditional `ExecuteUpdateAsync` + `AuditLog` `TRIAL_ACTIVATED`), kiểm tra JWT tenant membership và chặn bịa lịch sử Trial cho `NO_PLAN`.
- Thêm `SubscriptionAccessMiddleware` chặn `NO_PLAN`/`EXPIRED`/`REVIEW_REQUIRED`, Trial hết hạn và gói trả phí đã hết hạn (`ACTIVE` có `SubscriptionExpiresAt <= UtcNow`) bằng 403 `ProblemDetails` (`SUBSCRIPTION_REQUIRED`); bổ sung endpoint `GET /api/tenant/subscriptions/access-status` cho mọi vai trò active trong tenant và chặn hạ cấp từ `PRO` xuống `STANDARD` (`400 DOWNGRADE_NOT_ALLOWED`).
- Thêm `TrialOnboardingBackfillService` hỗ trợ dry-run/backfill deterministic cho Trial, Paid, Trial+Paid và `REVIEW_REQUIRED` (báo cáo chỉ chứa ID/masked ID).
- Sửa `DbInitializer` không ghi đè `CurrentPlan = "PRO"` lên tenant `NO_PLAN`/`REVIEW_REQUIRED`/`TRIAL`/`EXPIRED` khi khởi động và làm sạch bản ghi `NO_PLAN` bị gán nhầm `CurrentPlan`; đồng thời cập nhật `TenantService` luôn trả `PlanName = "Chưa kích hoạt"` và `CurrentPlan = null` khi `SubscriptionStatus == "NO_PLAN"`.
- HomePage CTA (`CtaBanner`, `HomePage`) dẫn thẳng tới `/auth/register?intent=trial` (3 ngày, không cần thẻ, không tự động gia hạn); `RegisterPage`, `ProtectedRoute`, `SubscriptionLockOverlay`, `TrialStatusBanner`, `SubscriptionCheckoutModal`, `StoreSettingsHubPage`, `SubscriptionPricingCards` xử lý đầy đủ onboarding `NO_PLAN`, tab `store-info` và trải nghiệm hết hạn gói (giữ user ở đúng route có quyền, hiển thị tấm phủ tối + icon ổ khóa đơn sắc + CTA gia hạn cho Chủ shop / nhắc liên hệ Chủ shop cho Nhân viên).

## File ảnh hưởng

- `src/MT_GRMS.Domain/Entities/Identity/User.cs`, `src/MT_GRMS.Domain/Entities/SaaS/Tenant.cs`
- `src/MT_GRMS.Application/Auth/*`, `Branches/BranchService.cs`, `Application/SaaS/Services/ITrialOnboardingBackfillService.cs`
- `src/MT_GRMS.Api/Controllers/AuthController.cs`, `TenantStoreController.cs`, `Filters/IdempotencyFilter.cs`, `Middlewares/SubscriptionAccessMiddleware.cs`, `Program.cs`
- `src/MT_GRMS.Infrastructure/Persistence/*`, `Repositories/UserRepository.cs`, `Services/RegistrationOtpStore.cs`, `Services/TrialOnboardingBackfillService.cs`, `Services/WalletService.cs`, `DependencyInjection.cs`
- `tests/MT_GRMS.UnitTests/UserAuthenticationTests.cs`, `Admin/TenantStoreControllerTests.cs`
- `tests/MT_GRMS.IntegrationTests/AuthControllerTests.cs`, `TrialOnboardingIntegrationTests.cs`

## Xác minh

- Backend Release build (`dotnet build MT_GRMS_BACKEND.sln -c Release`): PASS (0 errors).
- Backend UnitTests (`UserAuthenticationTests|TenantStoreControllerTests|WalletServiceTests`): 37/37 PASS.
- Backend IntegrationTests (`TrialOnboardingIntegrationTests|AuthControllerTests|IdempotencyFilterTests`): 28/28 PASS.
- Frontend `oxlint` trên các file chỉnh sửa: 0 errors; `vitest`: 17/17 PASS; `npm run build` (`tsc -b && vite build`): PASS.

## Ghi chú cho Lead

API Debug đang chạy khóa DLL `bin/Debug`, vì vậy build xác minh dùng Release/output riêng và không dừng tiến trình của developer. Migration report chỉ dùng ID, không ghi email/điện thoại đầy đủ.
