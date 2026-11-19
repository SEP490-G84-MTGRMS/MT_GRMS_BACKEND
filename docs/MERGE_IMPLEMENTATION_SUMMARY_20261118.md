# Tổng hợp triển khai gộp ca POS và tài khoản thanh toán

**Ngày:** 18/11/2026  
**Phạm vi:** `MT_GRMS_BACKEND`, `MT_GRMS_FRONTEND` và PostgreSQL Docker cục bộ `mtgrms_db`.  
**Tài liệu định hướng:** `FINANCE_PAYMENT_ACCOUNT_MERGE_PLAN.md` và `SHIFT_HANDOVER_CLOSING_ERD.md`. Các tài liệu này là phương án thiết kế; quyết định nghiệp vụ đã được người dùng làm rõ trong cuộc trao đổi.

## 1. Nghiệp vụ ca POS đã triển khai

- Một ca POS có **một người mở và một người chốt**. Người tham gia ca là những nhân viên thực sự tham gia thao tác trong ca; không tự thêm toàn bộ nhân viên chỉ vì có lịch làm việc trùng giờ.
- Nhân viên có lịch 08:00–12:00 có thể tham gia ca POS 08:00–09:00 rồi ca 09:00–12:00. Ca trước phải chốt trước khi mở ca kế tiếp; lịch HR không bị ép thành đúng một ca POS.
- Danh sách người tham gia và thời điểm hoạt động được chụp vào `ShiftZReport.ParticipantsJson`. Người chốt được lưu trên `ShiftZReport.ClosedByUserId` và `ClosedAtUtc`.
- Bỏ entity và bảng `ShiftParticipant`, `ShiftCloseConfirmation` sau khi chuyển dữ liệu. **Giữ `ShiftCashMovement`** vì đây là từng phiếu thu/chi trong ca, có trạng thái xử lý riêng.
- Các luồng mở, xem, kiểm đếm, chốt thường và chốt cưỡng bức đã được điều chỉnh theo mô hình này.

**File chính:** `src/MT_GRMS.Application/PosShifts/ShiftService.cs`, `IShiftRepository.cs`; `src/MT_GRMS.Infrastructure/Repositories/ShiftRepository.cs`; `src/MT_GRMS.Domain/Entities/Finance/ShiftZReport.cs`; migration `20261118170000_MergeShiftParticipationIntoZReport.cs`.

## 2. Nghiệp vụ tài khoản và SePay đã triển khai

- Key SePay, webhook secret, trạng thái bật SePay và hai hạn mức chi chuyển về `Tenant`: một cấu hình tích hợp cho cửa hàng.
- `Branch.CurrentBankFinanceAccountId` xác định **một Bank hiện hành** để phát QR mới cho chi nhánh. Các dòng `FinanceAccount` Bank cũ được giữ để đọc lịch sử sau khi đổi tài khoản.
- `Order.FinanceAccountId` và `PaymentTransaction.FinanceAccountId` lưu tài khoản nhận tiền theo từng đơn/giao dịch. Đơn QR đã tạo tiếp tục dùng Bank được chốt lúc tạo, kể cả sau khi chi nhánh đổi Bank.
- API cấu hình chỉ nhận một chi nhánh cho một tài khoản Bank; kiểm tra tài khoản thuộc đúng tenant/chi nhánh. API QR không lấy số tài khoản từ cấu hình tenant nếu chi nhánh chưa chọn Bank.
- Webhook đối chiếu tài khoản nhận với Bank của đơn, chống xử lý lặp và ghi `PaymentTransaction`, `CashbookEntry`, cập nhật Order và số dư Bank trong transaction. Khi chờ khóa Bank, Order được tải lại để tránh đọc trạng thái cũ.
- Sổ quỹ lấy Bank hiện hành từ Branch, không tự tạo Bank để nhận tiền. Chốt ca/chốt ngày vẫn tổng hợp doanh thu Bank nhưng không ghi lại bút toán Bank đã được webhook ghi.

**File chính:** `src/MT_GRMS.Api/Controllers/PaymentsController.cs`, `FinanceAccountsController.cs`; `src/MT_GRMS.Application/PosOrders/OrderService.cs`, `Cashbook/CashbookService.cs`, `BusinessDays/BusinessDayClosingService.cs`; `src/MT_GRMS.Domain/Entities/SaaS/Tenant.cs`, `Branch.cs`; migration `20261118180000_ExpandFinancePaymentAccountMerge.cs`; `src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`.

## 3. Giao diện đã triển khai

