# WORKLOG — Admin tạo cửa hàng mới

- Ngày thực hiện 2026-11-15
- Người thực hiện AI
- Mã tính năng ADMIN-TENANT
- Loại thay đổi Feature

## Mục đích
Popup tạo tenant trước đây chỉ báo thành công ở frontend. Bổ sung API tạo thật và kết nối form mới.

## Thay đổi
- Thêm CreateTenantRequest và CreateTenantRequestValidator cho thông tin cửa hàng, subdomain, chủ sở hữu, email, số điện thoại, mật khẩu, địa chỉ, thành phố và mã số thuế.
- Thêm TenantProvisioningService, đăng ký DI và action trong AdminTenantsController.
- Tái sử dụng IUserRepository.RegisterStoreOwnerAsync để lưu tenant, user, tenant membership với role STORE_OWNER, chi nhánh chính và audit log trong cùng transaction.
- Kiểm tra subdomain, email và số điện thoại trùng. Các unique constraint hiện có bảo vệ yêu cầu đồng thời.
- Mật khẩu được hash bằng IPasswordHasher. Audit chỉ ghi định danh và trạng thái khởi tạo.
- User được tạo với email chưa xác minh, 2FA tắt, không cấp phiên đăng nhập. Admin nhập mật khẩu ban đầu trong form.
- Tenant có SubscriptionStatus NO_PLAN, chưa tiêu thụ trial. Chủ cửa hàng tự kích hoạt trial hoặc mua gói qua luồng hiện có.

## API
- POST /api/admin/tenants yêu cầu quyền Admin theo RoleConstants.AdminOnly.
- Request gồm name, subdomain, ownerName, ownerEmail, ownerPhone, password và các trường tùy chọn address, city, taxCode.
- HTTP 201 trả ApiResponse có data.id để frontend mở trang chi tiết.
- HTTP 400 cho dữ liệu không hợp lệ, HTTP 409 cho dữ liệu trùng.
- Sử dụng bảng và unique index hiện có.

## Kiểm chứng
- Frontend npm run build đã thành công.
- Build backend lần cuối thành công với 0 lỗi, 13 cảnh báo ở các file hiện có. Dùng output riêng vì tiến trình API đang chạy giữ khóa DLL trong thư mục mặc định.
- Chưa kiểm thử tạo bản ghi thực tế qua UI/database.

## Ghi chú vận hành
- Khởi động lại backend sau khi build để endpoint mới có hiệu lực.
- Chủ cửa hàng đăng nhập bằng số điện thoại và mật khẩu Admin đã nhập. Form không gửi email cấp mật khẩu.
