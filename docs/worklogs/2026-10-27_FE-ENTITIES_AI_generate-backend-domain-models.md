# Worklog: 2026-10-27 — FE-ENTITIES — Khởi tạo và chuẩn hóa 34 Domain Entities và EF Core Mapping từ MT_GRMS_Full_Database.sql

- **Author**: Antigravity AI Assistant
- **Task/Feature**: FE-ENTITIES (`generate-backend-domain-models`)
- **Dự án**: `project/MT_GRMS_BACKEND`
- **Mức độ rủi ro (Risk Level)**: Medium (Bổ sung mô hình thực thể toàn diện cho 7 phân hệ, cấu hình Fluent API và DbContext)

---

## 1. Mục đích (Why)
- Đồng bộ hóa toàn diện mô hình dữ liệu trong backend `MT_GRMS_BACKEND` (.NET 8 Clean Architecture) với cơ sở dữ liệu hoàn chỉnh 34 bảng thuộc 7 phân hệ nghiệp vụ chuẩn 3NF từ `MT_GRMS_Full_Database.sql`.
- Đảm bảo tính nhất quán của Clean Architecture: tầng Domain độc lập, tách biệt thực thể Global và Tenant-scoped, kế thừa `BaseTenantEntity` để tự động hóa cô lập dữ liệu (Multi-tenancy Global Query Filter) và Soft Delete.
- Hoàn thiện tầng Persistence trong Infrastructure với Fluent API tường minh cho khóa chính phức hợp (composite key), khóa ngoại, chỉ mục (index), precision tiền tệ `decimal(18,2)` và optimistic concurrency token `RowVersion`.

---

## 2. Danh sách file & Nội dung sửa (What)
- **Tầng Domain (`MT_GRMS.Domain`)**:
  - `Common/Uuid7.cs`: Khởi tạo bộ sinh UUIDv7 chuẩn RFC 9562 tuần tự theo thời gian (48-bit timestamp + entropy ngẫu nhiên) cho .NET 8.
  - `Common/BaseEntity.cs`: Khởi tạo lớp thực thể cơ sở toàn cục (`Id`, `CreatedAt`, `UpdatedAt`, `IsDeleted`), sử dụng `Uuid7.NewGuid()`.
  - `Common/BaseTenantEntity.cs`: Kế thừa `BaseEntity`, bổ sung `TenantId`, `CreatedBy`, sử dụng `Uuid7.NewGuid()`.
  - `Entities/SaaS/`: `SubscriptionPlan.cs`, `Tenant.cs`, cập nhật `Branch.cs`.
  - `Entities/Identity/`: Cập nhật `User.cs` đầy đủ theo CSDL chuẩn, thêm `TenantUser.cs`.
  - `Entities/System/`: `TaxSnapshot.cs`, `AuditLog.cs`.
  - `Entities/Catalog/`: Cập nhật `Product.cs`, `Category.cs`, thêm `ProductUnit.cs`, `ProductCombo.cs`.
  - `Entities/Inventory/`: `BranchStock.cs`, `Transfer.cs`, `TransferDetail.cs`.
  - `Entities/Supplier/`: `Supplier.cs`, `SupplierCategory.cs`, `SupplierDeliveryConfig.cs`, `SupplierBlackoutDate.cs`, `SupplierAPLedger.cs`, `SupplierProduct.cs`, `PurchaseOrder.cs`, `PurchaseOrderDetail.cs`, `GoodsReceipt.cs`, `GoodsReceiptLine.cs`.
  - `Entities/Sales/`: `Customer.cs`, `Promotion.cs`, `Order.cs`, `OrderDetail.cs`.
  - `Entities/Finance/`: `CashBook.cs`, `ServiceExpense.cs`.
  - `Entities/HR/`: `Employee.cs`, `Payslip.cs`, `EmployeeSchedule.cs`, `EmployeeBonusViolation.cs`.
  - `Enums/`: Khai báo các Enum trạng thái, phương thức và vai trò mới phục vụ 34 bảng.
- **Tầng Infrastructure (`MT_GRMS.Infrastructure`)**:
  - `Persistence/AppDbContext.cs`: Khai báo 34 `DbSet<T>`, đồng bộ Global Query Filter và tự động thiết lập `DeleteBehavior.Restrict` cho mọi quan hệ Cascade để triệt tiêu lỗi Multiple Cascade Paths (Error 1785) của SQL Server.
  - `Persistence/Configurations/`: Tạo đầy đủ các Fluent API EntityTypeConfiguration tương ứng chia theo từng thư mục phân hệ.
  - `Persistence/DbInitializer.cs`: Tạo bộ khởi tạo Code-First tự động áp dụng `MigrateAsync()` và nạp dữ liệu mẫu (Seed Data) phong phú cho cả 7 phân hệ.
  - `Persistence/Migrations/20261027154419_InitialSqlServerFull34Tables.cs`: Migration Code-First tạo trọn vẹn 34 bảng chuẩn Microsoft SQL Server.
- **Tầng Presentation (`MT_GRMS.Api`)**:
  - `appsettings.json`: Cấu hình chuỗi kết nối Microsoft SQL Server (`Server=localhost;Database=mtgrms_db;...`).
  - `Program.cs`: Tự động gọi `DbInitializer.InitializeAsync(context)` khi khởi động máy chủ.

---

## 3. Chi tiết Kỹ thuật (Technical Highlights)
- **Tương thích ngược**: Sử dụng thuộc tính `[NotMapped]` alias chuyển tiếp (ví dụ `SKU => ProductCode`, `Username => UserName`) trên các entity cốt lõi để bảo đảm 100% không làm vỡ các use-case đang hoạt động ở tầng Application.
- **Composite Keys**: Cấu hình `builder.HasKey(x => new { ... })` cho `TenantUsers`, `ProductCombos`, `BranchStocks` (Unique Index), `SupplierProducts`.
- **Decimal Precision**: Thiết lập rõ ràng `decimal(18,2)` cho các trường tiền tệ và `decimal(18,3)` / `decimal(12,2)` cho các trường số lượng.
- **Concurrency Token**: Cấu hình `.IsConcurrencyToken()` cho cột `RowVersion` ở các bảng giao dịch.
- **SQL Server Compatibility**: Giải quyết triệt để lỗi chu kỳ cascade bằng quy tắc Soft-Delete `DeleteBehavior.Restrict`.

---

## 4. Kết quả Kiểm thử (Verification)
- `dotnet build MT_GRMS_BACKEND.sln`: **Build succeeded (0 Error)**.
- `dotnet test`: **50/50 tests PASS** (45 Unit tests + 5 Integration tests).
- Khởi động thực tế (`dotnet run --project src/MT_GRMS.Api`):
  - Tự động tạo CSDL `mtgrms_db` trên SQL Server localhost.
  - Sinh đủ **35 bảng** (34 bảng nghiệp vụ + `__EFMigrationsHistory`).
  - Insert toàn bộ dữ liệu mẫu (Plans, Tenants, Users, Products, Suppliers, Employees, CashBooks).
  - API lắng nghe ổn định tại `http://localhost:5232`.

---

## 5. Ghi chú cho Manager khi Duyệt & Merge (Manager Merge Notes)
- **Mức độ rủi ro**: Low - Medium.
- **File cần lưu tâm**: `AppDbContext.cs`, `DbInitializer.cs`, `appsettings.json`.
- **Hậu merge**: Chạy ứng dụng (`dotnet run`) là hệ thống sẽ tự động khởi tạo CSDL và seed data nếu CSDL chưa có dữ liệu. Không cần chạy lệnh tay.
