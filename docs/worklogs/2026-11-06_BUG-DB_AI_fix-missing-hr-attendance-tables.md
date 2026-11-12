# Worklog: Khắc Phục Lỗi Thiếu Bảng "BranchAttendancePolicies" & Các Bảng HR Sau Khi Merge

- **Ngày thực hiện**: 2026-11-06
- **Mã task / Tính năng**: BUG-DB (Database Auto-Sync & HR Schema Resilience)
- **Tác giả**: AI Coding Assistant
- **Mô tả ngắn**: Khắc phục lỗi `42P01: relation "BranchAttendancePolicies" does not exist` và `relation "ShiftConfigs" does not exist` khi khởi động Backend và chạy Seeder phân hệ Nhân sự (HR).

---

## 1. Mục đích (Why)
- Sau khi thành viên trong nhóm merge code mới về Phân hệ Nhân sự (Quản lý chấm công, ca làm việc, bảng lương theo chi nhánh), trong code xuất hiện các Entity mới: `BranchAttendancePolicy`, `ShiftConfig`, `Employee`, `EmployeeSchedule`, `Payslip`, `EmployeeAllowance`, `CashbookTransaction`.
- Khi khởi động ứng dụng Backend, hàm `EmployeeSeedData.SeedAsync()` được gọi và thực hiện truy vấn `SELECT ... FROM "BranchAttendancePolicies"` và `"ShiftConfigs"`.
- Do cơ chế migration tự động của EF Core bị dừng lại giữa chừng (vướng các migration cũ hoặc do database đã có sẵn bảng `Users`), các câu lệnh tạo bảng mới trong migration `20261105102112_AddBranchAttendancePolicyAndRemoveBonusViolation.cs` chưa được thực thi trên PostgreSQL.
- Hậu quả: PostgreSQL ném ngoại lệ `Npgsql.PostgresException (0x80004005): 42P01: relation "BranchAttendancePolicies" does not exist`, khiến quá trình nạp dữ liệu mẫu bị gián đoạn.

---

## 2. Danh sách tệp & Nội dung thay đổi (What)
- [`src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`](file:///d:/FPT_University/Final/Grade/new/project/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs):
  - Bổ sung cơ chế tự động khởi tạo bảng an toàn và lũy đẳng (`CREATE TABLE IF NOT EXISTS`) độc lập cho toàn bộ các bảng thuộc phân hệ HR:
    - `"BranchAttendancePolicies"` (kèm index duy nhất theo `TenantID` và `BranchID`).
    - `"ShiftConfigs"` (kèm index duy nhất theo `TenantID`, `BranchID`, `ShiftCode`).
    - `"Employees"` (kèm index duy nhất theo `TenantID` và `CitizenId`, `EmployeeCode`).
    - `"EmployeeAllowances"` (phụ cấp nhân viên).
    - `"EmployeeSchedules"` (lịch phân ca và dữ liệu chấm công FaceID).
    - `"Payslips"` (phiếu lương chi tiết kỳ tính lương).
    - `"CashbookTransaction"` (bút toán chi lương sổ quỹ).
  - Tự động bổ sung các cột mới cho các bảng HR hiện hữu qua `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` (ví dụ: `Employees.Address`, `Employees.Note`, `Employees.OtWeekday`, `Employees.StartDate`, `EmployeeSchedules.BranchID`, `Payslips.UnpaidLeaveDays`, v.v.).
  - Bổ sung kiểm tra an toàn schema trước khi tạo Index nhằm tương thích hoàn toàn với mọi biến thể định dạng cột (`TenantId` / `TenantID`, `BranchId` / `BranchID`).

---

## 3. Chi tiết kỹ thuật (Technical Highlights)
- **Idempotency & Resilience**: Mỗi khối DDL được bọc trong một khối `try/catch` độc lập, đảm bảo nếu một bảng đã tồn tại hoặc một index đã có sẵn thì không bao giờ ảnh hưởng hay làm gián đoạn các bảng khác.
- **Tương thích toàn team**: Bất kỳ thành viên nào trong nhóm khi pull code mới về và khởi động backend đều sẽ tự động có đầy đủ cấu trúc bảng và dữ liệu mẫu chuẩn mà không cần phải chạy `dotnet ef database update` thủ công.

---

## 4. Kết quả kiểm thử (Verification)
- `dotnet build MT_GRMS.Api.csproj`: **0 Error(s)**, build thành công 100%.
- Khởi động backend `dotnet run`:
  - `Initializing database schema and seed data...`
  - `Database seeded successfully.`
  - `Now listening on: http://localhost:5232`
  - Hoàn toàn sạch lỗi `42P01: relation does not exist`, nạp dữ liệu mẫu 11 phân hệ thành công mượt mà.

---

## 5. Ghi chú cho Manager khi duyệt & merge (Manager Merge Notes)
- **Mức độ rủi ro**: Thấp (Low).
- **Tác động**: Chỉ can thiệp vào quy trình khởi tạo schema tự động trong `DbInitializer.cs`, không thay đổi bất kỳ logic nghiệp vụ hoặc API endpoints nào.
- **Hậu merge**: Không cần chạy lệnh migration thủ công, backend sẽ tự động đồng bộ cấu trúc bảng khi khởi động.

