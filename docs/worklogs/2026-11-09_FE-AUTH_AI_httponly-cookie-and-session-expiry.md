# Worklog: Triển Khai Xác Thực HttpOnly Cookie Toàn Diện & Xử Lý Hết Hạn Phiên

- **Ngày thực hiện**: 2026-11-09
- **Tác giả**: AI Assistant (Antigravity)
- **Mã Task/FE**: FE-AUTH / REQ-AUTH-04..07
- **Project**: MT_GRMS_BACKEND

---

## 1. Mục đích (Why)
- Đáp ứng yêu cầu chuyển đổi toàn diện kiến trúc xác thực người dùng từ việc lưu chuỗi token trong `localStorage` sang **100% HttpOnly Cookie**.
- Bảo vệ hệ thống khỏi tấn công XSS Token Theft (mã độc JavaScript không thể đọc được token từ Cookie có cờ `HttpOnly`).
- Triển khai cơ chế xoay vòng Refresh Token (Token Rotation) trên CSDL PostgreSQL bảng `Users`, cấp AccessToken/RefreshToken mới mỗi lần refresh và thu hồi token khi đăng xuất.
- Bổ sung endpoint `GET /api/auth/me` phục vụ hydrate trạng thái đăng nhập cho Frontend khi người dùng F5 hoặc mở tab mới.

---

## 2. Danh Sách Tệp & Nội Dung Đã Thay Đổi (What)

### `src/MT_GRMS.Api/Program.cs`
- Bổ sung `JwtBearerEvents.OnMessageReceived`: tự động đọc `access_token` từ HttpOnly Cookie đính kèm theo request (bên cạnh header `Authorization: Bearer` sẵn có).
- CORS policy `AllowFrontend` tiếp tục kích hoạt `.AllowCredentials()`.

### `src/MT_GRMS.Api/Controllers/AuthController.cs`
- Thêm route kép `[Route("api/[controller]")]` và `[Route("api/v1/[controller]")]`.
- Bổ sung 2 phương thức bảo mật `SetAuthCookies` và `DeleteAuthCookies`:
  - `access_token`: `HttpOnly`, `SameSite=Lax`, `Path=/`, `Expires=expiresAt`.
  - `refresh_token`: `HttpOnly`, `SameSite=Lax`, `Path=/api/auth`, `Expires=7 ngày`.
- `POST /api/auth/login`: Ghi HttpOnly Cookie và trả về `ApiResponse<UserInfo>` (Response body không chứa chuỗi token nhạy cảm).
- `POST /api/auth/register`: Ghi HttpOnly Cookie và trả về `ApiResponse<UserInfo>`.
- `POST /api/auth/refresh`: Đọc `refresh_token` từ Cookie (hoặc body), gọi `RefreshTokenAsync`, ghi đè cookies mới (Token Rotation) và trả về `ApiResponse<UserInfo>`. Nếu hết hạn/không hợp lệ, xóa cookies và trả về HTTP 401 Unauthorized.
- `GET /api/auth/me`: Đọc danh tính từ JWT Cookie qua `ClaimsPrincipal`, tra cứu user qua `GetCurrentUserAsync`, trả về `ApiResponse<UserInfo>`.
- `POST /api/auth/logout`: Xóa cookies (`Max-Age=0`) và thu hồi Refresh Token trong DB qua `RevokeRefreshTokenAsync`.

### `src/MT_GRMS.Application/Auth/IAuthService.cs` & `AuthService.cs`
- Bổ sung phương thức `GetCurrentUserAsync(string userId)` và `RevokeRefreshTokenAsync(string userId)`.
- Cập nhật `LoginAsync`: lưu `user.RefreshToken = refreshToken`, `user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7)` và `user.LastLoginAt = DateTime.UtcNow` vào CSDL `Users`.
- Cập nhật `RefreshTokenAsync`: tra cứu user theo `RefreshToken` trong DB, kiểm tra hạn sử dụng `RefreshTokenExpiryTime > DateTime.UtcNow`, sinh cặp token mới (Token Rotation) và cập nhật CSDL. Ném `UnauthorizedAccessException` nếu hết hạn hoặc không tìm thấy.

### `tests/MT_GRMS.IntegrationTests/AuthControllerTests.cs`
- Cập nhật các assertions kiểm tra `ApiResponse<UserInfo>` cho `Login`, `Register`, `Logout`.
- Bổ sung 3 test cases:
  - `AuthController_Refresh_Should_Return_Ok_200_When_Valid_RefreshToken` (xác minh Token Rotation trong CSDL).
  - `AuthController_Refresh_Should_Return_Unauthorized_401_When_Token_Expired` (xác minh từ chối token quá hạn).
  - `AuthController_Refresh_Should_Return_Unauthorized_401_When_Token_Missing` (xác minh từ chối khi không có cookie).

---

## 3. Kết Quả Kiểm Thử (Verification)
- `dotnet build src/MT_GRMS.Api/MT_GRMS.Api.csproj`: **0 Errors, 0 Warnings**.
- `dotnet test tests/MT_GRMS.IntegrationTests/MT_GRMS.IntegrationTests.csproj`: **47/47 tests PASS 100%**.

---

## 4. Lưu Ý Cho Quản Lý Khi Merge (Manager Merge Notes)
- **Mức độ rủi ro**: Thấp (Low). Toàn bộ logic tương thích ngược: endpoint `/api/auth/refresh` vẫn hỗ trợ đọc cả từ Cookie và request body.
- **Thao tác hậu merge**: Khởi động lại backend để áp dụng pipeline đọc HttpOnly Cookie. Không cần migration mới vì các cột `RefreshToken` và `RefreshTokenExpiryTime` đã có sẵn trong bảng `Users`.

