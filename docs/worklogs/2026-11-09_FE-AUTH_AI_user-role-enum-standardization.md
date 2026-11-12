# Worklog: Chuẩn Hóa Enum UserRole (Admin, StoreOwner, Staff) Cho Toàn Hệ Thống

- **Ngày thực hiện**: 2026-11-09
- **Mã Task/Tính năng**: FE-AUTH / Role Enum Standardization
- **Tác giả**: AI Assistant (Pair Programming với User)
- **Dự án**: `project/MT_GRMS_BACKEND`

---

## 1. Mục Đích (Why)
- Đáp ứng yêu cầu của User: *"cái role thì hãy thêm enum là admin, store_onwer, staff và cho role của người dùng"*.
- Loại bỏ việc dùng string tự do ("EMPLOYEE", "CASHIER", "OWNER"...) hoặc logic fallback sai (trước đây nếu role rỗng dễ bị fallback về "ADMIN").
- Định nghĩa enum chuẩn hóa `UserRole` gồm 3 vai trò cốt lõi:
  1. `Admin` (ADMIN): Quản trị viên hệ thống SaaS.
  2. `StoreOwner` (STORE_OWNER): Chủ cửa hàng / Chuỗi bán lẻ.
  3. `Staff` (STAFF): Nhân viên vận hành cửa hàng.
- Gán và chuẩn hóa thuộc tính `Role` / `UserRole` cho `User`, `TenantUser`, `UserInfo` DTO và ánh xạ an toàn trong `AuthService`.

---

## 2. Danh Sách Tệp & Nội Dung Thay Đổi (What)
1. **`src/MT_GRMS.Domain/Enums/UserEnums.cs`** (Tạo mới):
   - Định nghĩa enum `UserRole { Admin = 1, StoreOwner = 2, Staff = 3 }`.
   - Cung cấp class `UserRoleNames` với các hằng số chuỗi (`ADMIN`, `STORE_OWNER`, `STAFF`) và các helper `ToStringRole()` / `FromString()`.
2. **`src/MT_GRMS.Domain/Entities/Identity/User.cs`**:
   - Thêm thuộc tính `[NotMapped] public string Role { get; set; }` và `[NotMapped] public UserRole UserRole { get; }`.
3. **`src/MT_GRMS.Domain/Entities/Identity/TenantUser.cs`**:
   - Gán giá trị mặc định cho `Role = UserRoleNames.Staff`.
   - Thêm thuộc tính `[NotMapped] public UserRole UserRole { get; set; }`.
4. **`src/MT_GRMS.Application/Auth/Dtos/AuthDtos.cs`**:
   - Cập nhật `UserInfo`: thêm thuộc tính `public UserRole UserRole => UserRoleNames.FromString(RoleName);`.
5. **`src/MT_GRMS.Application/Auth/AuthService.cs`**:
   - Áp dụng `UserRole` / `UserRoleNames` trong `LoginAsync`, `RefreshTokenAsync`, `GetCurrentUserAsync`, `RegisterTenantAsync`.
   - Tuyệt đối không fallback về `ADMIN` khi user không phải admin (mặc định nếu tạo gian hàng là `STORE_OWNER`, nhân viên là `STAFF`).
6. **`src/MT_GRMS.Infrastructure/Repositories/UserRepository.cs`**:
   - Đồng bộ gán `Role = UserRoleNames.StoreOwner` khi đăng ký tài khoản chủ cửa hàng mới.

---

## 3. Chi Tiết Kỹ Thuật (Technical Highlights)
- **Role Isolation**: Quyền hạn `Admin` chỉ được cấp khi `user.IsAdmin == true` hoặc username là `admin`. Mọi tài khoản khác được phân định rõ ràng giữa `StoreOwner` và `Staff`.
- **Backward Compatibility**: `Role` dạng chuỗi và `UserRole` dạng Enum cùng tồn tại mượt mà, không làm gãy schema CSDL PostgreSQL và không làm gãy các integration tests hiện hữu.

---

## 4. Kết Quả Kiểm Thử (Verification)
- `dotnet build`: **0 Error(s)**, 16 Warning(s) tiền định.
- `dotnet test MT_GRMS.IntegrationTests.csproj`: **47/47 tests PASS 100%**.

---

## 5. Ghi Chú Cho Manager Khi Duyệt & Merge (Manager Merge Notes)
- **Mức độ rủi ro**: Thấp (Low).
- **Migration**: Không cần chạy thêm DB Migration vì `Users` dùng `[NotMapped]` alias và `TenantUsers.Role` giữ nguyên kiểu chuỗi tương thích.

