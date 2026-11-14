# WORKLOG: FIX-WARN - Fix Entity Framework Core 8 Model Validation Warnings (20601 & 10620) & Query First Without OrderBy Warning (10103)

- **Ngày thực hiện**: 2026-11-15
- **Người thực hiện**: AI Assistant
- **Nhánh Git (Branch)**: `dev`
- **Mã tính năng liên kết**: [FIX-WARN] Fix EF Core Model Validation Warnings & Query Warning 10103
- **Loại thay đổi**: Bugfix / Configuration Refactor

---

### 1. Mục Đích Thay Đổi (Why)
- Khi khởi động ứng dụng backend (`dotnet run` tại `MT_GRMS.Api`), Entity Framework Core 8 sinh ra một loạt cảnh báo trong console log:
  1. `Microsoft.EntityFrameworkCore.Model.Validation[20601]`:
     Xảy ra trên các thuộc tính Enum cấu hình `.HasDefaultValue(...)` do giá trị mặc định CLR (0) trùng hoặc mơ hồ với giá trị mặc định của database, khiến EF Core 8 không phân biệt được thuộc tính chưa được gán (unassigned) hay người dùng cố tình gán giá trị 0.
     - `Product.Status` (`ProductStatus`)
     - `Employee.SalaryType` (`SalaryType`)
     - `Employee.Status` (`EmployeeStatus`)
     - `Payslip.SalaryType` (`SalaryType`)
     - `Payslip.Status` (`PayslipStatus`)
     - `ScheduleApprovalRequest.Status` (`ScheduleApprovalStatus`)
     - `Transfer.Status` (`TransferStatus`)
  2. `Microsoft.EntityFrameworkCore.Model.Validation[10620]`:
     Thuộc tính `ScheduleApprovalRequest.EvidenceImageUrls` có kiểu `List<string>` được chuyển đổi JSON (Value Converter) lưu cột `text`, nhưng thiếu `ValueComparer<List<string>>` dẫn đến EF Core không thể snapshot và theo dõi thay đổi phần tử (change tracking) chuẩn xác.
  3. `Microsoft.EntityFrameworkCore.Query[10103]`:
     `The query uses the 'First'/'FirstOrDefault' operator without 'OrderBy' and filter operators. This may lead to unpredictable results.`
     Xảy ra trong quá trình khởi tạo/nạp seed data (`DbInitializer.cs`), cũng như tại các repository/service (`ShiftRepository.cs`, `TaxExportService.cs`, `WalletService.cs`, `OrderRepository.cs`) khi gọi `.FirstOrDefaultAsync()` trực tiếp lên `DbSet` mà không có mệnh đề sắp xếp (`.OrderBy(...)`) hoặc điều kiện lọc xác định, khiến RDBMS trả về thứ tự ngẫu nhiên phụ thuộc execution plan.

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)
- `src/MT_GRMS.Infrastructure/Persistence/Configurations/ProductConfiguration.cs`:
  - Cấu hình `.HasSentinel((ProductStatus)0)` cho `builder.Property(x => x.Status)`.
- `src/MT_GRMS.Infrastructure/Persistence/Configurations/HR/EmployeeConfiguration.cs`:
  - Cấu hình `.HasSentinel((SalaryType)0)` cho `builder.Property(x => x.SalaryType)`.
  - Cấu hình `.HasSentinel((EmployeeStatus)0)` cho `builder.Property(x => x.Status)`.
- `src/MT_GRMS.Infrastructure/Persistence/Configurations/HR/PayslipConfiguration.cs`:
  - Cấu hình `.HasSentinel((SalaryType)0)` cho `builder.Property(x => x.SalaryType)`.
  - Cấu hình `.HasSentinel((PayslipStatus)0)` cho `builder.Property(x => x.Status)`.
- `src/MT_GRMS.Infrastructure/Persistence/Configurations/HR/ScheduleApprovalRequestConfiguration.cs`:
  - Cấu hình `.HasSentinel(ScheduleApprovalStatus.None)` cho `builder.Property(x => x.Status)`.
  - Khởi tạo và thiết lập `ValueComparer<List<string>>` đầy đủ logic so sánh phần tử `SequenceEqual`, hash code tổng hợp và deep clone snapshot `ToList()` truyền vào overload thứ 3 của `.HasConversion(...)`.
- `src/MT_GRMS.Infrastructure/Persistence/Configurations/Inventory/TransferConfiguration.cs`:
  - Cấu hình `.HasSentinel((TransferStatus)0)` cho `builder.Property(x => x.Status)`.
