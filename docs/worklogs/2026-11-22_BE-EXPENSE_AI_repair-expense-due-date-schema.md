# Đồng bộ hạn thanh toán phiếu chi trên PostgreSQL local

`ExpenseReminderWorker` báo lỗi PostgreSQL `42703` khi đọc cơ sở dữ liệu local thiếu `ExpenseVouchers.DueDate`. Hai profile local bỏ qua khởi tạo schema, nên migration hạn thanh toán chưa được áp dụng.

- `Program.cs` và `launchSettings.json` bật bước sửa schema cục bộ trong Development.
- `DbInitializer.cs` dùng lại SQL của hai migration để bổ sung cột phân bổ tồn của chi tiết hóa đơn, hạn thanh toán phiếu chi và chỉ mục hạn thanh toán. Các lệnh chỉ tạo phần schema còn thiếu và giữ nguyên dữ liệu phiếu chi.
- Đã bổ sung cột và chỉ mục trên `mtgrms_db`, sau đó kiểm tra truy vấn đọc phiếu chi đến hạn trên PostgreSQL local.
- Build dự án API thành công với 0 lỗi và 0 cảnh báo.
