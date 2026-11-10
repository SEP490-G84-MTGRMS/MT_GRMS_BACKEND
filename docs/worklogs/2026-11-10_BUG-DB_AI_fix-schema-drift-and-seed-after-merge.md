# Worklog: Sửa lỗi DB khi khởi động Backend sau khi merge nhánh (schema drift & seed)

- **Ngày thực hiện**: 2026-11-10
- **Mã Task**: BUG-DB
- **Tác giả**: AI Assistant
- **Dự án**: `project/MT_GRMS_BACKEND`

## 1. Mục đích (Why)
Sau khi nhóm merge code vào nhánh chính, chạy Backend bị lỗi DB (`column "DebtStatus" does not exist`,
`relation "ShiftZReports" does not exist`, `column e.FaceDescriptor does not exist`, ...), khiến `DbInitializer`
dừng giữa chừng và nhiều API trả `500` (suppliers, pos/cash-movements, ...).

**Nguyên nhân gốc (chuỗi domino):**
1. `DbInitializer` chạy toàn bộ script `GenerateCreateScript()` trong **1 lệnh duy nhất**. DB cũ đã có bảng `Customers`
   nhưng thiếu cột `DebtStatus` → câu `CREATE INDEX ... (TenantId, DebtStatus)` lỗi → **cả script bị rollback**,
   các bảng mới (`ShiftZReports`, `SupplierBranch`, ...) không được tạo.
2. Khối `ALTER TABLE` bổ sung cột chạy cũng là 1 lệnh: `ShiftZReports` không tồn tại → rollback cả khối
   (kể cả cột `Employees.FaceDescriptor`) → seed nhân viên lỗi → toàn bộ khởi tạo dừng.
3. Seed dùng `AnyAsync/FirstOrDefaultAsync` **không** `IgnoreQueryFilters()` nên không thấy bản ghi đã soft-delete
   → insert trùng khóa unique (`IX_Branches_TenantID_BranchCode`, `IX_Suppliers_TenantId_Code`, `IX_Customers_...`).
4. `EmployeeSeedData`: `existing.Allowances.Add(new EmployeeAllowance { Id = ... })` khiến EF coi entity là
   `Modified` (đã có Id) → `UPDATE` 0 dòng → `DbUpdateConcurrencyException`.
5. Khối chuyển dữ liệu `SupplierDeliveryConfigs` (legacy) lỗi `c.CutoffDayOffset does not exist` trên DB cũ.

## 2. Tệp & nội dung thay đổi (What)
1. `src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`
   - Tách script tạo schema thành **từng câu lệnh** chạy độc lập (`CREATE TABLE` → đồng bộ cột → `CREATE INDEX`).
   - Thêm `SyncMissingColumnsAsync`: so khớp EF model với DB, tự `ADD COLUMN IF NOT EXISTS` cho cột thiếu
     (NOT NULL có DEFAULT an toàn theo kiểu dữ liệu).
   - Thêm `using Microsoft.EntityFrameworkCore.Metadata;`.
   - Thêm `IgnoreQueryFilters()` cho toàn bộ 46 lệnh `context.X.AnyAsync/FirstOrDefaultAsync` trong seed.
   - Khối chuyển dữ liệu `SupplierDeliveryConfigs` chỉ chạy khi bảng cũ có cột `CutoffDayOffset`.
2. `src/MT_GRMS.Infrastructure/Persistence/EmployeeSeedData.cs`
   - Thêm phụ cấp qua `context.EmployeeAllowances.Add(...)` thay vì `existing.Allowances.Add(...)`.

## 3. Kiểm thử (Verification)
- `dotnet build`: 0 lỗi.
- Chạy Backend trên DB cũ đang bị lỗi: log `Database seeded successfully`, không còn `fail:`.
- `GET /api/suppliers`, `/api/pos/cash-movements/pending`, `/api/branches/quota`, `/api/tenant/store-info` → `200`.

## 4. Ghi chú cho Manager (Merge Notes)
- **Rủi ro**: Medium (chỉ ảnh hưởng bước khởi tạo/đồng bộ schema lúc khởi động; không đổi API contract).
- Cần review kỹ: `SyncMissingColumnsAsync` và đoạn chạy script theo từng câu lệnh trong `DbInitializer.cs`.
- Hậu merge: **không cần migration**; chỉ cần chạy lại Backend, DB cũ sẽ tự được vá cột/bảng thiếu.
- Khuyến nghị cho nhóm: mỗi khi thêm cột/bảng mới vào entity, chạy lại Backend để schema tự đồng bộ.

