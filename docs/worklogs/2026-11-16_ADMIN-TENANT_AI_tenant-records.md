# Lịch sử mua gói trong chi tiết tenant Admin

- Theo yêu cầu điều chỉnh của người dùng, chi tiết tenant chỉ thêm lịch sử mua gói dịch vụ.
- API AdminTenantRecordsController chỉ trả WalletTransactions loại PURCHASE_PLAN, lọc TenantId rõ ràng, loại dữ liệu đã xóa và phân trang tối đa 100.
- Các section cũ owners, employees, branches, transactions, invoices trả BadRequest. Response không có số dư ví, mã ngân hàng hoặc dữ liệu nhân sự.
- TenantService trả danh sách Branches rỗng trong chi tiết; vẫn giữ số lượng tổng hợp phục vụ hạn mức. TenantRepository đọc số lượng chi nhánh theo tenant được chọn bằng khóa.
- Frontend gỡ các tab vừa thêm và liên hệ cá nhân chủ cửa hàng trong chi tiết, bỏ request tải chủ sở hữu. Lịch sử mua gói có tải/lỗi/rỗng, thử lại và phân trang.
- Menu Billing được gỡ, route cũ về danh sách tenant.
- Kiểm tra AdminTenantRecordsTests xác nhận giới hạn tenant, loại dữ liệu ngoài PURCHASE_PLAN, chặn các section cũ và chi tiết không trả danh sách chi nhánh.
- Frontend npm run build đạt; backend build trong dotnet test đạt, kiểm tra AdminTenantRecordsTests PASS (1/1). Còn các cảnh báo sẵn có của repo. Chưa kiểm tra trực tiếp trên trình duyệt hoặc PostgreSQL thực tế.
