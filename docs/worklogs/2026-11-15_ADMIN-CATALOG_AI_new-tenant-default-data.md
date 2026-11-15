# Dữ liệu khởi tạo toàn cầu cho cửa hàng mới

## Mục tiêu

Admin cấu hình danh mục mẫu tại /admin/master-catalog. Tài khoản chủ cửa hàng mới nhận danh mục này ngay khi cửa hàng được tạo.

## Thay đổi

- TenantCatalogInitializer đọc các MasterProducts IsActive và chưa xóa, cùng đơn vị chưa xóa.
- UserRepository.RegisterStoreOwnerAsync gọi initializer trong transaction có sẵn. Cả đăng ký công khai và Admin tạo cửa hàng đều đi qua luồng này.
- Tạo nhóm hàng theo tên đã chuẩn hóa khoảng trắng, sản phẩm với mã riêng, đơn vị cơ bản tỷ lệ 1, các đơn vị quy đổi và BranchProducts cho chi nhánh chính.
- Sử dụng giá bán gợi ý hoặc 0 nếu mẫu chưa có giá. Tồn kho bắt đầu bằng 0.
- Dữ liệu tenant là bản sao độc lập tại thời điểm đăng ký. Mẫu toàn cầu được bật/tắt từ popup sửa mẫu.
- Giữ giới hạn schema hiện tại với dữ liệu master cũ: tên sản phẩm tối đa 200 ký tự, URL ảnh dài hơn 500 ký tự được để trống khi sao chép.
- UI đổi thành Dữ liệu khởi tạo, bỏ khẳng định 5.000+ mã mẫu và chỉ hiển thị thống kê có dữ liệu tương ứng.

## Kiểm chứng

- npm run build frontend đạt.
- dotnet test --filter FullyQualifiedName~TenantCatalogInitializationTests đạt 2/2 sau khi chạy ngoài sandbox. Lần đầu testhost bị timeout kết nối trong sandbox.
- Kiểm tra mẫu tắt/xóa bị loại, đơn vị cơ bản, giá quy đổi, TenantId, gán chi nhánh và tồn kho rỗng.
- Kiểm tra hai cửa hàng nhận ID độc lập và cửa hàng đăng ký sau dùng mẫu cập nhật.
- Chưa đăng ký tài khoản thật trên database hoặc kiểm tra trực tiếp trình duyệt. Cần khởi động lại backend để dùng luồng mới. Nếu chưa có mẫu đang bật, danh mục được khởi tạo rỗng.
