# Mua thêm Quản lý nhân viên cho Gói Thường

Chủ Gói Thường còn hiệu lực có thể xem báo giá và mua riêng tiện ích bằng Ví gian hàng. Giá gốc là 30.000 đ cho 30 ngày, số ngày còn lại làm tròn lên và cộng VAT 10%. API kiểm tra lại tổng tiền và ngày hết hạn trước khi trừ ví. Giao dịch mua và trạng thái mở khóa được lưu cùng nhau, mã yêu cầu cho phép thử lại mà chỉ trừ ví một lần.

`TenantStoreController` xử lý báo giá và thanh toán. `TenantService` trả trạng thái khóa và khả năng mua trong thông tin gian hàng. `SubscriptionAccessMiddleware` dùng trạng thái gói và tiện ích để bảo vệ các thao tác quản lý hồ sơ nhân viên dành cho chủ cửa hàng. Chuyển từ Dùng thử sang Gói Thường chỉ cấp tiện ích nếu đã chọn mua.

Gian hàng Gói Thường đã bật tiện ích tiếp tục dùng trạng thái đã lưu. Dữ liệu demo Gói Thường có sẵn giao dịch mua tiện ích.

Backend build thành công. 36 ca trong hai lớp kiểm thử controller và middleware liên quan đạt khi chạy riêng. Bộ chạy VSTest hết thời gian chờ kết nối testhost trên môi trường này.
