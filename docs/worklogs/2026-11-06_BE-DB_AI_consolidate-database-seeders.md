# Worklog: Consolidate DatabaseSeeder into Modular DbInitializer

- **Task/Feature**: BE-DB / TECH-DEBT
- **Author**: AI Coding Assistant
- **Date**: 2026-11-06
- **Branch**: `refactor/consolidate-seeders`

---

## 1. Mục đích (Why)
- **Vấn đề**: Trước đây dự án tồn tại song song 2 tệp Seeder khổng lồ: `DbInitializer.cs` (~1.291 dòng) và `DatabaseSeeder.cs` (~1.589 dòng), cả hai đều được gọi tại `Program.cs`. Điều này gây ra:
  1. Trùng lặp kiểm tra và nạp Tenant, Users, Branches, Categories.
  2. Phân mảnh dữ liệu seed gây khó khăn khi bảo trì (Technical Debt).
  3. Làm chậm thời gian khởi động container API trên Render/Cloud.
- **Giải pháp**: Tái cấu trúc hợp nhất toàn bộ dữ liệu nghiệp vụ (Báo cáo Thuế HKD theo NQ 204, Doanh thu Q3 385tr, Sổ quỹ Till/Safe/Bank, Công nợ FIFO, PO Matrix nhà cung cấp) từ `DatabaseSeeder.cs` vào `DbInitializer.cs`. Xóa bỏ hoàn toàn `DatabaseSeeder.cs`, tinh gọn `Program.cs` chỉ gọi một điểm khởi tạo duy nhất: `await DbInitializer.InitializeAsync(dbContext, logger);`.

---

## 2. Chi tiết thay đổi (What & Where)

### A. Tích hợp dữ liệu vào `DbInitializer.cs` (`src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`)
- Nâng cấp chữ ký hàm: `public static async Task InitializeAsync(AppDbContext context, ILogger? logger = null)`.
- Bổ sung `using Order = MT_GRMS.Domain.Entities.Sales.Order;` và `using OrderItem = MT_GRMS.Domain.Entities.Sales.OrderDetail;` để giải quyết triệt để xung đột namespace `Order`.
- Tích hợp các hàm nạp dữ liệu phân hệ từ `DatabaseSeeder`:
  - `SeedTaxAndFinanceAsync`: Nạp danh mục FMCG, ALCOHOL, TOBACCO, sản phẩm gắn `TaxCategory` (0.8% VAT vs 1.0% TTĐB), doanh thu Q3/2026 (385 triệu VNĐ), các phiếu chi mặt bằng/điện nước, 4 tài khoản quỹ (`FinanceAccounts`: Két quầy, Két sắt, VCB, TCB), hồ sơ công nợ khách hàng FIFO, và 20 sản phẩm kèm cấu hình nhà cung cấp PO matrix.
  - `SeedPromotionsAsync`: Nạp các chương trình khuyến mãi và voucher codes.
  - `SeedPosRetailDataAsync`: Nạp chi nhánh `BR-POS-01`, khách hàng POS và tồn kho FEFO theo lô `LOT-xxx-EARLY` và `LOT-xxx-LATE`.
- Gọi tự động `await SeedTaxAndFinanceAsync(context, logger);` ngay sau `SeedOrdersAndPreOrdersAsync(context)`.

### B. Tinh gọn `Program.cs` (`src/MT_GRMS.Api/Program.cs`)
- Xóa bỏ lời gọi dư thừa `await DatabaseSeeder.SeedAsync(dbContext, logger);`.
- Cập nhật dòng khởi tạo duy nhất: `await DbInitializer.InitializeAsync(dbContext, logger);`.

### C. Xóa bỏ tệp `DatabaseSeeder.cs`
- Xóa tệp `src/MT_GRMS.Infrastructure/Persistence/DatabaseSeeder.cs` (1.589 dòng code dư thừa).

---

## 3. Kỹ thuật quan trọng (Technical Highlights)
- **Tính lũy đẳng (Idempotency)**: 100% các khối thêm dữ liệu đều được bọc kiểm tra `if (!await context.Xxx.AnyAsync(...))` trước khi insert, bảo đảm an toàn tuyệt đối khi restart ứng dụng.
- **Bảo toàn dữ liệu kiểm thử (Invariants)**: Toàn bộ ID của tài khoản quản trị `admin`, tài khoản chủ tiệm `store_owner` (với mã PIN 1234 băm PBKDF2), 385 triệu doanh thu Q3 phục vụ báo cáo thuế và tồn kho FEFO đều được bảo toàn nguyên vẹn.

---

## 4. Kết quả kiểm thử (Verification)
- `dotnet build`: **Succeeded (0 Errors, 0 Warnings nghiêm trọng)**.
- `dotnet test`: **Passed 451/451 tests (100% GREEN)** (Unit Tests: 407, Integration Tests: 44).

---

## 5. Ghi chú cho Manager khi Review & Merge
- **Mức độ rủi ro**: Thấp (Low) - Đây là bản refactor thuần túy dọn dẹp kỹ thuật, không thay đổi API hay Schema CSDL.
- **Hành động hậu merge**: Không cần chạy migration DB. Khi deploy lên Render, server sẽ tự động chạy qua điểm khởi tạo `DbInitializer` duy nhất.

