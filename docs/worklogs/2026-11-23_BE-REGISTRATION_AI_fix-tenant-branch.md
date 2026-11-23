# Sửa gán chi nhánh khi đăng ký cửa hàng

- Khi trình duyệt còn phiên của một cửa hàng khác, quy tắc gán mã gian hàng lúc lưu có thể đưa chi nhánh và danh mục mẫu mới sang cửa hàng của phiên đó.
- `UserRepository` dùng đường lưu dành riêng cho giao dịch tạo gian hàng. `AppDbContext` giữ mã gian hàng mới cho các bản ghi đã được gán đúng mã khi gian hàng đang ở trạng thái thêm mới. Đường lưu thông thường tiếp tục áp dụng mã gian hàng của phiên.
- Bổ sung kiểm thử tích hợp cho phiên đã đăng nhập, kiểm tra chi nhánh chính, thành viên, danh mục mẫu và đường lưu thông thường.
- Đã chuyển chi nhánh gắn nhầm về cửa hàng 21 trong PostgreSQL local sau khi kiểm tra các tham chiếu nghiệp vụ. Truy vấn xác nhận chi nhánh chính đang hoạt động.
- Kết quả kiểm thử tích hợp theo bộ lọc `UserDatabaseIntegrationTests` và `TrialOnboardingIntegrationTests` đạt 25/25.
