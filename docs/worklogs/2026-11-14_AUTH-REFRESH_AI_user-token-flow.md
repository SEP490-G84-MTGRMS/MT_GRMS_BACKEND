# Thu gọn luồng access và refresh token

## Phạm vi

Theo yêu cầu của người dùng, sử dụng RefreshToken và RefreshTokenExpiryTime hiện có trên Users. Đã gỡ các entity, store, mapping, migration và test quản lý phiên nhiều thiết bị vừa thêm.

## Thay đổi

- AuthService cấp access mặc định 15 phút và refresh cố định 7 ngày khi đăng nhập hoặc đăng ký. Token mới dùng CSPRNG và lưu SHA-256 trong cột hiện có. Refresh token dạng cũ còn hạn được hỗ trợ; giá trị hash trong DB được loại khỏi nhánh kiểm tra token cũ.
- Refresh chỉ cấp access mới, giữ refresh và hạn ban đầu, tránh ghi đè token khi các tab refresh đồng thời.
- AuthController đồng bộ hạn refresh cookie với dữ liệu Users, hỗ trợ đường dẫn API auth thường và v1, logout bằng refresh cookie khi access hết hạn.
- Reset password xóa refresh token. Access token đã cấp có thể tiếp tục hoạt động đến hạn của nó.
- Frontend chia sẻ refresh giữa API thường và tài chính, replay tối đa một lần, giữ trạng thái khi lỗi mạng/429/5xx và có thông báo thử lại.

## Kiểm chứng

- Dừng bổ sung và chạy test theo yêu cầu người dùng. Các thay đổi test của phương án nhiều thiết bị đã được gỡ.
- Backend API build thành công, 0 lỗi; còn các warning nullable/unreachable code sẵn có trong dự án.
- Frontend npm run build thành công; có cảnh báo kích thước bundle.
- git diff --check cho hai repo thành công. Chưa xác minh tương tác trình duyệt với server chạy thực tế.

## Triển khai

- Đăng nhập mới thay refresh token đang lưu của tài khoản.
- Refresh token hết 7 ngày yêu cầu đăng nhập lại.
