# WORKLOG: FIX-BUILD - Bổ sung Migration AddBranchProductAllowedSupplierIds bị thiếu gây lỗi biên dịch CS0234

- **Ngày thực hiện**: 2026-11-25
- **Người thực hiện**: AI Agent (Antigravity)
- **Nhánh Git (Branch)**: `dev`
- **Mã tính năng liên kết**: `FIX-BUILD` / `store-product-management`
- **Loại thay đổi**: Bugfix

---

### 1. Mục Đích Thay Đổi (Why)
- Trong commit `15a9925` (`feat: add branch product management and supplier returns implementation`), thuộc tính `BranchProduct.AllowedSupplierIdsJson` và lời gọi `Migrations.AddBranchProductAllowedSupplierIds.UpSql` tại `DbInitializer.cs` (dòng 1821) đã được đưa lên nhánh `dev`, nhưng file migration `AddBranchProductAllowedSupplierIds` bị `.gitignore` dòng 56 (`**/[Mm]igrations/*`) chặn lại nên không được đưa lên Git.
- Hậu quả: Khi kéo code mới về và chạy `dotnet run` / `dotnet build`, trình biên dịch báo lỗi `CS0234: The type or namespace name 'AddBranchProductAllowedSupplierIds' does not exist in the namespace 'MT_GRMS.Infrastructure.Persistence.Migrations'`.
- Đồng thời khi chạy `dotnet run` với `launchSettings.json` (`Database__SkipDbInitialization=true`, `Database__RepairLocalSchema=true`), cần gọi `AddBranchProductAllowedSupplierIds.UpSql` trong `ApplyLocalSchemaRepairsAsync` để tự động bổ sung cột `AllowedSupplierIdsJson` trên CSDL PostgreSQL local hiện có.

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)
- `src/MT_GRMS.Infrastructure/Persistence/SchemaMigrations/20261124142800_AddBranchProductAllowedSupplierIds.cs`: Bổ sung class migration idempotent thêm cột `"AllowedSupplierIdsJson" text` cho bảng `"BranchProducts"` (đặt tại `SchemaMigrations/` cùng chuẩn với `AddExpenseVoucherDueDate` để không bị `.gitignore` bỏ sót).
- `src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`:
  - Bổ sung thực thi đầy đủ các migration `UpSql` gần đây (`AddProductImageUrlsJson`, `AddBranchProductProcessedPrice`, `AddSupplierPoModificationRules`, `AddBranchConversionRulesAndNotifications`, `AddBranchProductAllowedSupplierIds`, `ScheduleFefoDiscounts`, `RemoveGoodsReceiptFreight`, `AddTaxFilingConfirmations`) trong `ApplyLocalSchemaRepairsAsync` và `InitializeAsync`.
  - Tối ưu `SyncMissingColumnsAsync` đọc trước `information_schema.tables` & `information_schema.columns` (chỉ 2 câu `SELECT`) rồi gọi ngay trong `ApplyLocalSchemaRepairsAsync` để tự động vá mọi cột còn thiếu (như `Products.ImageUrlsJson` gây lỗi 500 trên `/store-owner/dashboard` và bảng `Notifications`) khi chạy `dotnet run` với `SkipDbInitialization=true`.

### 3. API Endpoints & Contract
- **Method & Route**: Không thay đổi
- **Database Migration**: `20261124142800_AddBranchProductAllowedSupplierIds`
- **Breaking Changes**: Không có

### 4. Kiểm Thử & Xác Nhận (Verification)
- [x] Đã chạy `dotnet build src/MT_GRMS.Api/MT_GRMS.Api.csproj --no-incremental`: Thành công (0 lỗi, 0 cảnh báo).
- [x] Đã chạy `dotnet build MT_GRMS_BACKEND.sln --no-incremental`: Thành công (0 lỗi).
- [x] Đã chạy unit test `REQ_STOREPROD_03c_ManualAssignSecondSupplier_AppendsToAllowedSupplierIds`: Đạt 100%.

### 5. Ghi Chú Cho Manager Khi Duyệt & Merge (Manager Merge Notes)
- **Mức độ rủi ro**: Thấp (SQL sử dụng `ALTER TABLE IF EXISTS ... ADD COLUMN IF NOT EXISTS` hoàn toàn idempotent).
- **File trọng yếu cần review**: `SchemaMigrations/20261124142800_AddBranchProductAllowedSupplierIds.cs`, `DbInitializer.cs`.
- **Hành động sau Merge**: Không cần thao tác thủ công, API tự động nâng cấp cột khi khởi động.
