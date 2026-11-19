# WORKLOG: FE-12, FE-13, FE-14, FE-16, BUG-DB-42703 - POS Checkout, VietQR Webhook, Pre-Orders, Vouchers & Realtime Alerts

- **Ngày thực hiện**: 2026-11-20
- **Người thực hiện**: Son Nguyen Ba (@sonnguyenn10)
- **Nhánh Git (Branch)**: `fix/pos-v1`
- **Mã tính năng liên kết**:
  - [FE-12 - POS Checkout & Cashier Shift Management](../../../DOCS_MT_GRMS/specs/FE-12/spec.md)
  - [FE-13 - QR & Cash Payment & SePay Webhook](../../../DOCS_MT_GRMS/specs/FE-13/spec.md)
  - [FE-14 - Customer Pre-orders & Reservations](../../../DOCS_MT_GRMS/specs/FE-14/spec.md)
  - [FE-16 - Promotions, Vouchers & Discounts](../../../DOCS_MT_GRMS/specs/FE-16/spec.md)
- **Loại thay đổi**: Feature & Bug Fix & Refactor & Realtime Notifications

---

### 1. Mục Đích Thay Đổi (Why)
- **FE-13.2 Webhook SePay & Thanh toán VietQR**:
  - Khách chuyển khoản thành công: Đẩy realtime SignalR để màn hình POS tự động đóng popup VietQR và in bill; đồng thời gửi notification chuông báo tiền vào tài khoản cho Chủ cửa hàng và Thu ngân.
  - Xử lý chuyển thiếu tiền (partial payment): Tiền thực nhận vẫn ghi nhận vào sổ cái ngân hàng (`PaymentTransactions`) và Sổ Quỹ (`CashbookEntries`) để số dư khớp 100%; đơn hàng cập nhật trạng thái `PARTIALLY_PAID` và lũy kế `DepositAmount`. Màn hình POS hiển thị cảnh báo thiếu tiền và sinh lại QR với số tiền còn thiếu để khách quét tiếp lần 2.
- **FE-12.5 Giao ca, Đóng ca & Quản lý két POS**:
  - Gửi thông báo khẩn cấp (🔴) cho Chủ cửa hàng khi chốt ca bị Lệch tiền mặt (`Discrepancy != 0`).
  - Gửi thông báo (🟠) cho Chủ cửa hàng khi thu ngân tạo yêu cầu chi tiền vượt hạn mức két quầy (`PendingApproval`).
- **FE-14.1 - FE-14.4 Đơn đặt trước (Pre-order)**:
  - Thông báo (🟠) cho nhân viên/cửa hàng khi có đơn đặt trước mới hẹn giờ lấy để chủ động chuẩn bị và giữ tồn kho.
- **FE-16.2 Quản lý Voucher**:
  - Cảnh báo (🟡) cho Chủ cửa hàng khi đợt phát hành Voucher sắp hết lượt sử dụng ($\le 5$ hoặc $= 0$).
- **Hợp nhất Tài khoản tài chính & Két quỹ (Finance Accounts Consolidation)**:
  - Loại bỏ các bảng/entity trùng lặp `BranchPaymentAccount`, `PaymentAccount`, `StorePaymentConfig`, hợp nhất vào `FinanceAccount`.
  - Tích hợp dữ liệu ca làm việc và nhân viên trực ca vào trường JSON của `ShiftZReport`.
- **BUG-DB-42703 (Fix schema drift PostgreSQL)**:
  - Khắc phục lỗi `42703: column b.ConversionRulesJson does not exist` trên bảng `Branches` bằng cách đưa các cột chính sách chấm công vào script `EnsureBranchColumnsAsync`.

---

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)
- `src/MT_GRMS.Api/Controllers/PaymentsController.cs`: Webhook SePay phòng thủ 6 lớp, hỗ trợ thanh toán thiếu/đủ tiền, chống replay attack, chuẩn hóa thời gian UTC, bắn SignalR realtime.
- `src/MT_GRMS.Api/Controllers/FinanceAccountsController.cs`: Endpoint `/sepay/transactions` tra cứu lịch sử chuyển khoản, phân trang 9 dòng/trang, lọc theo tài khoản/chi nhánh, chuẩn hóa giờ UTC.
- `src/MT_GRMS.Api/Hubs/NotificationHub.cs`: Mở rộng SignalR groups (`OrderGroup`, `TenantGroup`, `UserGroup`) và join/leave group realtime.
- `src/MT_GRMS.Application/PosShifts/ShiftService.cs`: Bắn notification khi lệch tiền chốt ca và yêu cầu chi két quầy chờ duyệt.
- `src/MT_GRMS.Application/PosOrders/OrderService.cs`: Hỗ trợ đơn đặt trước (Pre-order) và bắn notification khi tạo đơn giữ chỗ.
- `src/MT_GRMS.Application/Promotions/PromotionService.cs`: Cảnh báo khi voucher sắp hết hạn mức phát hành.
- `src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`: Đảm bảo đồng bộ cột `ConversionRulesJson` và các cột chính sách chấm công cho bảng `Branches`.
- `docs/FINANCE_PAYMENT_ACCOUNT_MERGE_PLAN.md`: Kế hoạch và tài liệu đối soát hợp nhất tài khoản tài chính.

---

### 3. Kiểm Thử & Xác Nhận (Verification)
- [x] Đã tạo file worklog markdown trong thư mục `docs/worklogs/`.
- [x] Chạy biên dịch toàn bộ solution `MT_GRMS_BACKEND`: Thành công (0 lỗi, 0 cảnh báo).
- [x] Tuân thủ Clean Architecture: Controller giao tiếp qua Service và DTO, không làm lộ Domain Entity.
- [x] Đã pull nhánh `dev` mới nhất và giải quyết toàn bộ xung đột (0 conflict).