- `src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`:
  - Dòng 4230: Thay thế `context.Branches.IgnoreQueryFilters().FirstOrDefaultAsync(ct)` bằng truy vấn có filter theo tenant mặc định và deterministic ordering `.OrderBy(b => b.Id)`.
  - Dòng 4667, 4688, 4709, 4730: Bổ sung `.Where(s => s.Code == ...).OrderBy(s => s.Id)` cho các truy vấn lấy nhà cung cấp Acecook, Masan, Orion, Unilever trong phân hệ PO matrix.
  - Dòng 4781, 4785: Bổ sung `.Where(c => c.CategoryCode == ...).OrderBy(c => c.Id)` cho các truy vấn FMCG & ALCOHOL.
  - Dòng 5134 & 5497: Bổ sung `.OrderBy(p => p.Id)` cho các truy vấn kiểm tra sản phẩm trùng lặp.
  - Dòng 5386: Bổ sung `.Where(b => b.BranchCode == "BR-POS-01").OrderBy(b => b.Id)` cho truy vấn lấy chi nhánh POS chuẩn.
  - Dòng 5470: Bổ sung `.Where(c => c.CategoryCode == "FMCG").OrderBy(c => c.Id)` cho danh mục POS.
  - Dòng 5488: Bổ sung `.OrderBy(p => p.Id)` cho truy vấn sửa barcode Lavie 500ml.
  - Dòng 5579: Bổ sung `.OrderBy(b => b.Id)` cho truy vấn lấy chi nhánh của DefaultTenantId trong `SeedSupplierReturnsAndGrnsAsync`.
  - Dòng 5583, 5584: Bổ sung `.Where(s => s.Code == ...).OrderBy(s => s.Id)` cho truy vấn NCC trong `SeedSupplierReturnsAndGrnsAsync`.
  - Dòng 5588, 5589, 5590: Bổ sung `.Where(p => p.ProductCode == ...).OrderBy(p => p.Id)` cho truy vấn sản phẩm Hảo Hảo, Omachi.
  - Dòng 5597, 5662: Bổ sung `.Where(g => g.Code == ...).OrderBy(g => g.Id)` cho các truy vấn lấy phiếu nhập GRN-1 và GRN-2.
  - Dòng 5714, 5721, 5728: Bổ sung `.Where(l => ...).OrderBy(l => l.Id)` cho các truy vấn fallback `GoodsReceiptLines`.
- `src/MT_GRMS.Infrastructure/Repositories/CashbookRepository.cs`:
  - Dòng 48: Bổ sung `.OrderByDescending(s => s.OpenedAtUtc)` cho truy vấn `GetActiveShiftByTillIdAsync`.
- `src/MT_GRMS.Infrastructure/Repositories/PurchaseOrderRepository.cs`:
  - Dòng 384: Bổ sung `.OrderByDescending(po => po.CreatedAt)` cho truy vấn `GetDraftPoAsync`.
- `src/MT_GRMS.Infrastructure/Repositories/ShiftRepository.cs`:
  - Dòng 206, 220: Bổ sung `.OrderBy(b => b.Id)` cho các truy vấn lấy `defaultBranch` và `fallback` branch.
- `src/MT_GRMS.Infrastructure/Services/TaxExportService.cs`:
  - Dòng 61: Bổ sung `.OrderBy(t => t.Id)` cho truy vấn lấy `tenant` đầu tiên.
- `src/MT_GRMS.Infrastructure/Services/WalletService.cs`:
  - Dòng 99: Bổ sung `.OrderByDescending(o => o.CreatedAt)` cho truy vấn tìm đơn nạp ví `GetOrderByCodeAsync`.
  - Dòng 243, 352: Bổ sung `.OrderBy(t => t.Id)` cho các truy vấn fallback lấy tenant đầu tiên.
- `src/MT_GRMS.Infrastructure/Repositories/OrderRepository.cs`:
  - Dòng 126: Bổ sung `.OrderBy(s => s.Id)` cho truy vấn `GetBranchStockAsync`.

### 3. API Endpoints & Contract
- Không thay đổi API routes hay DTO contracts.
- Toàn bộ schema database vật lý (PostgreSQL DDL) được bảo toàn nguyên vẹn, không sinh migration mới và không gây breaking changes.

### 4. Kiểm Thử & Xác Nhận (Verification)
- [x] Đã chạy `dotnet build project/MT_GRMS_BACKEND`: Thành công 100% (0 lỗi, 19 warnings CS thông thường).
- [x] Đã chạy Unit Tests `EmployeeSeedDataTests`: Thành công 100% (Passed 2/2).
- [x] Triệt tiêu triệt để các nguyên nhân phát sinh warning `Microsoft.EntityFrameworkCore.Query[10103]`.

### 5. Ghi Chú Cho Manager Khi Duyệt & Merge (Manager Merge Notes)
- **Mức độ rủi ro**: Rất thấp (Low Risk). Chỉ bổ sung chỉ dẫn Sentinel, ValueComparer cho EF Core Model Builder và `.OrderBy(x => x.Id)` cho các truy vấn LINQ to Entities.
- **Tệp cần lưu tâm**:
  - `src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`
  - `src/MT_GRMS.Infrastructure/Persistence/Configurations/HR/ScheduleApprovalRequestConfiguration.cs`
- **Hậu merge**: Không cần chạy thêm migration database (`Add-Migration` / `ef database update`), vì cấu trúc bảng vật lý giữ nguyên không đổi.
