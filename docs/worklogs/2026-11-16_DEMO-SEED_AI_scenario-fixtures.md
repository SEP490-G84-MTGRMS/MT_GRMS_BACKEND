# Chuẩn hóa dữ liệu demo

- Thay phần seed và helper demo chồng chéo trong DbInitializer bằng DemoScenarioSeedData. Giữ các bước khởi tạo schema và thay đổi wallet hiện hữu.
- Bỏ hành vi mỗi lần khởi động ghi đè giá gói, vô hiệu gói khác, tự nâng tenant cũ lên Pro. Các cấu hình gói hiện tại được bảo toàn.
- Bộ mới có 1 Admin khi database mới, tái sử dụng Admin hiện có. Có 8 tài khoản owner cho NO_PLAN, TRIAL 3 ngày, Standard 1 chi nhánh có addon nhân viên, Pro 2 chi nhánh, Pro thêm chi nhánh, sắp hết hạn, ân hạn và khóa mềm.
- Có hồ sơ nhân viên, tài khoản theo vai trò, ca làm việc, sản phẩm, đơn vị, gán chi nhánh, tồn thấp/hết hàng/cận hạn, hóa đơn, PO, chi phí và lịch sử ví mua gói có số dư cân bằng.
- Seed trong transaction, chỉ tạo nhóm mới, không xóa dữ liệu cũ và không reset thay đổi của người dùng. Không kéo dài trial hoặc hạn gói khi restart.
- Program hỗ trợ --seed-demo-only true ở Development, nạp dữ liệu không chạy DDL hoặc mở web server. Fallback tự seed chỉ bật trong Development.
- PostgreSQL cũ có unique index OrderCode toàn cục. Đã dùng mã kịch bản trong các mã chứng từ để tránh trùng giữa tenant. Lần thử lỗi rollback đầy đủ.
- Build đạt 0 error. Test seed InMemory đạt 1/1, kiểm tra số lượng, trial 3 ngày, mua gói/addon, số dư ví, không trùng mã và chạy lại bảo toàn giá/tên đã sửa.
- Đã nạp thật vào PostgreSQL localhost mtgrms_db, chạy lại lần hai vẫn 8 tenant / 12 chi nhánh / 33 nhân viên / 77 hóa đơn.
- Thử đăng nhập 0910000003 đạt STORE_OWNER, không yêu cầu OTP. API dashboard last7days của Standard trả 1 chi nhánh, doanh thu 1.747.000 đ, Pro-extra trả 3 chi nhánh, doanh thu 5.241.000 đ.
- Tài khoản và cách dùng được ghi trong docs/DEMO_ACCOUNTS.md. Không đổi mật khẩu Admin hiện có, không xóa nhóm dữ liệu cũ. Chưa kiểm tra trực tiếp giao diện browser.

- Theo yêu cầu, chuyển toàn bộ seed và helper trực tiếp vào DbInitializer.cs, xóa DemoScenarioSeedData.cs. Program và test gọi DbInitializer.SeedDemoAsync; giữ nguyên dữ liệu và hành vi seed.

- Đổi email seed Admin thành admin@mtgrms.vn và owner thành demo.<kịch bản>@mtgrms.vn theo yêu cầu. Đồng bộ email hiện có của đúng ID demo trong PostgreSQL, frontend hiển thị email để đăng nhập; giữ mật khẩu và vai trò.
