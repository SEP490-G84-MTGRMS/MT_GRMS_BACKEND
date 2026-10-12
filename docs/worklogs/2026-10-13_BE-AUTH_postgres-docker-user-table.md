# WORKLOG: BE-AUTH - Triển khai PostgreSQL Docker & Bảng Users Tối Giản

- **Ngày thực hiện**: 2026-10-13
- **Người thực hiện**: Ortus24 (AI Team Kit)
- **Nhánh Git (Branch)**: `feature/log-in`
- **Mã tính năng liên kết**: [BE-AUTH / FE-01](../../../DOCS_MT_GRMS/specs/FE-01/spec.md)
- **Loại thay đổi**: Architecture / Feature

---

### 1. Mục Đích Thay Đổi (Why)
- Triển khai hạ tầng cơ sở dữ liệu PostgreSQL 16 Alpine chạy cục bộ qua Docker Compose cho MT-GRMS.
- Tối giản hóa thực thể và bảng `users` chỉ gồm đúng 3 trường (`Id` string Uid, `Username`, `Password`) theo đúng yêu cầu để phục vụ nhanh chóng và độc lập cho tính năng đăng nhập Authentication.
- Khởi tạo EF Core Initial Migration chỉ sinh duy nhất bảng `users` và auto-seed tài khoản quản trị mẫu `admin` / `Admin@123`.

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)
- `docker-compose.yml`: Dịch vụ PostgreSQL 16 Alpine, port 5432, persistent volume `postgres_data`, auto-mount init script.
- `init-db.sql`: Script SQL khởi tạo bảng `users` và nạp seed user `admin`.
- `src/MT_GRMS.Domain/Entities/Identity/User.cs`: Tối giản hóa thực thể với `Id` (string Uid), `Username`, `Password`.
- `src/MT_GRMS.Infrastructure/Persistence/Configurations/UserConfiguration.cs`: Fluent API ánh xạ bảng `users`, PK `id`, unique index `username`.
- `src/MT_GRMS.Infrastructure/Persistence/AppDbContext.cs`: Đăng ký `DbSet<User> Users` và áp dụng `UserConfiguration`.
- `src/MT_GRMS.Infrastructure/Persistence/Migrations/20261012174712_InitialCreate_UsersOnly.cs`: Initial Migration chỉ sinh duy nhất bảng `users`.
- `src/MT_GRMS.Application/Auth/`: Cập nhật `IUserRepository`, `UserRepository`, `UserInfo` và `AuthService` tương thích `string Id`.
- `tests/MT_GRMS.UnitTests/UserAuthenticationTests.cs`: Unit test xác thực User và AuthService.
- `tests/MT_GRMS.IntegrationTests/UserDatabaseIntegrationTests.cs`: Integration test DbContext với UserRepository.

### 3. Kiểm Thử & Xác Nhận (Verification)
- [x] Đã chạy `dotnet build MT_GRMS_BACKEND.sln`: Thành công (0 lỗi, 0 cảnh báo).
- [x] Đã chạy `dotnet test MT_GRMS_BACKEND.sln`: 6/6 tests PASS 100%.
- [x] Đã kiểm tra tính toàn vẹn của Migration và script SQL.

### 4. Ghi Chú Cho PM & Frontend
- Bảng `users` đã sẵn sàng trong PostgreSQL với tài khoản mẫu: `admin` / `Admin@123`.
- Để chạy database local: mở Docker Desktop và chạy `docker compose up -d` tại thư mục `MT_GRMS_BACKEND`.
