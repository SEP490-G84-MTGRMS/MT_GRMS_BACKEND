# Admin bị chặn bởi kiểm tra gói dịch vụ

- Nguyên nhân: Admin không có tenant được ResolveTenant gán tenant_mt_mart_01; sau reset DB tenant này không tồn tại nên SubscriptionAccessMiddleware trả SUBSCRIPTION_REQUIRED (403).
- Sửa: bỏ kiểm tra subscription cho principal đã xác thực có role ADMIN. Không miễn cho user thường hoặc dựa trên header, không bỏ Authorize của API quản trị.
- Xác minh: Build API đạt 0 lỗi. Đã khởi động lại đúng backend local cổng 5232 vì tiến trình cũ chưa nhận sửa đổi. Kiểm tra HTTP thật: Admin trả 200 cho KPI, cơ cấu gói, cảnh báo, overview và tenant list. Trial/Standard/Pro-extra trả 200 cho dashboard và branches; cả ba vẫn trả 403 khi gọi API Admin (đúng phân quyền). Tài khoản NO_PLAN bị chặn nghiệp vụ nhưng vẫn truy cập được auth/me/2fa. Không tắt kiểm tra subscription hoặc phân quyền của chủ cửa hàng.
