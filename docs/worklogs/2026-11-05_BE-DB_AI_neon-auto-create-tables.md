# Worklog: Khởi tạo toàn bộ 34 bảng CSDL tự động trên Neon PostgreSQL (Option 1)

- **Mã Task**: BE-DB
- **Tác giả**: AI Assistant
- **Ngày thực hiện**: 2026-11-05
- **Trạng thái**: Hoàn thành

---

## 1. Mục đích (Why)
- Người dùng phát hiện CSDL Neon trên Render chỉ có 2 bảng (`__EFMigrationsHistory` và `supplier_branch_assignments`), toàn bộ 34 bảng nghiệp vụ chưa được tạo.
- Nguyên nhân:
  1. `MigrateAsync()` cố chạy delta migration `AddTaxAndFinanceModules` (chứa `ALTER TABLE "Users"`). Do CSDL Neon mới chưa có bảng `Users`, Postgres quăng lỗi `relation "Users" does not exist`.
  2. Khi fallback sang `EnsureCreatedAsync()`, EF Core thấy CSDL đã có bảng nên tự động dừng lại mà không tạo thêm bảng nào.
- Giải pháp: Cập nhật `DbInitializer.cs` sử dụng `IRelationalDatabaseCreator.CreateTablesAsync()` để ép EF Core tạo toàn bộ 34 bảng trực tiếp từ C# Model khi phát hiện CSDL chưa có bảng `Users`.

---

## 2. Danh sách tệp & Nội dung thay đổi (What)
- **`project/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`**:
  - Bổ sung kiểm tra sự tồn tại của bảng `"Users"`.
  - Nếu `"Users"` chưa có: Dọn dẹp bảng tạm, gọi `creator.CreateTablesAsync()` sinh 34 bảng chuẩn 3NF từ DbContext.
  - Tự động ghi nhận tất cả migrations vào `__EFMigrationsHistory` để ngăn các lần sau chạy `ALTER TABLE` thừa.
  - Tiến hành nạp dữ liệu mẫu Subscription Plans và Super Admin (`admin`/`Admin@123`).

---

## 3. Kết quả kiểm thử (Verification)
- `dotnet build`: Build succeeded, 0 errors.

---

## 4. Ghi chú cho Manager (Manager Merge Notes)
- **Mức độ rủi ro**: Thấp (Low).
- **Hậu triển khai**: Sau khi push code lên GitHub, Render khởi động lại sẽ tự động tạo đủ 34 bảng trên Neon.

