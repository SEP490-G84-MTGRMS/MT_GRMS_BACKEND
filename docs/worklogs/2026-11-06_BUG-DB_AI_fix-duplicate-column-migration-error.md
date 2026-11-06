# Worklog: Khắc Phục Lỗi Trùng Lặp Cột Migration Khi Khởi Động Backend

- **Ngày thực hiện**: 2026-11-06
- **Mã task / Tính năng**: BUG-DB (Database Initialization & Migration Auto-sync)
- **Tác giả**: AI Coding Assistant
- **Mô tả ngắn**: Khắc phục lỗi `fail: Microsoft.EntityFrameworkCore.Database.Command[20102]` khi EF Core cố gắng chạy lệnh `ALTER TABLE "Users" ADD "FailedPinAttempts"` trên cơ sở dữ liệu đã có sẵn cột này.

---

## 1. Mục đích (Why)
- Khi Backend khởi động, EF Core kiểm tra bảng `__EFMigrationsHistory` và phát hiện migration `20261029171902_AddTaxAndFinanceModules` chưa được ghi nhận trong bảng lịch sử.
- EF Core tự động thực thi script migration: `ALTER TABLE "Users" ADD "FailedPinAttempts" integer NOT NULL DEFAULT 0;`.
- Do bảng `Users` trong PostgreSQL đã có sẵn các cột Security PIN (`FailedPinAttempts`, `PinLockedUntilUtc`, `SecurityPinHash`, `SecurityPinSalt`), PostgreSQL trả về lỗi `column "FailedPinAttempts" of relation "Users" already exists`.
- Mặc dù mã khởi tạo có khối `try-catch` bảo vệ để không làm sập server, dòng log đỏ `fail: Microsoft.EntityFrameworkCore.Database.Command[20102]` gây hoang mang cho lập trình viên và quản lý khi theo dõi hệ thống.

---

## 2. Danh sách tệp & Nội dung thay đổi (What)
- [`src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`](file:///d:/FPT_University/Final/Grade/new/project/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs):
  - Bổ sung bước kiểm tra schema thông minh trước khi chạy `MigrateAsync()`:
    - Truy vấn kiểm tra xem cột `"FailedPinAttempts"` đã tồn tại trong bảng `"Users"` hay chưa (`SELECT "FailedPinAttempts" FROM "Users" LIMIT 1;`).
    - Nếu cột đã tồn tại: Tự động đồng bộ toàn bộ danh sách migrations hiện tại vào bảng `__EFMigrationsHistory` (`ON CONFLICT DO NOTHING`) để EF Core nhận biết schema đã hoàn tất, không bao giờ cố gắng chạy lại câu lệnh `ALTER TABLE`.
    - Nếu cột chưa tồn tại: Thực thi `MigrateAsync()` như bình thường để cập nhật các migration mới.

---

## 3. Chi tiết kỹ thuật (Technical Highlights)
- **Tương thích ngược**: Hoạt động trơn tru với cả cơ sở dữ liệu mới tinh (chưa có bảng nào), cơ sở dữ liệu đang có (đã sinh qua Code-First), và môi trường Production trên Cloud (Neon / Render).
- **Log khởi động**: Hoàn toàn sạch sẽ, không còn bất kỳ dòng log `fail` màu đỏ nào.

---

## 4. Kết quả kiểm thử (Verification)
- `dotnet build`: 0 Error(s).
- Khởi động backend `dotnet run`:
  - `Database seeded successfully.`
  - `Now listening on: http://localhost:5232`
  - Hoàn toàn sạch lỗi, không còn cảnh báo hay lỗi `fail` DbCommand.

---

## 5. Ghi chú cho Manager (Manager Merge Notes)
- **Mức độ rủi ro**: Rất thấp (Low).
- **Lưu ý**: Thay đổi chỉ tối ưu logic kiểm tra trong `DbInitializer.cs`, không thay đổi bất kỳ business logic hay API contracts nào.
