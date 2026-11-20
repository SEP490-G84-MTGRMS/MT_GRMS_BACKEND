# WORKLOG: WALLET - Hợp nhất yêu cầu nạp tiền và giao dịch ví

- **Ngày thực hiện**: 2026-11-16
- **Người thực hiện**: AI Agent
- **Nhánh Git**: `refactor/wallet-management`
- **OpenSpec**: `wallet-topup-sepay-integration`, T14–T16
- **Loại thay đổi**: Refactor

## Mục đích

Một yêu cầu nạp VietQR dùng cùng bản ghi `WalletTransactions` từ lúc chờ thanh toán đến lúc ghi nhận số dư.

## Thay đổi

- `WalletTransaction` lưu thông tin đơn nạp và định danh giao dịch SePay. API tra cứu vẫn trả `COMPLETED` cho giao dịch `SUCCESS`.
- `WalletService` xác nhận tiền trong một PostgreSQL transaction, lọc sổ cái theo `SUCCESS` và lưu khoản chuyển dùng lại mã đơn đã hoàn tất ở trạng thái `REVIEW`.
- `WalletTopupSchemaMigration` chuyển đơn cũ trong lúc khởi động. Đơn hoàn tất được ghép vào bút toán hiện có theo tenant và mã tham chiếu. Nếu thiếu hoặc trùng kết quả đối chiếu, transaction rollback và quá trình khởi động dừng.
- `SepayWebhookController` kiểm tra thông tin xác thực trước khi xử lý khoản chuyển. Môi trường nhận webhook cần cấu hình khóa thật.

## Kiểm thử

- `WalletServiceTests` và `SepayWebhook_ValidatesApiKey_AndPreventsReplay` bao phủ hợp đồng tạo đơn, tra cứu theo tenant, sổ cái và webhook gửi lại.
- `WalletPostgresIntegrationTests` dùng PostgreSQL tạm để kiểm tra rollback khi thiếu kết quả đối soát, bảo toàn mã đơn và số dư, và bốn webhook đồng thời. Thiết lập `MT_GRMS_WALLET_TEST_POSTGRES` với kết nối dành cho kiểm thử có quyền tạo database.
- Build backend thành công. Toàn bộ kiểm thử ví, gồm kiểm thử PostgreSQL, đều đạt. Khi chạy cả solution, 1.053 unit test đạt và 68/72 integration test đạt. Ba lỗi integration thuộc `AuthControllerTests` và một lỗi thuộc `TransferBusinessFlowIntegrationTests`.

## Ghi chú triển khai

Chạy migration khi các instance backend dùng schema hai bảng đã dừng. Migration tự chạy khi backend mới khởi động. Dữ liệu cần đối soát được xác định trong lỗi PostgreSQL nếu migration dừng.
