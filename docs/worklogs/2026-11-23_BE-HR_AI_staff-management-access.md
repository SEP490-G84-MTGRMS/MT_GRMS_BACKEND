# Khóa chức năng Nhân viên theo gói

Các màn ca làm việc, bảng chấm công và duyệt đơn yêu cầu thiếu kiểm tra quyền theo gói. Các API liên quan cũng thiếu metadata để SubscriptionAccessMiddleware áp dụng điều kiện mua tiện ích.

- Gắn RequiresStaffManagement vào các controller cấu hình ca, chính sách chấm công, lịch làm, duyệt đơn và bảng lương.
- Áp dụng điều kiện gói ở các endpoint có metadata cho người dùng thuộc gian hàng. Quy tắc hiện có xác định Gói Thường chưa mua tiện ích là trường hợp cần khóa.
- Thêm kiểm thử metadata và kiểm tra middleware với vai trò chủ cửa hàng và nhân viên.
- Frontend kiểm tra TypeScript thành công và 4 kiểm thử tiện ích đạt.
- Backend build thành công trong thư mục riêng và 21 kiểm thử SubscriptionAccessMiddlewareTests đạt.