- `src/features/pos/pages/StorePaymentConfigPage.tsx`: chọn một chi nhánh cho tài khoản nhận tiền; hiển thị Bank đang dùng và Bank lịch sử. Phần hướng dẫn cấu hình SePay trên trang này vẫn còn.
- `src/features/pos/components/modals/PaymentConfigModal.tsx`: chọn một chi nhánh, thay vì chọn đồng thời nhiều chi nhánh cho cùng tài khoản.
- `src/features/pos/components/modals/VietQrModal.tsx`: lấy cấu hình QR theo `orderCode` để dùng Bank đã chốt trên đơn; bỏ fallback sang Bank mặc định khác.
- `src/features/cashbook/pages/CashbookPage.tsx`: nút **Lịch sử chuyển tiền** lọc bút toán có `SourceType=INTERNAL_TRANSFER`; có thể bấm lại để xem toàn bộ Sổ quỹ.
- `src/features/finance-accounts/pages/FinanceAccountsSettingsPage.tsx` và `src/features/finance-accounts/components/SepayVisualGuide.tsx`: phần giao diện hướng dẫn SePay hiện có trong cây làm việc được giữ nguyên. `SepayVisualGuide.tsx` hiện là file **untracked**, cần đưa vào commit để có trên máy khác. Các thay đổi có sẵn ở ProductFormModal và LoginPage không thuộc phạm vi gộp này.

## 4. Migration và đối soát database cục bộ

- Đã backup database trước khi chuyển: `C:\Users\Admin\Downloads\mtgrms_db_before_merge_20261118.dump`.
- Đã phục hồi backup vào database thử `mtgrms_merge_validation`, chạy cả hai migration và kiểm tra kết quả; sau đó chạy cùng migration trên `mtgrms_db`.
- Theo lựa chọn của chủ cửa hàng: **Cầu Giấy giữ tài khoản MB đuôi 1113**, Ba Đình tạm không có tài khoản nhận QR. Bản ghi assignment Ba Đình đã được ngừng trước khi backfill.
- 21 ca giữ nguyên; 66 dòng người tham gia cũ thành 66 mục snapshot. 7 bút toán Bank giữ nguyên; tổng ròng bút toán và tổng số dư sổ Bank đều **2.769.750đ** trước/sau.
- Có 2 chi nhánh có Bank hiện hành; không có liên kết Bank hiện hành sai tenant, sai chi nhánh hoặc sai loại tài khoản. Key SePay đã mã hóa, webhook secret và hai hạn mức chi trên Tenant khớp cấu hình cũ.
- Hai migration đã được ghi vào `__EFMigrationsHistory`. Báo cáo chi tiết: `docs/MERGE_RECONCILIATION_20261118.md`.

## 5. Kiểm thử

- Backend: **1.169 unit test đạt**, **105 integration test đạt**, 5 integration test được đánh dấu skip từ trước.
- Frontend: `npm run build` thành công.
- Đã chạy `git diff --check` không thấy lỗi khoảng trắng trong diff.
- Đã chạy SQL migration thật trên bản sao database và database cục bộ; kết quả đối soát nêu ở mục 4.

## 6. Giới hạn và việc chưa hoàn tất

1. **9 đơn Bank/VietQR cũ đã thanh toán** vốn không lưu `PaymentAccountId`, nên không thể xác định chắc tài khoản thực nhận. `FinanceAccountId` của chúng để NULL; không gán hồi tố theo Bank hiện hành.
2. **Xóa vật lý 3 bảng tài chính cũ**: Ba bảng cũ `PaymentAccounts`, `BranchPaymentAccounts`, `StorePaymentConfigs` đã được **xóa vật lý hoàn toàn** (migration `20261118190000_DropLegacyPaymentAccountTables`), các DbSet và entity cũ đã được dọn sạch khỏi Domain & DbContext.
3. Tenant An Nhiên có key SePay được chuyển nhưng `IsSepayEnabled=false` theo cấu hình cũ. Cầu Giấy có Bank hiện hành nhưng QR vẫn tắt cho đến khi chủ cửa hàng bật SePay.
4. Chưa kiểm thử webhook bằng giao dịch SePay thực tế; build, test và đối soát database không xác nhận được kết nối ngoài hoặc tiền thực nhận tại ngân hàng.
5. Các thay đổi code hiện nằm trong cây làm việc của hai repo, **chưa commit/push**. Pull trên một máy khác sẽ không mang theo thay đổi chưa commit, đặc biệt file guide đang untracked.
