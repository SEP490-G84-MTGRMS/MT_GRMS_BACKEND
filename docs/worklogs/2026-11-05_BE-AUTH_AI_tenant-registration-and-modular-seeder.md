# Worklog: Implement Tenant Registration API and Modular Idempotent Seeder

- **Task/Feature**: BE-AUTH / BE-DB
- **Author**: AI Coding Assistant
- **Date**: 2026-11-05
- **Branch**: `feature/login-screen`

---

## 1. Mục đích (Why)
- **Vấn đề 1 (Seeder Neon PostgreSQL không có data)**: `DbInitializer.cs` gặp 2 lỗi nghiêm trọng:
  1. Dòng 155 kiểm tra `if (await context.SubscriptionPlans.AnyAsync()) return;` khiến việc khởi tạo bị dừng ngay lập tức (early return) nếu bảng subscription đã có dữ liệu, bỏ qua toàn bộ việc nạp `Users`, `Tenants`, `Branches`.
  2. Dữ liệu mẫu `userStoreOwner` và `userAdmin` đều dùng chung số điện thoại `"0901234567"`, vi phạm ràng buộc Unique Index `IX_Users_Phone` trên PostgreSQL khiến transaction nạp `Users` bị abort. Mật khẩu mẫu của `userStoreOwner` và `userStaff` chưa được hash bằng BCrypt.
- **Vấn đề 2 (Đăng ký tài khoản thất bại)**: Frontend gửi request đăng ký tài khoản chủ cửa hàng đến `POST /api/v1/auth/register`, nhưng Backend chỉ có endpoint `/login`, `/refresh`, `/logout`, trả về `404 Not Found`.

---

## 2. Chi tiết thay đổi (What & Where)

### A. Modular Idempotent Seeder (`DbInitializer.cs`)
- Bỏ cơ chế early return toàn cục. Chia nhỏ việc kiểm tra thành từng module độc lập:
  - `SubscriptionPlans`: chỉ nạp khi chưa có plan code tương ứng.
  - `Tenants`: chỉ nạp khi chưa có tenant `tenant_mt_mart_01`.
  - `Users`: chỉ nạp khi chưa có user `admin`.
  - `Branches`: chỉ nạp khi chưa có branch mặc định.
- Đổi số điện thoại `userStoreOwner.Phone = "0901234568"` để không trùng lặp với `admin.Phone = "0901234567"`.
- Băm mật khẩu `userStoreOwner` và `userStaff` bằng `PasswordHasher.Hash("123456")`.
- Cố định ID cho `employeeManager` (`usr_store_manager`) và `employeeCashier` (`usr_cashier`).

### B. Atomic Tenant Registration API (`POST /api/v1/auth/register`)
- **DTOs (`AuthDtos.cs`)**:
  - Thêm `RegisterTenantRequest(FullName, StoreName, Phone, Email, Password, AgreeTerms)`.
- **Interface & Service (`IAuthService.cs`, `AuthService.cs`)**:
  - Thêm method `RegisterTenantAsync(RegisterTenantRequest request, CancellationToken cancellationToken)`.
  - Tự động sinh `Tenant` (mã ngẫu nhiên, slug, plan FREE), sinh `User` (role `STORE_OWNER`, hash password), tạo `TenantUser` liên kết và tạo `Branch` mặc định ("Chi nhánh chính").
- **Repository (`IUserRepository.cs`, `UserRepository.cs`)**:
  - `ExistsByPhoneOrEmailAsync`: kiểm tra trùng SĐT/Email.
  - `RegisterStoreOwnerAsync`: thực thi lưu `Tenant`, `User`, `TenantUser`, `Branch` trong một đơn vị công việc (Unit of Work) nguyên tử thông qua `SaveChangesAsync`. Khắc phục triệt để lỗi xung đột giao dịch thủ công với `NpgsqlRetryingExecutionStrategy` trên PostgreSQL.
- **Controller (`AuthController.cs`)**:
  - Thêm endpoint `POST /api/v1/auth/register`, trả về `ApiResponse<LoginResponse>` chứa JWT Token và thông tin User để frontend đăng nhập ngay.
- **Tests (`TenantStoreControllerTests.cs`, `AuthControllerTests.cs`, `UserAuthenticationTests.cs`)**:
  - Cập nhật mock repository `FakeUserRepository`.
  - Bổ sung Unit Test `AuthController_Register_Should_Return_Ok_200_When_Valid`.

---

## 3. Kỹ thuật quan trọng (Technical Highlights)
- **API Endpoint**: `POST /api/v1/auth/register` (hoặc `/api/auth/register`)
- **Request Body**:
  ```json
  {
    "fullName": "Nguyễn Văn A",
    "storeName": "Cửa Hàng Thực Phẩm Sạch",
    "phone": "0987654321",
    "email": "owner@example.com",
    "password": "Password123@",
    "agreeTerms": true
  }
  ```
- **Response**: `ApiResponse<LoginResponse>` (HTTP 200) kèm Bearer JWT Access Token.
- **Tính toàn vẹn CSDL**: Multi-entity atomic commit (Tenant, User, TenantUser, Branch) bảo đảm không có dữ liệu mồ côi nếu có lỗi phát sinh.

---

## 4. Kết quả kiểm thử (Verification)
- `dotnet build`: Succeeded (0 Errors, 0 Warnings nghiêm trọng).
- `dotnet test`: **Passed 451/451 tests** (Unit Tests: 407, Integration Tests: 44).

---

## 5. Hướng dẫn cho Manager khi Review & Merge
- **Mức độ rủi ro**: Thấp (Low).
- **Hành động cần làm sau khi push lên Render**:
  - Backend tự động chạy `DbInitializer.InitializeAsync` khi khởi động trên Render:
    1. Kiểm tra bảng `Users` và `Tenants` trên Neon Console: bảng sẽ có ngay dữ liệu tài khoản quản trị `admin` / `Admin@123` và chủ cửa hàng `store_owner` / `123456`.
    2. Thử nghiệm tính năng đăng ký trên màn hình Đăng ký của Frontend: thông tin tài khoản và Tenant mới sẽ được tạo thành công vào CSDL Neon.

