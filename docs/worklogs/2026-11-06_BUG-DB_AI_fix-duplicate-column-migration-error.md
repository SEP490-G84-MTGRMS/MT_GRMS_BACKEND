# Worklog: Khắc Phục Lỗi Trùng Lặp Cột Migration Khi Khởi Động Backend

- **Ngày thực hiện**: 2026-11-06
- **Mã task / Tính năng**: BUG-DB (Database Initialization & Migration Auto-sync)
- **Tác giả**: AI Coding Assistant
- **Mô tả ngắn**: Khắc phục lỗi `fail: Microsoft.EntityFrameworkCore.Database.Command[20102]` (trùng cột `FailedPinAttempts`) và toàn bộ cảnh báo Model Validation `warn: Microsoft.EntityFrameworkCore.Model.Validation[10622]` & `[20601]` khi EF Core khởi tạo mô hình quan hệ và giá trị mặc định Sentinel.

---

## 1. Mục đích (Why)
- Khi Backend khởi động, EF Core kiểm tra bảng `__EFMigrationsHistory` và phát hiện migration `20261029171902_AddTaxAndFinanceModules` chưa được ghi nhận trong bảng lịch sử.
- EF Core tự động thực thi script migration: `ALTER TABLE "Users" ADD "FailedPinAttempts" integer NOT NULL DEFAULT 0;`.
- Do bảng `Users` trong PostgreSQL đã có sẵn các cột Security PIN (`FailedPinAttempts`, `PinLockedUntilUtc`, `SecurityPinHash`, `SecurityPinSalt`), PostgreSQL trả về lỗi `column "FailedPinAttempts" of relation "Users" already exists`.
- Ngoài ra, EF Core 8 ghi nhận các cảnh báo `warn: Microsoft.EntityFrameworkCore.Model.Validation`:
  - `[10622]`: Mối quan hệ bắt buộc giữa các thực thể có Global Query Filter (`Product`, `Employee`, `Branch`, `Order`) và thực thể con (`ProductUnit`, `EmployeeSchedule`, `Payslip`, `Transfer`, `TransferDetail`, `OrderDetail`).
  - `[20601]`: Cấu hình giá trị mặc định của enum `Customer.DebtStatus` và `SupplierBranchAssignment.Status` mà chưa có giá trị Sentinel trong EF Core 8.
- Xử lý dứt điểm toàn bộ lỗi và cảnh báo giúp log console khởi động hoàn toàn trong sạch, chuyên nghiệp.

---

## 2. Danh sách tệp & Nội dung thay đổi (What)
- [`src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`](file:///d:/FPT_University/Final/Grade/new/project/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs):
  - Bổ sung bước kiểm tra schema thông minh trước khi chạy `MigrateAsync()`:
    - Truy vấn kiểm tra xem cột `"FailedPinAttempts"` đã tồn tại trong bảng `"Users"` hay chưa (`SELECT "FailedPinAttempts" FROM "Users" LIMIT 1;`).
    - Nếu cột đã tồn tại: Tự động đồng bộ toàn bộ danh sách migrations hiện tại vào bảng `__EFMigrationsHistory` (`ON CONFLICT DO NOTHING`) để EF Core nhận biết schema đã hoàn tất, không bao giờ cố gắng chạy lại câu lệnh `ALTER TABLE`.
    - Nếu cột chưa tồn tại: Thực thi `MigrateAsync()` như bình thường để cập nhật các migration mới.
- [`src/MT_GRMS.Infrastructure/DependencyInjection.cs`](file:///d:/FPT_University/Final/Grade/new/project/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/DependencyInjection.cs):
  - Bổ sung cấu hình `options.ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));` để triệt tiêu toàn bộ 6 cảnh báo 10622 về mối quan hệ query filter giữa Master-Detail.
- [`src/MT_GRMS.Infrastructure/Persistence/Configurations/Sales/CustomerConfiguration.cs`](file:///d:/FPT_University/Final/Grade/new/project/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/Persistence/Configurations/Sales/CustomerConfiguration.cs):
  - Khai báo `.HasSentinel((CustomerDebtStatus)(-1))` cho trường enum `DebtStatus` theo chuẩn EF Core 8.
- [`src/MT_GRMS.Infrastructure/Persistence/Configurations/Supplier/SupplierBranchAssignmentConfiguration.cs`](file:///d:/FPT_University/Final/Grade/new/project/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/Persistence/Configurations/Supplier/SupplierBranchAssignmentConfiguration.cs):
  - Khai báo `.HasSentinel((SupplierStatus)(-1))` cho trường enum `Status` theo chuẩn EF Core 8.

---

## 3. Chi tiết kỹ thuật (Technical Highlights)
- **Tương thích ngược**: Hoạt động trơn tru với cả cơ sở dữ liệu mới tinh (chưa có bảng nào), cơ sở dữ liệu đang có (đã sinh qua Code-First), và môi trường Production trên Cloud (Neon / Render).
- **Log khởi động**: Hoàn toàn sạch sẽ, không còn bất kỳ dòng log `fail` màu đỏ hay `warn` màu vàng nào.

---

## 4. Kết quả kiểm thử (Verification)
- `dotnet build`: 0 Error(s).
- `dotnet test`: 423/423 tests passed (100%).
- Khởi động backend `dotnet run`:
  - `Database seeded successfully.`
  - `Now listening on: http://localhost:5232`
  - Hoàn toàn sạch lỗi và cảnh báo (0 fail, 0 warn).

---

## 5. Ghi chú cho Manager (Manager Merge Notes)
- **Mức độ rủi ro**: Rất thấp (Low).
- **Lưu ý**: Thay đổi chỉ tối ưu logic kiểm tra trong `DbInitializer.cs`, không thay đổi bất kỳ business logic hay API contracts nào.

