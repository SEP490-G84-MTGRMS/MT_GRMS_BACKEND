# WORKLOG: [BE-AUTH] - auth-login-api

- **Ngày thực hiện**: 2026-10-11
- **Người thực hiện**: Dev
- **Nhánh Git (Branch)**: `dev`
- **Mã tính năng liên kết**: [BE-AUTH](../../../DOCS_MT_GRMS/specs/BE-AUTH/spec.md)
- **Loại thay đổi**: Feature / API / Authentication

---

### 1. Mục Đích Thay Đổi (Why)
- Triển khai phân hệ Xác thực (Authentication) tại tầng Backend ASP.NET Core 8:
  - Cung cấp endpoint `POST /api/auth/login` tiếp nhận `LoginRequest` (Username/Email, Password, TenantCode) và cấp trả `LoginResponse` (AccessToken, RefreshToken, UserInfo).
  - Cung cấp endpoint `POST /api/auth/refresh` hỗ trợ cơ chế Refresh Token kéo dài phiên làm việc.
  - Cung cấp endpoint `POST /api/auth/logout` hủy phiên đăng nhập.
  - Tích hợp `IAuthService` & `AuthService` kế thừa `IUserRepository`.

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)
- `src/MT_GRMS.Application/Auth/`:
  - `IAuthService.cs`: Bổ sung định nghĩa `LoginAsync` và `RefreshTokenAsync`.
  - `AuthService.cs`: Hiện thực logic tìm kiếm User, xác thực tenant và sinh JWT token / Refresh Token.
- `src/MT_GRMS.Api/Controllers/`:
  - `AuthController.cs`: Controller đón nhận HTTP Request từ client.

### 3. Kiểm Thử & Xác Nhận (Verification)
- [x] Đã chạy `dotnet build`: Build thành công (0 Warning, 0 Error).
- [x] Đã chạy `dotnet test`: 100% Unit Tests & Integration Tests PASS.
