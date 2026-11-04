# BRIEF GIAO VIỆC CHO AI/DEV: REFACTOR PHÂN HỆ THUẾ - TÀI CHÍNH (MT-GRMS)

> **Thông báo quan trọng:** Tài liệu này **thay thế phần "Kế hoạch refactor" (Mục 8)** trong Báo cáo kiểm toán v1.0 (28/10/2026) vì báo cáo gốc có một số điểm sai/thiếu về kiến trúc và bảo mật (xem Mục 2). Hãy tuân thủ nghiêm ngặt tài liệu này.

---

## 0. QUY TẮC LÀM VIỆC (BẮT BUỘC)

1. **ĐỌC code thật trước khi sửa:**
   - [HouseholdTaxService.cs](file:///D:/Sourse_code_Do_An/MT_GRMS_BACKEND/src/MT_GRMS.Application/Tax/HouseholdTaxService.cs)
   - [HouseholdTaxRepository.cs](file:///D:/Sourse_code_Do_An/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/Repositories/HouseholdTaxRepository.cs)
   - [IHouseholdTaxRepository.cs](file:///D:/Sourse_code_Do_An/MT_GRMS_BACKEND/src/MT_GRMS.Application/Tax/IHouseholdTaxRepository.cs)
   - [HouseholdTaxController.cs](file:///D:/Sourse_code_Do_An/MT_GRMS_BACKEND/src/MT_GRMS.Api/Controllers/HouseholdTaxController.cs)
   - [PnLReportRepository.cs](file:///D:/Sourse_code_Do_An/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/Repositories/PnLReportRepository.cs)
   - Entity [HouseholdTaxSnapshot.cs](file:///D:/Sourse_code_Do_An/MT_GRMS_BACKEND/src/MT_GRMS.Domain/Entities/Finance/HouseholdTaxSnapshot.cs)
   - [HouseholdTaxServiceTests.cs](file:///D:/Sourse_code_Do_An/MT_GRMS_BACKEND/tests/MT_GRMS.UnitTests/HouseholdTaxServiceTests.cs)
   *(Lưu ý: Tên hàm/cột trong tài liệu mô tả có thể không khớp 100% với code; nếu có độ lệch, hãy căn cứ theo code thực tế và ghi chú lại).*
2. **Kiểm tra Baseline trước và sau:** Chạy `dotnet build` và `dotnet test` **TRƯỚC** khi sửa để lấy baseline (8/8 unit tests hiện đang pass). Sau mỗi bước refactor, bắt buộc phải build + test lại.
3. **Commit nguyên tử (Atomic Commits):** Thực hiện từng bước nhỏ, mỗi bước là một commit riêng biệt. Tuyệt đối không thay đổi hành vi ngoài phạm vi của bước đó.
4. **Không tự suy đoán số liệu:** Tuyệt đối không tự bịa số liệu hay thuế suất. Chỗ nào được đánh dấu `[CẦN XÁC NHẬN]` thì dừng lại và hỏi Chủ dự án / Tech Lead.
5. **Cập nhật CSDL qua Migration:** Mọi thay đổi schema phải thông qua EF Core Migration (`dotnet ef migrations add`), không can thiệp sửa tay trực tiếp trên Database.

---

## 1. BỐI CẢNH DỰ ÁN NGẮN GỌN

- **Dự án:** MT-GRMS (Phần mềm quản lý tiệm tạp hóa bán lẻ đa chi nhánh).
- **Công nghệ Backend:** .NET 8, C# 12, Entity Framework Core, PostgreSQL.
- **Kiến trúc:** Layered / Clean Architecture (`Api` $\rightarrow$ `Application` $\rightarrow$ `Domain` $\leftarrow$ `Infrastructure`).
- **Phân hệ phụ trách:** Tính thuế Hộ kinh doanh (GTGT 1% + TNCN 0.5% trên phần vượt 1 tỷ), Chốt kỳ tính thuế (Snapshot bất biến), và hệ thống 5 Sổ kế toán HKD.
- **Bộ số đối soát chuẩn Quý 3/2026 (dùng làm Test Benchmark):**
  - Doanh thu phát sinh kỳ Q3: **467.500.000đ**
  - Doanh thu lũy kế đầu năm YTD: **1.417.500.000đ**
  - Thuế GTGT (1%): $467.500.000 \times 1\% = \mathbf{4.675.000đ}$
  - Thuế TNCN (0.5% tính trên phần vượt 1 tỷ): $(1.417.500.000 - 1.000.000.000) \times 0.5\% = \mathbf{2.087.500đ}$
  - Tổng thuế Quý 3/2026: $4.675.000 + 2.087.500 = \mathbf{6.762.500đ}$
  - Quý 1/2026 (470tr) và Quý 2/2026 (480tr) doanh thu lũy kế 950tr $\le$ 1 tỷ nên thuế nộp = **0đ**.
  - **Hạn nộp thuế Quý 3/2026:** Ngày 31/10/2026 là Thứ Bảy $\rightarrow$ Tự động dời sang ngày làm việc tiếp theo: **Thứ Hai, 02/11/2026**.

---

## 2. CÁC ĐIỂM BÁO CÁO GỐC SAI / THIẾU & CÁCH LÀM ĐÚNG

| # | Vấn đề trong Báo cáo v1.0 | Cách làm ĐÚNG (Chuẩn Production) |
| :-: | :--- | :--- |
| **A** | **COGS dùng hằng số `UnitPrice * 0.70m`:** Đây là ước lượng giả định, không phải giá vốn thực, mâu thuẫn với nguyên tắc khai thuế. Hardcode 70% là sai. | **COGS = Tổng (Số lượng bán $\times$ Giá vốn thực tế của từng mặt hàng)** tại thời điểm xuất bán (Bình quân gia quyền hoặc FIFO theo module Kho). Hằng số 70% chỉ dùng làm fallback khi dữ liệu kho chưa có giá vốn, kèm cờ `IsCogsEstimated = true` gửi lên DTO để cảnh báo trên UI. |
| **B** | **Verify PIN đặt trong Controller:** Phá vỡ phân tầng Clean Architecture, và việc kiểm tra PIN không nằm trong cùng một transaction với nghiệp vụ chốt kỳ. | **Chuyển vào Application Service:** Đặt trong `IPinVerificationService` và được gọi trực tiếp bởi `FinalizeTaxSnapshotAsync`, cùng luồng với kiểm tra điều kiện chốt kỳ. |
| **C** | **Đề xuất "SHA-256 + Salt" cho PIN:** Mã PIN chỉ 4–6 chữ số nên không gian khóa cực nhỏ ($10^4 - 10^6$), kẻ tấn công có thể brute-force bằng GPU trong vài giây nếu lộ DB. | **Dùng PBKDF2 (>= 600.000 iterations, HMAC-SHA256) hoặc Argon2id / BCrypt (work factor >= 12)**. So sánh chuỗi bằng thời gian hằng số (`CryptographicOperations.FixedTimeEquals`). Khóa tạm sau 5 lần sai trong 15 phút, lưu bộ đếm trong CSDL (không lưu RAM process). |
| **D** | **AuditSignatureHash = SHA-256 + Secret Key:** Dễ bị tấn công length-extension và không chuẩn format. | **Dùng HMAC-SHA256:** Khóa bí mật lấy từ Secret Store hoặc Environment Variable (không commit appsettings lên Git). Băm trên chuỗi JSON chuẩn hóa (Canonical JSON, thứ tự thuộc tính cố định). |
| **E** | **Bỏ qua Race Condition khi chốt kỳ:** Hai request gửi song song cùng thời điểm có thể cùng vượt qua câu lệnh kiểm tra `if (existing == null)`. | **Thêm Unique Index `(BranchId, Year, PeriodType, PeriodNumber)`** trên bảng `HouseholdTaxSnapshots` + bọc transaction; bắt ngoại lệ `DbUpdateException` do vi phạm unique index và ném lỗi nghiệp vụ `TAX_PERIOD_LOCKED`. |
| **F** | **Ranh giới kỳ dùng UTC `(>= fromDateUtc && <= toDateUtc)`:** Đơn hàng phát sinh từ 00:00 - 07:00 ngày đầu quý (giờ VN) bị lọt sang kỳ trước do lệch múi giờ. | **Tính ranh giới kỳ theo múi giờ `Asia/Ho_Chi_Minh`** rồi chuyển sang UTC; sử dụng khoảng nửa mở: `[fromDateUtc, toDateUtcExclusive)`. |
| **G** | **Doanh thu chưa định nghĩa rõ Gross vs Net:** Thiếu xử lý hàng trả lại, hủy đơn, chiết khấu. | **Định nghĩa chuẩn mực:** `NetRevenue = GrossRevenue - SalesReturns - Discounts - CancelledOrders`. Thống nhất toàn bộ code dùng NetRevenue để tính thuế. |
| **H** | **Repository dùng `ToListAsync()` kéo hết về RAM rồi `.Sum()`:** Gây tràn bộ nhớ và nghẽn CPU khi dữ liệu đơn hàng lớn. | **Sử dụng truy vấn tổng hợp phía Database (`SumAsync` / `GroupBy`)**. Lọc chi phí rút vốn chủ sở hữu bằng danh sách Category trực tiếp ở câu lệnh SQL. Đồng bộ tên cột StoreId/BranchId giữa Orders và ExpenseVouchers. |
| **I** | **Viện dẫn văn bản luật chưa kiểm chứng:** Giảm GTGT 0.8% hay tỷ lệ khoán chưa có điều khoản cụ thể. | `[CẦN XÁC NHẬN]` với quy định pháp luật hiện hành. Tách toàn bộ mức thuế suất vào cấu hình tập trung (`TaxPolicy`), tuyệt đối không hardcode trong code. |
| **J** | **Phương pháp Kê khai thiếu thuế suất TNCN trên lợi nhuận:** Không có quy tắc bù trừ lỗ. | `[CẦN XÁC NHẬN]` mức thuế suất TNCN trên lợi nhuận và quy tắc chuyển lỗ. Khi chưa xác nhận, DeclarationBased bắt buộc đọc tỷ lệ từ `TaxPolicy`. |
| **K** | **Chấm điểm SOLID chủ quan ("9/10, 6/10"):** Không có giá trị kỹ thuật. | Bỏ qua việc chấm điểm số, chỉ tập trung vào **Tiêu chí chấp nhận (Acceptance Criteria)** ở Mục 4. |

---

## 3. LỘ TRÌNH THỰC HIỆN TỪNG BƯỚC (LÀM TUẦN TỰ)

### Bước 0: Baseline & Test Khung
- Chạy `dotnet build` và `dotnet test` trong `MT_GRMS_BACKEND`. Ghi lại kết quả baseline.
- Xác nhận đã có (hoặc bổ sung) bài test đặc tả bộ số chuẩn Q1/Q2/Q3 năm 2026 ở Mục 1. Test này phải pass trước và sau khi refactor.

### Bước 1: Tách Chiến Lược Tính Thuế (Strategy Pattern - Giữ Nguyên Hành Vi)
- Tạo thư mục `MT_GRMS.Application/Tax/Strategies/`:
  ```csharp
  public interface ITaxCalculationStrategy
  {
      TaxCalculationMethod Method { get; }
      TaxCalculationResult Calculate(TaxCalculationContext context);
  }

  public interface ITaxCalculationStrategyFactory
  {
      ITaxCalculationStrategy Resolve(TaxCalculationMethod method);
  }
  ```
- Tạo `RevenuePercentageTaxStrategy` và `DeclarationBasedTaxStrategy`: Chuyển toàn bộ logic từ khối `if/else` hiện tại trong `HouseholdTaxService` sang 2 class này. Bảo đảm kết quả đầu ra không thay đổi.
- **Quy tắc:** Strategy là Pure Logic (không gọi Database, không gọi `DateTime.Now`). Mọi dữ liệu đầu vào được truyền qua `TaxCalculationContext`.
- Factory nhận `IEnumerable<ITaxCalculationStrategy>` qua DI container, đăng ký `Scoped` trong `Application/DependencyInjection.cs`.
- `HouseholdTaxService` chỉ còn vai trò Orchestrator (Điều phối): Gọi repository lấy dữ liệu $\rightarrow$ Xác định nhóm $\rightarrow$ Gọi Strategy tính toán $\rightarrow$ Lưu Snapshot.
- Viết Unit Test riêng cho từng Strategy (Pure Test không cần Mock DB).

### Bước 2: Vá Giá Vốn Hàng Bán (COGS) trong Phương pháp Kê Khai
- Khảo sát mã nguồn thực tế: Xác định giá vốn đang được quản lý ở đâu (Cost price trong `Products`, lô nhập `Batches`, hay phiếu nhập kho `GoodsReceiptNotes`)? *[Nếu chưa có giá vốn thực tế: Dừng lại và hỏi Tech Lead]*.
- Thêm vào `IHouseholdTaxRepository`:
  - `GetPeriodCogsAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? branchId, CancellationToken ct)`
  - `GetPeriodOpexAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? branchId, CancellationToken ct)`
- `COGS = Tổng (Số lượng bán × Giá vốn tại thời điểm bán)` của các đơn hàng đã thanh toán trong kỳ, trừ phần hàng trả lại.
- Nếu sản phẩm chưa có giá vốn thực, áp dụng fallback ước tính 70% và gán cờ `IsCogsEstimated = true` trên DTO để cảnh báo người dùng trên giao diện.
- Toàn bộ truy vấn phải dùng `SumAsync` phía Database, ranh giới ngày tính theo múi giờ `Asia/Ho_Chi_Minh` (Mục 2.F & 2.H).
- Viết Unit Test: Tạo dữ liệu kiểm thử Q3 (Doanh thu 467.5tr, chi phí dịch vụ ~20tr, giá vốn thực ~70%), kiểm tra thu nhập chịu thuế là khoảng $\text{Doanh thu} - \text{COGS} - \text{OPEX}$ (~120tr), không còn bị thổi phồng lên ~447.5tr.
- Đối chiếu với `PnLReportRepository` để đảm bảo phân hệ Thuế và P&L dùng chung nguồn dữ liệu giá vốn.

### Bước 3: Bảo Mật Chốt Kỳ & Xác Thực Mã PIN
- Tại `HouseholdTaxController`:
  - Gắn attribute `[Authorize(Roles = "StoreOwner,ChiefAccountant")]` cho endpoint chốt kỳ (`POST /api/tax/household/snapshots`).
  - Các endpoint đọc dữ liệu (`GET`) yêu cầu tối thiểu `[Authorize]`.
  - Xóa bỏ fallback ẩn danh `User?.Identity?.Name ?? "system-accountant"`. Nếu không có thông tin định danh, trả mã `401 Unauthorized`.
- Tạo `IPinVerificationService`:
  ```csharp
  Task<PinVerificationResult> VerifyStoreOwnerPinAsync(Guid userId, string inputPin, CancellationToken ct);
  ```
- Tạo EF Core Migration bổ sung các trường vào bảng Người dùng / Cài đặt tiệm:
  - `SecurityPinHash` (string)
  - `SecurityPinSalt` (string)
  - `FailedPinAttempts` (int, mặc định 0)
  - `PinLockedUntilUtc` (DateTime?, null nếu không bị khóa)
- Sử dụng thuật toán PBKDF2 (>= 600.000 iterations) hoặc BCrypt, so sánh hằng số thời gian bằng `CryptographicOperations.FixedTimeEquals`.
- Nếu nhập sai: Tăng `FailedPinAttempts`. Nếu $\ge$ 5 lần trong 15 phút, cập nhật `PinLockedUntilUtc = DateTime.UtcNow.AddMinutes(15)`. Nếu đúng: Reset `FailedPinAttempts = 0`.
- Gọi `_pinService.VerifyStoreOwnerPinAsync(...)` bên trong `HouseholdTaxService.FinalizeTaxSnapshotAsync` (cùng transaction với chốt kỳ). Tuyệt đối không log chuỗi PIN thô ra console hay log file.
- Viết Unit/Integration Tests: Kiểm thử PIN đúng, PIN sai, tài khoản bị tạm khóa sau 5 lần, user sai role, request thiếu PIN.

### Bước 4: Chống Chốt Trùng (Concurrency) & Ký Số Snapshot
- Tạo EF Core Migration: Thêm **Unique Constraint / Unique Index** trên bảng `HouseholdTaxSnapshots`:
  ```csharp
  builder.HasIndex(s => new { s.BranchId, s.Year, s.PeriodType, s.PeriodNumber }).IsUnique();
  ```
- Trong `HouseholdTaxService.FinalizeTaxSnapshotAsync`: Bọc thao tác chốt kỳ trong Database Transaction. Bắt ngoại lệ `DbUpdateException` do vi phạm Unique Index và ném lỗi nghiệp vụ `TAX_PERIOD_LOCKED`.
- Bổ sung trường `AuditSignatureHash` (string) trên thực thể `HouseholdTaxSnapshot`:
  - Tính toán bằng thuật toán **HMAC-SHA256** với Secret Key lấy từ biến môi trường (`TAX_AUDIT_SECRET_KEY`).
  - Chuỗi băm được tạo từ chuỗi Canonical JSON (thứ tự thuộc tính cố định: `Year`, `PeriodType`, `PeriodNumber`, `TaxableRevenue`, `TotalTaxAmount`, `FinalizedAtUtc`, `FinalizedBy`).
- Bổ sung hàm `VerifySnapshotIntegrityAsync(Guid snapshotId)` để kiểm tra tính toàn vẹn khi đọc lại snapshot.

### Bước 5: Hoàn Thiện 5 Sổ Kế Toán HKD (Sau khi hoàn tất Bước 1–4)
- **S2d-HKD:** Xây dựng endpoint và DTO riêng truy vấn Nhập kho (`GoodsReceiptNotes`) - Xuất kho (`OrderItems`) - Tồn kho.
- **S2e-HKD:** Thay thế dữ liệu giả lập bằng truy vấn thực tế từ bảng `CashbookTransactions`.

---

## 4. TIÊU CHÍ CHẤP NHẬN (ACCEPTANCE CRITERIA)

1. [x] `dotnet build` hoàn tất không có bất kỳ lỗi hoặc cảnh báo mới.
2. [x] Toàn bộ unit tests cũ vẫn pass 100%. Các unit/integration tests mới cho Strategy, COGS, PIN Verification, và Concurrency Lock đều pass.
3. [x] Kết quả tính thuế Quý 3/2026 phương pháp Tỷ lệ % vẫn đúng chuẩn số học:
   $$\text{GTGT (4.675.000đ)} + \text{TNCN (2.087.500đ)} = \mathbf{6.762.500đ}$$
4. [x] `HouseholdTaxService` hoàn toàn không còn khối lệnh `if/else` tính thuế dài, không phụ thuộc trực tiếp vào EF Core `AppDbContext`.
5. [x] Việc thêm một phương pháp tính thuế mới chỉ cần tạo thêm 1 class Strategy mới và đăng ký vào DI.
6. [x] Phương pháp Kê khai: Chi phí hợp lý được trừ bao gồm **COGS thực tế + OPEX**, có cờ `IsCogsEstimated` khi dùng tỷ lệ ước lượng.
7. [x] Tuyệt đối không thể chốt kỳ khi: Thiếu PIN, sai PIN, tài khoản bị tạm khóa, thiếu Role hợp lệ, hoặc khi 2 request gửi song song cùng thời điểm.
8. [x] EF Core Migrations chạy thành công trên cả database trống lẫn database đang có dữ liệu.
9. [x] Mỗi bước refactor được tạo thành một Commit Git riêng biệt, có commit message rõ ràng và ghi chú các mục `[CẦN XÁC NHẬN]` nếu còn mở.

---

## 5. BÁO CÁO KẾT THÚC CẦN TRẢ LẠI
Sau khi hoàn thành, AI / Lập trình viên phải cung cấp báo cáo gồm:
- Danh sách các file đã thay đổi/thêm mới.
- Kết quả chạy thực tế của `dotnet build` và `dotnet test`.
- Các giả định kỹ thuật đã áp dụng.
- Danh sách các điểm `[CẦN XÁC NHẬN]` còn tồn đọng (nếu có) kèm khuyến nghị.
- Đánh giá rủi ro còn lại sau khi refactor.

---

## PROMPT MẪU DÁN CHO AI CODING (Claude Code, Cursor, Copilot, v.v.)

```text
Bạn đang làm việc trên repo MT_GRMS_BACKEND (.NET 8). Hãy đọc file AI_REFACTOR_BRIEF.md ở thư mục gốc
và thực hiện lần lượt từ Bước 0 -> Bước 4. Đọc code thật trước, chạy build/test trước và sau mỗi bước,
commit riêng từng bước. Gặp mục [CẦN XÁC NHẬN] thì dừng và hỏi tôi, tuyệt đối không tự suy đoán thuế suất
hay quy định pháp luật. Cuối cùng gửi báo cáo nghiệm thu theo đúng Mục 5.
```
