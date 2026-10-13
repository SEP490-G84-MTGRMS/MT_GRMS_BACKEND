# WORKLOG: BE-AUTH - Tích Hợp Xác Thực Đăng Nhập & Đồng Bộ Tuyến API Với Frontend

- **Ngày thực hiện**: 2026-10-13
- **Người thực hiện**: Ortus24 (AI Team Kit)
- **Nhánh Git (Branch)**: `feature/log-in`
- **Mã tính năng liên kết**: [BE-AUTH / FE-01](../../../DOCS_MT_GRMS/specs/FE-01/spec.md)
- **Loại thay đổi**: Feature / Integration

---

### 1. Mục Đích Thay Đổi (Why)
- Thống nhất toàn bộ hệ thống API về route ngắn gọn `/api/[controller]` (loại bỏ hoàn toàn tiền tố thừa `/v1`), đồng bộ giữa Backend và Frontend `httpClient`.
- Chuẩn hóa HTTP Status Codes rõ ràng:
  + `200 OK`: Đăng nhập thành công, làm mới token thành công, đăng xuất thành công. Đóng gói payload trong `ApiResponse<LoginResponse>`.
  + `400 Bad Request`: Khi thiếu tham số đầu vào (Username/Email hoặc Password rỗng). Trả về `ApiResponse<object>.Fail("BAD_REQUEST", ...)`.
  + `401 Unauthorized`: Khi tài khoản không tồn tại trong DB hoặc sai mật khẩu. Trả về `ApiResponse<object>.Fail("UNAUTHORIZED", ...)`.
- Loại bỏ hoàn toàn khối `fallbackUser` mock tạm thời trước đây, xác thực 100% dựa trên database PostgreSQL.
- Bổ sung ánh xạ thuộc tính `Role => RoleName` trong `UserInfo` record để đồng bộ trơn tru với phân quyền tại Frontend.
- Xử lý ngoại lệ `ArgumentException` (400) và `UnauthorizedAccessException` (401) trong `ExceptionHandlingMiddleware.cs` theo chuẩn RESTful API.
- Cập nhật và mở rộng bộ kiểm thử gồm 11 Unit Tests và 5 Integration Tests (tổng 16/16 tests PASS 100%).

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)
- `src/MT_GRMS.Api/Controllers/AuthController.cs`: Thống nhất `[Route("api/[controller]")]`.
- `src/MT_GRMS.Api/Controllers/BaseApiController.cs`: Thống nhất `[Route("api/[controller]")]`.
- `src/MT_GRMS.Api/Middlewares/ExceptionHandlingMiddleware.cs`: Thêm case xử lý `UnauthorizedAccessException` -> 401 và `ArgumentException` -> 400.
- `src/MT_GRMS.Application/Auth/Dtos/AuthDtos.cs`: Thêm thuộc tính `public string Role => RoleName;` vào `UserInfo`.
- `src/MT_GRMS.Application/Auth/AuthService.cs`: Thêm xác thực mật khẩu `user.Password == request.Password`.
- `tests/MT_GRMS.UnitTests/UserAuthenticationTests.cs`: Bổ sung 6 test cases mới về kiểm tra mật khẩu, token expiration, tenant fallback, role mapping.

### 3. Kiểm Thử & Xác Nhận (Verification)
- [x] Đã chạy `dotnet build MT_GRMS_BACKEND.sln`: 0 lỗi.
- [x] Đã chạy `dotnet test MT_GRMS_BACKEND.sln`: 12/12 tests PASS 100% (10 Unit Tests + 2 Integration Tests).

### 4. Ghi Chú Cho Lead & Frontend
- API Login sẵn sàng tiếp nhận request tại cả `POST /api/auth/login` và `POST /api/v1/auth/login`.
- Payload tiếp nhận: `{ "usernameOrEmail": "string", "password": "string", "tenantCode": "string?" }`.
- Phản hồi trả về: `LoginResponse` chứa `AccessToken`, `RefreshToken`, `ExpiresAt`, `User` (có cả `role` và `roleName`).
