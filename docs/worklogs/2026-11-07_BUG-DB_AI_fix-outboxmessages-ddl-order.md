# Worklog: Sửa Lỗi Thứ Tự DDL Khi Khởi Tạo CSDL (OutboxMessages & SupplierDispatchPackages)

- **Ngày thực hiện**: 2026-11-07
- **Mã Task/FE**: BUG-DB-03
- **Người thực hiện**: AI Coding Agent
- **Mô tả ngắn**: Khắc phục lỗi lặp liên tục `42P01: relation "OutboxMessages" does not exist` từ worker `SupplierOrderDispatchWorker` và lỗi `Failed executing DbCommand` khi khởi chạy Backend do thứ tự chạy câu lệnh DDL trong `DbInitializer.cs`.

---

### 1. Mục đích (Why)
- Khi chạy `dotnet run`, tiến trình chạy ngầm `SupplierOrderDispatchWorker` định kỳ quét bảng `"OutboxMessages"` để gửi đơn mua hàng tới nhà cung cấp.
- Tuy nhiên, bảng `"OutboxMessages"` không tồn tại trong CSDL, khiến console log liên tục báo lỗi đỏ `42P01: relation "OutboxMessages" does not exist`.
- Nguyên nhân: Trong hàm `EnsureSupplierDispatchSchemaAsync` của `DbInitializer.cs`, câu lệnh `ALTER TABLE "SupplierDispatchPackages"` được đặt TRƯỚC câu lệnh `CREATE TABLE IF NOT EXISTS "SupplierDispatchPackages"`. Do bảng chưa được tạo, câu lệnh `ALTER TABLE` bị lỗi, khiến toàn bộ khối SQL bị ngắt và câu lệnh `CREATE TABLE IF NOT EXISTS "OutboxMessages"` ở phía sau không bao giờ được chạy.
- Ngoài ra, khối DDL nâng cấp bảng `Products` có chứa đoạn trùng lặp lớn các bảng HR, nếu có bất kỳ lỗi nào sẽ khiến toàn bộ lệnh SQL bị hủy.

---

### 2. Danh sách file & Nội dung đã sửa (What)

1. **`project/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`**:
   - Tái cấu trúc hàm `EnsureSupplierDispatchSchemaAsync`:
     - Tách thành 3 khối lệnh SQL riêng biệt có `try/catch` và ghi log chi tiết:
       1. Khối 1: `CREATE TABLE IF NOT EXISTS` cho 4 bảng (`SupplierDispatchPackages`, `SupplierDispatchPackageItems`, `PurchaseOrderRevisions`, `OutboxMessages`).
       2. Khối 2: `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` cho các bảng đã tồn tại (`PurchaseOrders`, `PurchaseOrderDetails`, `SupplierBranch`, `SupplierDispatchPackages`).
       3. Khối 3: Tạo Unique Indexes và drop index cũ an toàn.
   - Bỏ đoạn mã DDL trùng lặp các bảng HR ở khối nâng cấp `Products/Users` (vì các bảng HR đã có khối DDL độc lập ở phần sau).
   - Truyền `logger` vào `EnsureSupplierDispatchSchemaAsync(context, logger)` để theo dõi chi tiết nếu có cảnh báo.

---

### 3. Kết quả kiểm thử (Verification)
- Chạy thử `dotnet run --project project/MT_GRMS_BACKEND/src/MT_GRMS.Api/MT_GRMS.Api.csproj`:
  - Toàn bộ các bảng `OutboxMessages`, `SupplierDispatchPackages`, `PurchaseOrderRevisions` được tạo thành công ngay lần đầu chạy.
  - Worker `SupplierOrderDispatchWorker` thực thi chu kỳ quét định kỳ (`SELECT ... FROM "SupplierDispatchPackages"`) với kết quả `Executed DbCommand (1ms)` thành công 100%.
  - Server lắng nghe tại `http://localhost:5232`, console log hoàn toàn sạch không còn bất kỳ lỗi `relation does not exist` nào.

---

### 4. Ghi chú cho Manager (Manager Merge Notes)
- **Mức độ rủi ro**: Rất thấp (Low Risk - chỉ sửa thứ tự DDL lũy đẳng `CREATE TABLE IF NOT EXISTS` và `ALTER TABLE`).
- **Files Manager cần lưu tâm**: `src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`.
- **Thao tác hậu merge**: Không cần chạy lệnh tay, `DbInitializer` sẽ tự động tạo bảng khi server khởi động.

