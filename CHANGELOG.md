# CHANGELOG - MT_GRMS_BACKEND

Toàn bộ các thay đổi của repository Backend được tổng hợp tại đây theo từng phiên bản. Chi tiết từng commit xem tại [`docs/worklogs/`](./docs/worklogs/).

---

## [Unreleased] - 2026-10-13

### Added

- Chuẩn hóa HTTP Status Codes: 200 OK, 400 Bad Request, 401 Unauthorized đóng gói qua `ApiResponse<T>` [BE-AUTH].
- Loại bỏ hoàn toàn fallback mock ngầm, xác thực nghiêm ngặt 100% dựa trên database PostgreSQL [BE-AUTH].
- Hỗ trợ route kép `[Route("api/v1/[controller]")]` và `[Route("api/[controller]")]` trên `AuthController` tương thích frontend [BE-AUTH].
- Bổ sung xác thực mật khẩu tài khoản trong `AuthService` và mã lỗi 401 qua `ExceptionHandlingMiddleware` [BE-AUTH].
- Mở rộng `UserInfo` record với thuộc tính ánh xạ `Role => RoleName` [BE-AUTH].
- Bổ sung bộ kiểm thử đơn vị `UserAuthenticationTests` (10/10 tests PASS) [BE-AUTH].
- Triển khai hạ tầng PostgreSQL 16 qua Docker Compose (`docker-compose.yml`, `init-db.sql`).
- Tối giản hóa thực thể và bảng `users` (`Id` string Uid, `Username`, `Password`) phục vụ đăng nhập độc lập.
- Khởi tạo EF Core Initial Migration `InitialCreate_UsersOnly` và seed data `admin` [BE-AUTH].
- Triển khai endpoints `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/logout` [BE-AUTH].
- Cung cấp `IAuthService` và `AuthService` xử lý xác thực tenant và sinh JWT token / Refresh Token.

## [0.1.0] - 2026-09-30

### Added

- Khung mẫu cấu hình Code-First Fluent API: `ProductConfiguration.cs`.
- Mẫu DTOs `ProductCreateRequest`, `ProductResponse`.
- Mẫu Service Interface `ICatalogService` & Service `CatalogService`.
- Controller mẫu mỏng `ProductsController` chỉ giao tiếp bằng DTO.
- Thư mục quản lý worklog nội bộ: `docs/worklogs/`.

### Fixed

- Lỗ hổng cô lập người thuê đa tầng (Multi-Tenant Isolation) trong `AppDbContext.cs`: chuyển sang đánh giá động runtime trong `HasQueryFilter`.
- Biên dịch 0 Warning, 0 Error.
  ádasd
