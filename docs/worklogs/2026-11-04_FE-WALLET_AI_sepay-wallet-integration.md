# Worklog: Tích hợp Cổng thanh toán SePay & Ví gian hàng (Backend)

- **Ngày thực hiện**: 2026-11-04
- **Mã task / Tính năng**: `FE-WALLET` / `wallet-topup-sepay-integration`
- **Người thực hiện**: AI Coding Assistant (Antigravity)
- **Dự án**: `MT_GRMS_BACKEND`

---

## 1. Mục đích (Why)
- Hiện thực hóa tính năng nạp tiền ví gian hàng thật qua cổng thanh toán SePay (VietQR Napas247).
- Đảm bảo khi khách chuyển khoản qua ngân hàng, Webhook SePay được tiếp nhận tự động, kiểm tra Idempotency chống nạp trùng, tăng số dư ví `Tenants.WalletBalance`, lưu sổ cái biến động `WalletTransactions` và trừ ví thực tế khi thanh toán mua gói cước SaaS.

---

## 2. Danh sách tệp & Nội dung thay đổi (What)
- **Domain**:
  - `src/MT_GRMS.Domain/Entities/SaaS/Tenant.cs`: Thêm thuộc tính `WalletBalance numeric(18,2) NOT NULL DEFAULT 0` và các Navigation Collections `WalletTransactions`, `WalletTopupOrders`.
  - `src/MT_GRMS.Domain/Entities/SaaS/WalletTransaction.cs`: Thực thể sổ cái biến động số dư bất biến (Immutable Ledger).
  - `src/MT_GRMS.Domain/Entities/SaaS/WalletTopupOrder.cs`: Thực thể lệnh yêu cầu nạp ví qua VietQR SePay.
- **Application**:
  - `src/MT_GRMS.Application/Common/SePaySettings.cs`: Cấu hình thông tin tài khoản ngân hàng và Secret API Key SePay.
  - `src/MT_GRMS.Application/SaaS/Dtos/WalletDtos.cs`: Các DTOs cho tạo đơn nạp ví, tra cứu trạng thái polling, số dư ví, danh sách giao dịch, và SePay Webhook payload.
  - `src/MT_GRMS.Application/SaaS/Services/IWalletService.cs`: Hợp đồng dịch vụ quản lý ví và xử lý webhook.
  - `src/MT_GRMS.Application/Admin/Dtos/TenantDtos.cs`: Bổ sung `WalletBalance` vào `TenantDetailResponse`.
  - `src/MT_GRMS.Application/Admin/Services/TenantService.cs`: Trả về `WalletBalance` trong chi tiết gian hàng.
- **Infrastructure**:
  - `src/MT_GRMS.Infrastructure/Persistence/AppDbContext.cs`: Đăng ký `DbSet<WalletTransaction>` và `DbSet<WalletTopupOrder>`.
  - `src/MT_GRMS.Infrastructure/Persistence/Configurations/SaaS/TenantConfiguration.cs`: Cấu hình thuộc tính `WalletBalance`.
  - `src/MT_GRMS.Infrastructure/Persistence/Configurations/SaaS/WalletTransactionConfiguration.cs`: Fluent API cấu hình bảng `WalletTransactions` với unique index và foreign key.
  - `src/MT_GRMS.Infrastructure/Persistence/Configurations/SaaS/WalletTopupOrderConfiguration.cs`: Fluent API cấu hình bảng `WalletTopupOrders`.
  - `src/MT_GRMS.Infrastructure/Services/WalletService.cs`: Hiện thực hóa nghiệp vụ tạo đơn nạp VietQR SePay, polling trạng thái, tính toán số dư/sổ cái, và xử lý Webhook Idempotency bọc trong DB Transaction.
  - `src/MT_GRMS.Infrastructure/DependencyInjection.cs`: Đăng ký `SePaySettings` và `IWalletService`.
- **Api**:
  - `src/MT_GRMS.Api/Controllers/WalletController.cs`: Controller `/api/v1/tenant/wallet/*` phục vụ nạp ví, polling, lấy số dư và giao dịch.
  - `src/MT_GRMS.Api/Controllers/SepayWebhookController.cs`: Controller `/api/v1/webhooks/sepay` tiếp nhận IPN từ SePay.
  - `src/MT_GRMS.Api/Controllers/TenantStoreController.cs`: Cập nhật logic `PurchaseSubscription` trừ ví thực tế khi chọn `WALLET`.
  - `src/MT_GRMS.Api/appsettings.json` & `appsettings.Development.json`: Cấu hình thông tin SePay.
- **Tests**:
  - `tests/MT_GRMS.UnitTests/WalletServiceTests.cs`: 7 bài kiểm thử xUnit bao phủ REQ-WAL-01, REQ-WAL-03 (Idempotency), REQ-WAL-04 (Deduct).

---

## 3. Chi tiết Kỹ thuật (Technical Highlights)
- **SePay Webhook Verification**: Hỗ trợ xác thực header `Authorization: Apikey <KEY>` / `Bearer <KEY>`.
- **Idempotency**: Dựa trên `referenceCode` từ SePay để đảm bảo không cộng số dư trùng lặp khi SePay retry.
- **Concurreny & Transaction**: Bọc toàn bộ quá trình cộng/trừ ví và lưu sổ cái trong Database Transaction.
- **Database Migration**: Đã cập nhật schema PostgreSQL trên container `mtgrms-postgres`.

---

## 4. Kết quả Kiểm thử (Verification)
- `dotnet build`: Passed, 0 Errors, 0 Warnings.
- `dotnet test`: 7/7 tests passed trong `WalletServiceTests`.

---

## 5. Ghi chú cho Manager khi duyệt & merge
- **Mức độ rủi ro**: Thấp (Low).
- **Hành động sau merge**: Cấu hình `SePay:ApiKey` chính thức trên production `appsettings.json` khi kết nối tài khoản ngân hàng thật của công ty.

