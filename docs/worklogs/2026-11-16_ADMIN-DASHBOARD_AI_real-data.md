# Dashboard Admin sử dụng dữ liệu thật

- Bỏ số liệu mẫu, trạng thái tác vụ giả và biểu đồ POS khỏi dashboard SaaS.
- Thêm GET /api/admin/dashboard/overview, chỉ Admin, thống kê tổng hợp không trả nhân sự hoặc chi nhánh cụ thể.
- Lọc mua gói SUCCESS theo kỳ tháng này, tháng trước, quý này, năm nay, ranh giới UTC+7 và khoảng nửa mở. Loại TOPUP, PURCHASE_ADDON, FAILED, bản ghi đã xóa và tenant đã xóa.
- Điểm bán đếm chi nhánh đang hoạt động của tenant chưa xóa, bỏ tenant query filter có chủ đích cho số liệu toàn nền tảng.
- Sửa statusType cảnh báo thành expiring/grace/locked đúng hợp đồng frontend. Số đếm cảnh báo ghi rõ phạm vi danh sách tối đa 10 khách hàng.
- MRR bỏ tenant hết hạn. KPI mua gói đếm PURCHASE_PLAN thành công thay vì UPGRADE_PLAN không được sinh trong luồng ví hiện tại.
- Giữ MRR là ước tính theo giá niêm yết hiện tại. Chưa đủ dữ liệu lịch sử để dựng MRR quá khứ chính xác nên biểu đồ dùng số tiền mua gói theo ngày.
- File chính gồm AdminDashboardOverviewController, AdminDashboardService, TenantRepository, AdminDashboardPage.tsx và adminDashboard.css.
- Kiểm chứng API build đạt, 22 warning hiện có, 0 error. Test EF InMemory đạt 1/1 kiểm tra kỳ, loại giao dịch và tổng hợp nhiều tenant. Frontend build đạt và test dashboard đạt 4/4.
- Chưa xác minh PostgreSQL thật hoặc trình duyệt. Không đổi schema. Cần khởi động lại backend.
