# Đối soát gộp ca POS và tài khoản thanh toán — 18/11/2026

## Phạm vi

- Database cục bộ Docker: `mtgrms_db`.
- Backup trước chuyển đổi: `C:\Users\Admin\Downloads\mtgrms_db_before_merge_20261118.dump` (pg_dump custom format).
- Đã phục hồi backup vào `mtgrms_merge_validation`, thử migration và đối soát trước khi chạy trên database cục bộ chính.

## Quyết định dữ liệu

- Tài khoản MB đuôi **1113** từng gán đồng thời Ba Đình và Cầu Giấy, không có Order hoặc PaymentTransaction nào tham chiếu. Theo quyết định của chủ cửa hàng, Cầu Giấy giữ tài khoản; assignment Ba Đình được ngừng, Ba Đình không có Bank hiện hành để phát QR.
- Sổ Bank cũ đuôi **9999** không có BranchId nhưng có 7 bút toán. Giữ nguyên sổ và số dư; không tự gán sang chi nhánh.

## Kết quả sau migration

| Chỉ tiêu | Trước | Sau |
|---|---:|---:|
| Ca POS | 21 | 21 |
| Người tham gia ca | 66 dòng ShiftParticipants | 66 mục trong ParticipantsJson |
| Xác nhận chốt ca | 17 dòng ShiftCloseConfirmations | Đã chuyển người chốt lên ShiftZReport, bỏ bảng cũ |
| Chi nhánh có Bank hiện hành | 3 assignment, trong đó một Bank gán 2 chi nhánh | 2 Branch.CurrentBankFinanceAccountId hợp lệ |
| Bút toán Bank | 7 | 7 |
| Tổng ròng bút toán Bank | 2.769.750đ | 2.769.750đ |
| Tổng số dư sổ Bank | 2.769.750đ | 2.769.750đ |
| Order/PaymentTransaction có PaymentAccountId nhưng chưa chuyển FinanceAccountId | 0 | 0 |

Không có con trỏ Bank hiện hành trỏ sai tenant, chi nhánh hoặc loại tài khoản. Hai migration `20261118170000` và `20261118180000` đã ghi vào `__EFMigrationsHistory`.
Key SePay đã mã hóa, webhook secret và hai hạn mức chi trên Tenant khớp từng giá trị ở StorePaymentConfig cũ (đối chiếu trong DB, không xuất nội dung secret).

## Dữ liệu lịch sử cần giữ để xử lý riêng

- **9 đơn Bank/VietQR đã thanh toán** vốn không có PaymentAccountId; không có dữ liệu đáng tin để xác định tài khoản nhận tiền. `FinanceAccountId` của các đơn này giữ NULL, không gán hồi tố theo Bank hiện hành.
- **3 bảng tài chính cũ**: `PaymentAccounts`, `BranchPaymentAccounts`, `StorePaymentConfigs` đã được **xóa vật lý hoàn toàn** bằng migration `20261118190000_DropLegacyPaymentAccountTables`. Mã nguồn và DbContext đã loại bỏ các entity và DbSet tương ứng.
- Tenant An Nhiên có key và webhook secret đã chuyển, nhưng `IsSepayEnabled=false` theo cấu hình cũ. Vì vậy dù Cầu Giấy đã có Bank hiện hành, QR vẫn chưa bật cho đến khi chủ cửa hàng bật SePay.

Backup chứa dữ liệu vận hành và khóa đã mã hóa; không đưa file backup vào Git.
