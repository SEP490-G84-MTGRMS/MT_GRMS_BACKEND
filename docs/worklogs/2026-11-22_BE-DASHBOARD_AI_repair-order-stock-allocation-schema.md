# Khôi phục truy vấn Tổng quan trên PostgreSQL local

API Tổng quan nhận lỗi PostgreSQL `42703` khi truy vấn P&L nạp chi tiết hóa đơn. Schema local thiếu `OrderDetails.StockAllocationsJson` dù repo đã có migration. Hai profile `http` và `https` bỏ qua đồng bộ schema khi khởi động.

- `Program.cs` bổ sung cột `text NULL` trong PostgreSQL khi bật cấu hình sửa schema ở môi trường Development.
- `launchSettings.json` bật cấu hình cho hai profile local. Lệnh `ADD COLUMN IF NOT EXISTS` có thể chạy lại và giữ nguyên dữ liệu hóa đơn.
- Đã áp dụng thay đổi vào `mtgrms_db` local. PostgreSQL xác nhận cột ban đầu chưa tồn tại, chạy lại lệnh thành công và truy vấn nối hóa đơn với chi tiết hóa đơn đã đọc được cột.
- Build dự án API thành công với 0 lỗi, 0 cảnh báo. Kiểm thử live thực hiện ở PostgreSQL, chưa thử lại API với phiên chủ cửa hàng trên Chrome.
