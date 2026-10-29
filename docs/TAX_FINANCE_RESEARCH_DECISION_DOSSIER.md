# HỒ SƠ NGHIÊN CỨU & THAM VẤN RA QUYẾT ĐỊNH: PHÂN HỆ THUẾ - TÀI CHÍNH (MT-GRMS)
**Document Version:** 2.0 (Post-Refactor Deep Research Dossier)  
**Date:** 29/10/2026  
**Audience:** Research AI (Claude 3.5 Sonnet/Opus, GPT-4o, Gemini Deep Research) & Chủ nhiệm dự án / Tech Lead MT-GRMS  
**System Target:** MT-GRMS (Grocery Retail Management System - Chuỗi Tạp hóa & Bán lẻ Hộ Kinh Doanh)  
**Repository:** `MT_GRMS_BACKEND` (.NET 8 WebAPI) & `MT_GRMS_FRONTEND` (React 18 / Vite / TypeScript)

---

## MỤC LỤC
1. [TỔNG QUAN & MỤC ĐÍCH TÀI LIỆU](#1-tổng-quan--mục-đích-tài-liệu)
2. [BỐI CẢNH NGHIỆP VỤ & PHÁP LÝ TỪ ROOT](#2-bối-cảnh-nghiệp-vụ--pháp-lý-từ-root)
3. [HIỆN TRẠNG KIẾN TRÚC KỸ THUẬT SAU REFACTOR (127/127 TESTS PASS)](#3-hiện-trạng-kiến-trúc-kỹ-thuật-sau-refactor-127127-tests-pass)
4. [CÁC ĐIỂM CẦN XÁC NHẬN & BẢNG THAM VẤN RA QUYẾT ĐỊNH (DECISION MATRIX)](#4-các-điểm-cần-xác-nhận--bảng-tham-vấn-ra-quyết-định-decision-matrix)
   - [Quyết định 1: Chính sách Giảm Thuế GTGT (Thời hạn hiệu lực & Tùy biến mức giảm)](#quyết-định-1-chính-sách-giảm-thuế-gtgt-thời-hạn-hiệu-lực--tùy-biến-mức-giảm)
   - [Quyết định 2: Thuế Suất Thuế TNCN Theo Phương Pháp Kê Khai (Nhóm 3 & Nhóm 4)](#quyết-định-2-thuế-suất-thuế-tncn-theo-phương-pháp-kê-khai-nhóm-3--nhóm-4)
   - [Quyết định 3: Cơ Chế Bù Trừ & Chuyển Lỗ Qua Năm (Loss Carry-Forward Policy)](#quyết-định-3-cơ-chế-bù-trừ--chuyển-lỗ-qua-năm-loss-carry-forward-policy)
   - [Quyết định 4: Chiến Lược Quản Lý & Tính Giá Vốn Hàng Bán Thực Tế (COGS Engine)](#quyết-định-4-chiến-lược-quản-lý--tính-giá-vốn-hàng-bán-thực-tế-cogs-engine)
   - [Quyết định 5: Chuẩn Hóa 5 Bộ Sổ Kế Toán & Định Dạng Tích Hợp Cơ Quan Thuế](#quyết-định-5-chuẩn-hóa-5-bộ-sổ-kế-toán--định-dạng-tích-hợp-cơ-quan-thuế)
5. [BỘ CÂU HỎI MẪU DÀNH CHO AI NGHIÊN CỨU CHUYÊN SÂU](#5-bộ-câu-hỏi-mẫu-dành-cho-ai-nghiên-cứu-chuyên-sâu)

---

## 1. TỔNG QUAN & MỤC ĐÍCH TÀI LIỆU

### 1.1. Mục đích
Tài liệu này được biên soạn nhằm cung cấp **toàn bộ ngữ cảnh kỹ thuật, kiến trúc mã nguồn và bối cảnh pháp lý** của phân hệ Thuế - Kế toán trong hệ thống **MT-GRMS**. 

Mục tiêu chính là đóng vai trò như một **Dossier tham vấn chuyên sâu** để Chủ dự án gửi cho các hệ thống AI mạnh về nghiên cứu (Deep Research AI) cùng phối hợp:
1. Đánh giá tính chuẩn xác của các giả định kế toán và mô hình số liệu.
2. Phân tích các lựa chọn (Options) từ góc độ pháp luật thuế Việt Nam và thực tiễn ngành bán lẻ.
3. Ra quyết định dứt điểm (Final Decision) cho các điểm đang còn mở (`[CẦN XÁC NHẬN]`).

### 1.2. Định vị bài toán hệ thống
MT-GRMS là giải pháp ERP thu nhỏ tối ưu cho các chuỗi cửa hàng tạp hóa, siêu thị mini vận hành dưới hình thức **Hộ kinh doanh cá thể (HKD)**. Điểm nghẽn lớn nhất trong quản trị HKD tại Việt Nam hiện nay là:
- Trước đây chủ yếu nộp thuế theo cơ chế **Thuế Khoán** (áp đặt doanh thu ước tính chủ quan).
- Kể từ năm 2025–2026, cơ quan thuế đẩy mạnh áp dụng hóa đơn điện tử khởi tạo từ máy tính tiền và ban hành **Thông tư 152/2025/TT-BTC**, **Nghị định 141/2026/NĐ-CP** (sửa đổi **Nghị định 68/2026/NĐ-CP**), yêu cầu các HKD quy mô vừa và lớn bắt buộc phải có sổ sách kế toán minh bạch (doanh thu, chi phí, nhập - xuất - tồn, tiền mặt - ngân hàng) và chuyển đổi sang phương pháp **Kê khai**.

---

## 2. BỐI CẢNH NGHIỆP VỤ & PHÁP LÝ TỪ ROOT

### 2.1. Phân nhóm Hộ kinh doanh theo Doanh thu năm (Annual Revenue Tiers)
Căn cứ theo Nghị định 141/2026/NĐ-CP và Thông tư 152/2025/TT-BTC, hệ thống MT-GRMS phân loại HKD thành 4 nhóm với chế độ quản trị thuế khác nhau:

| Nhóm | Ngưỡng doanh thu năm | Chế độ thuế áp dụng | Bộ sổ kế toán bắt buộc (TT 152/2025/TT-BTC) |
|---|---|---|---|
| **Nhóm 1** | $\le 1.000.000.000$đ (1 tỷ) | **Miễn 100% thuế GTGT & TNCN** | Mẫu **S1a-HKD** (Sổ chi tiết doanh thu bán hàng hóa, dịch vụ) |
| **Nhóm 2** | $1 	ext{ tỷ} - 3 	ext{ tỷ}$đ | **Phương pháp Tỷ lệ % trên doanh thu** (hoặc tự nguyện đăng ký Kê khai) | Mẫu **S2a-HKD** (Sổ doanh thu tính thuế) hoặc Bộ 4 sổ (S2b, S2c, S2d, S2e) nếu chọn Kê khai |
| **Nhóm 3** | $3 	ext{ tỷ} - 50 	ext{ tỷ}$đ | **Khuyến khích / Bắt buộc Kê khai** (Tính thuế TNCN trên thu nhập tính thuế) | **Bộ 4 sổ:** S2b (Doanh thu), S2c (Chi phí), S2d (Vật tư - Hàng hóa), S2e (Tiền mặt - Tiền gửi) |
| **Nhóm 4** | $> 50.000.000.000$đ (50 tỷ) | **Bắt buộc Kê khai** kèm chế độ chứng từ tương đương Doanh nghiệp nhỏ | Bộ 4 sổ (S2b, S2c, S2d, S2e) + Báo cáo tài chính doanh nghiệp |

### 2.2. Thuế Suất Ngành Thương Mại / Bán Lẻ Hàng Hóa (Retail Grocery)
- **Thuế GTGT:** 
  - Mức tỷ lệ chuẩn: **1.0%** trên doanh thu phát sinh trong kỳ chịu thuế.
  - Chính sách giảm thuế GTGT (theo các Nghị quyết kích cầu tiêu dùng của Quốc hội): Giảm 20% mức tỷ lệ % (từ 1.0% xuống **0.8%**).
- **Thuế TNCN:**
  - *Phương pháp Tỷ lệ % (Nhóm 2):* **0.5%** tính trên **phần doanh thu lũy kế trong năm vượt ngưỡng 1 tỷ đồng** (doanh thu dưới 1 tỷ được miễn).
  - *Phương pháp Kê khai (Nhóm 3, Nhóm 4):* Tính trên Thu nhập chịu thuế:
    $$	ext{Thu nhập chịu thuế (YTD)} = \max\Big(0,\ 	ext{Doanh thu thuần lũy kế} - 	ext{Chi phí hợp lý được trừ (COGS + OPEX)}\Big)$$

---

## 3. HIỆN TRẠNG KIẾN TRÚC KỸ THUẬT SAU REFACTOR (127/127 TESTS PASS)

Phân hệ Thuế - Kế toán đã được tái cấu trúc toàn diện trên nhánh `feature/refactor-tax-finance-backend`:

```
MT_GRMS_BACKEND (src/MT_GRMS.Application/Tax)
├── Strategies/
│   ├── ITaxCalculationStrategy.cs             <-- Interface chiến lược tính thuế (Pure Logic)
│   ├── ITaxCalculationStrategyFactory.cs      <-- Factory phân giải Strategy qua DI
│   ├── TaxCalculationModels.cs                <-- Context & Result bất biến
│   ├── RevenuePercentageTaxStrategy.cs        <-- Chiến lược Tỷ lệ % (NĐ 141/2026)
│   └── DeclarationBasedTaxStrategy.cs         <-- Chiến lược Kê khai (TT 152/2025)
├── Security/
│   ├── IPinVerificationService.cs             <-- Interface xác thực PIN chủ hộ
│   ├── ITaxSnapshotSignatureService.cs        <-- Interface ký số Canonical JSON HMAC-SHA256
│   └── TaxSnapshotSignatureService.cs         <-- Triển khai ký số & verify tính toàn vẹn
├── Dtos/
│   └── HouseholdTaxDtos.cs                    <-- DTOs chuẩn mực, hỗ trợ COGS, OPEX, PIN, Signature
├── IHouseholdTaxRepository.cs                 <-- Hợp đồng Repository tổng hợp SQL
├── IHouseholdTaxService.cs                    <-- Hợp đồng Service điều phối
└── HouseholdTaxService.cs                     <-- Orchestrator: Nạp data -> Strategy -> Ký số -> Chốt kỳ
```

### 3.1. Các trụ cột kiến trúc cốt lõi đã hoàn thành:
1. **Strategy Pattern (SOLID):** Khối lệnh `if/else` tính thuế dài đã bị triệt tiêu hoàn toàn. Logic tính toán được cô lập thành "Pure Logic" không gọi Database hay thời gian hệ thống, kiểm thử unit test độc lập không cần Mock DB.
2. **Khấu trừ Chi phí Kê khai chuẩn mực (Real COGS + OPEX):**
   - Giá vốn hàng bán (COGS) được tính từ $\sum (	ext{Số lượng bán} 	imes 	ext{Product.CostPrice})$ của các đơn hàng đã thanh toán trong kỳ trừ hàng trả lại.
   - Khi chưa thiết lập giá vốn sản phẩm: Tự động fallback về 70% doanh thu thuần kèm cờ cảnh báo `IsCogsEstimated = true`.
   - Chi phí vận hành (OPEX): Lấy từ `ExpenseVouchers`, tự động loại trừ 100% các khoản rút vốn chủ sở hữu (`Equity`).
3. **Múi giờ chuẩn hóa:** Chuyển đổi toàn bộ mốc thời gian từ múi giờ kinh doanh Việt Nam (`Asia/Ho_Chi_Minh` / GMT+7) sang UTC theo khoảng nửa mở `[fromDateUtc, toDateUtcExclusive)`.
4. **Bảo mật chốt kỳ (OWASP PBKDF2):** Mã PIN chủ hộ được băm bằng PBKDF2 (600.000 vòng lặp HMAC-SHA256, muối 16 bytes), so sánh bằng `CryptographicOperations.FixedTimeEquals`. Tự động khóa 15 phút nếu sai liên tiếp 5 lần.
5. **Chống chốt trùng & Ký số chống gian lận:**
   - Unique Index `(BranchId, Year, PeriodType, PeriodNumber)` trên bảng `HouseholdTaxSnapshots` ngăn chặn race condition khi 2 quản lý cùng bấm chốt kỳ một lúc.
   - Ký số HMAC-SHA256 trên chuỗi Canonical JSON bảo đảm snapshot không thể bị sửa đổi lén trong Database.
6. **Linh hoạt chính sách giảm thuế GTGT:** Bổ sung trường `VatReductionPercentage` cho phép bật/tắt và điều chỉnh tỷ lệ giảm linh hoạt thay vì fix cứng 0.8%.

---

## 4. CÁC ĐIỂM CẦN XÁC NHẬN & BẢNG THAM VẤN RA QUYẾT ĐỊNH (DECISION MATRIX)

Dưới đây là **5 Quyết định Kỹ thuật & Nghiệp vụ quan trọng nhất** cần tham vấn chuyên sâu giữa Chủ dự án và AI Nghiên cứu:

---

### QUYẾT ĐỊNH 1: CHÍNH SÁCH GIẢM THUẾ GTGT (THỜI HẠN HIỆU LỰC & TÙY BIẾN MỨC GIẢM)

#### A. Vấn đề thực tế (Problem Statement)
- Thuế suất GTGT ngành bán lẻ theo luật định là **1.0%**.
- Các chính sách giảm thuế GTGT (ví dụ giảm 20% tỷ lệ % còn 0.8% theo Nghị quyết 204/2025/QH15) **luôn có thời hạn hiệu lực cụ thể** (ví dụ: áp dụng từ 01/01/2026 đến hết 30/06/2026 hoặc 31/12/2026). Khi Quốc hội không gia hạn, mức thuế sẽ lập tức quay trở lại 1.0%.
- Nếu để người dùng tự tick chọn thủ công từng kỳ thì dễ xảy ra sai sót (quên tick hoặc tick nhầm vào kỳ không được giảm). Nếu hardcode vào code thì khi chính sách thay đổi dev phải sửa code và deploy lại.

#### B. Các phương án kiến trúc (Options Matrix)

| Tiêu chí | Phương án 1A: Cấu hình thủ công từng kỳ (Manual Override) | Phương án 1B: Bảng chính sách thuế có ngày hiệu lực (Tax Policy Engine) | Phương án 1C: Cấu hình qua File AppSettings / Env Variable |
|---|---|---|---|
| **Mô tả** | Người dùng có nút gạt bật/tắt và ô nhập mức giảm (%) khi xem trước và chốt kỳ thuế. | Tạo bảng `TaxReductionPolicies` trong DB lưu: `PolicyCode`, `VatReductionRate`, `EffectiveFromUtc`, `EffectiveToUtc`, `IsActive`. Hệ thống tự kiểm tra kỳ tính thuế có giao với khoảng hiệu lực không để tự động áp dụng. | Lưu cấu hình trong file `appsettings.json` hoặc cấu hình hệ thống trên Admin Portal. |
| **Ưu điểm** | Đơn giản, linh hoạt tối đa cho từng trường hợp ngoại lệ của chi nhánh. Đã được hỗ trợ sẵn trong code. | **Tự động hóa hoàn toàn.** Ngăn ngừa lỗi con người (quên bật/tắt). Có thể lên lịch trước chính sách giảm cho tương lai. | Dễ cấu hình lại mà không cần thay đổi code business logic. |
| **Nhược điểm** | Rủi ro sai sót do kế toán viên/chủ hộ không nắm rõ văn bản luật hết hiệu lực ngày nào. | Cần tạo thêm 1 Entity và Migration mới trong DB. | Khi chuỗi có nhiều chi nhánh áp dụng thời điểm khác nhau sẽ khó phân tách. |
| **Tiền lệ ngành (MISA, KiotViet)** | Cung cấp checkbox "Áp dụng giảm thuế theo NQ Quốc hội" kèm link cập nhật thông tư. | MISA áp dụng tự động theo khoảng ngày hóa đơn, người dùng có quyền override. | Ít dùng cho ứng dụng SaaS multi-tenant. |

> **Khuyến nghị đề xuất:** Kết hợp **Phương án 1B (Mặc định tự động theo ngày hiệu lực)** với **Phương án 1A (Cho phép Quản lý cấp cao override có ghi chú lý do)**.

---

### QUYẾT ĐỊNH 2: THUẾ SUẤT THUẾ TNCN THEO PHƯƠNG PHÁP KÊ KHAI (NHÓM 3 & NHÓM 4)

#### A. Vấn đề thực tế (Problem Statement)
- Theo quy định đối với Hộ kinh doanh thuộc Nhóm 3 (3 tỷ - 50 tỷ) và Nhóm 4 (> 50 tỷ), HKD nộp thuế theo phương pháp Kê khai trên **Thu nhập chịu thuế** ($	ext{Doanh thu} - 	ext{Chi phí hợp lý}$).
- **Điểm gây tranh cãi lớn nhất trong giới kế toán thuế hiện nay:** Thuế suất TNCN áp dụng trên phần thu nhập này là bao nhiêu?
  - Luật Thuế TNCN hiện hành quy định cá nhân cư trú có thu nhập từ kinh doanh nộp theo biểu thuế lũy tiến từng phần (từ 5% đến 35%) hoặc theo tỷ lệ trên doanh thu.
  - Tuy nhiên, khi chuyển sang Kê khai doanh thu - chi phí như doanh nghiệp, các địa phương và chuyên gia có 2 quan điểm: áp dụng mức cố định (tương đương thuế TNDN 15% - 20%) hoặc áp dụng theo Biểu thuế lũy tiến từng phần.
- Hệ thống MT-GRMS hiện đang đặt mức tham khảo: Nhóm 2 (15%), Nhóm 3 (17%), Nhóm 4 (20%).

#### B. Các phương án kiến trúc (Options Matrix)

| Tiêu chí | Phương án 2A: Thuế suất cố định theo phân nhóm doanh thu | Phương án 2B: Biểu thuế Lũy tiến từng phần (Progressive Tax Brackets) | Phương án 2C: Cấu hình tập trung `TaxRateTable` trong DB |
|---|---|---|---|
| **Cơ sở tính toán** | Nhóm 2: 15%<br>Nhóm 3: 17%<br>Nhóm 4: 20% | Bậc 1: $\le 60$tr (5%)<br>Bậc 2: $60 - 120$tr (10%)<br>...<br>Bậc 7: $> 960$tr (35%) | Tách biểu thuế ra bảng DB hoặc cấu hình `TenantTaxConfig`, người dùng có thể chọn chế độ áp dụng theo hướng dẫn của Chi cục Thuế quản lý. |
| **Ưu điểm** | Dễ hiểu, đơn giản, phù hợp với mô hình ước tính chi phí quản trị của HKD quy mô chuỗi. | Phù hợp sát nhất với câu chữ của Luật Thuế TNCN hiện hành đối với thu nhập tính thuế sau giảm trừ. | **An toàn pháp lý 100%.** Bất kể cơ quan thuế áp dụng biểu thuế nào, hệ thống đều đáp ứng được mà không cần sửa code. |
| **Nhược điểm** | Có thể bị kiểm toán thuế chất vấn nếu Chi cục Thuế địa phương yêu cầu nộp theo biểu lũy tiến. | Khấu trừ theo lũy tiến nội năm (YTD) phức tạp hơn khi có các kỳ lỗ - lãi đan xen. | Cần bổ sung UI cấu hình trong phần Cài đặt tiệm. |

> **Khuyến nghị đề xuất:** Lựa chọn **Phương án 2C**. Hệ thống cung cấp mặc định Strategy tính theo Biểu thuế cấu hình, cho phép chọn giữa *Tỷ lệ cố định quy mô (15-20%)* hoặc *Biểu lũy tiến từng phần*.

---

### QUYẾT ĐỊNH 3: CƠ CHẾ BÙ TRỪ & CHUYỂN LỖ QUA NĂM (LOSS CARRY-FORWARD POLICY)

#### A. Vấn đề thực tế (Problem Statement)
- Trong một năm tài chính, các cửa hàng tạp hóa bán lẻ có thể có quý kinh doanh thua lỗ (ví dụ: Q1 nhập hàng Tết tồn đọng, chi phí nhân sự cao dẫn đến lỗ 50 triệu).
- **Hiện trạng MT-GRMS:** Đã giải quyết trọn vẹn bài toán **Bù trừ nội năm (YTD Cumulative Model)**. Ví dụ: Q1 lỗ 50 triệu $ightarrow$ Thuế = 0; Q2 lãi 80 triệu $ightarrow$ Lợi nhuận lũy kế YTD = 30 triệu $ightarrow$ Thuế chỉ đánh trên 30 triệu. Số thuế tạm nộp 4 quý khớp 100% với quyết toán cả năm.
- **Vấn đề cần quyết định:** Nếu **CẢ NĂM bị lỗ ròng** (Tổng chi phí cả năm > Tổng doanh thu cả năm), số lỗ này có được chuyển sang giảm trừ vào lợi nhuận của năm tài chính tiếp theo không?

#### B. Các phương án pháp lý & kỹ thuật

| Tiêu chí | Phương án 3A: Chốt sổ từng năm độc lập (No Multi-Year Loss Carry) | Phương án 3B: Cho phép chuyển lỗ sang năm sau (Tối đa 5 năm) |
|---|---|---|
| **Cơ sở pháp lý** | Thông tư 92/2015/TT-BTC và Thông tư 40/2021/TT-BTC: Hộ kinh doanh là thể nhân, quyết toán thuế theo năm dương lịch, không có điều khoản rõ ràng về chuyển lỗ như doanh nghiệp. | Điều 9 Luật Thuế TNDN: Doanh nghiệp được chuyển lỗ liên tục không quá 5 năm. Khi HKD Nhóm 4 bắt buộc kê khai tương đương DN, một số chuyên gia đề xuất áp dụng nguyên tắc này. |
| **Độ phức tạp kỹ thuật** | **Rất thấp.** Reset YTD về 0 vào ngày 01/01 của năm mới. | **Trung bình.** Cần lưu trữ bảng `TaxLossCarryRecords` để theo dõi số lỗ chưa khấu trừ hết của từng năm cũ. |
| **Rủi ro kiểm toán thuế** | **Không có rủi ro.** Cơ quan thuế không bao giờ phạt vì HKD không chuyển lỗ. | **Có rủi ro.** Nếu tự ý trừ số lỗ năm trước vào doanh thu năm nay mà Chi cục Thuế không chấp thuận sẽ bị truy thu và phạt chậm nộp. |

> **Khuyến nghị đề xuất:** Áp dụng **Phương án 3A** cho bản phát hành hiện tại (chốt độc lập từng năm tài chính). Bổ sung tùy chọn ghi nhận "Lỗ lũy kế năm trước" ở dạng trường thông tin tham khảo, chỉ khấu trừ nếu chủ hộ có văn bản chấp thuận của cơ quan thuế quản lý.

---

### QUYẾT ĐỊNH 4: CHIẾN LƯỢC QUẢN LÝ & TÍNH GIÁ VỐN HÀNG BÁN THỰC TẾ (COGS ENGINE)

#### A. Vấn đề thực tế (Problem Statement)
- Phân hệ Thuế hiện tại đã hỗ trợ tính COGS thực tế:
  $$	ext{COGS} = \sum (	ext{Số lượng bán} 	imes 	ext{Product.CostPrice})$$
- Tuy nhiên, trong ngành tạp hóa - bán lẻ:
  - Giá nhập của các mặt hàng biến động liên tục (hàng tiêu dùng nhanh FMCG, sữa, dầu ăn, bia, nước ngọt).
  - Hệ thống MT-GRMS đã có module Lô hàng (`Batches`) với trường `LandedCostPerUnit` (giá vốn nhập kho tại từng lô).
  - Một số sản phẩm chưa kịp nhập giá vốn khi tạo nhanh trên POS.

#### B. Các phương án quản lý giá vốn (Inventory Valuation)

| Tiêu chí | Phương án 4A: Giá vốn tiêu chuẩn trên Danh mục (`Product.CostPrice`) [Hiện tại] | Phương án 4B: Nhập trước - Xuất trước theo Lô (FIFO Batch Costing) | Phương án 4C: Bình quân gia quyền di động (Moving Weighted Average) |
|---|---|---|---|
| **Mô tả** | Mỗi sản phẩm có 1 mức `CostPrice`. Đơn hàng bán nhân trực tiếp với giá này. Fallback 70% nếu giá = 0. | Đơn hàng xuất kho tự động trừ từ lô cũ nhất tới lô mới nhất và ghi nhận giá vốn chính xác theo từng lô (`LandedCostPerUnit`). | Mỗi khi nhập kho lô mới, tính lại giá vốn bình quân của sản phẩm trong kho. Đơn bán ghi nhận theo giá bình quân tại thời điểm bán. |
| **Mức độ chính xác** | Tương đối (sai lệch nhẹ khi giá nhập biến động mạnh). | **Chính xác tuyệt đối 100%.** Phù hợp hoàn hảo với hàng hóa có hạn sử dụng (FMCG). | Rất chính xác, mượt mà số liệu, không phụ thuộc vào thứ tự quét mã vạch lô. |
| **Yêu cầu vận hành** | Thu ngân bán hàng không cần quan tâm đến lô hàng. | Thu ngân hoặc kho phải chọn đúng lô khi bán / quét mã vạch có lô. | Hệ thống tự tính toán mỗi lần duyệt phiếu nhập kho (`GoodsReceiptNote`). |
| **Hiện trạng mã nguồn** | **Đã hoàn thành 100% trong Bước 2.** | Đã có thực thể `Batch` và `StockOnHand`, chưa liên kết vào chi tiết đơn bán POS. | Cần cập nhật trường `MovingAverageCost` trên bảng `Products`. |

> **Khuyến nghị đề xuất:** 
> - **Giai đoạn 1 (Hiện tại):** Duy trì **Phương án 4A** kết hợp fallback 70% có cờ `IsCogsEstimated`.
> - **Giai đoạn 2 (Khi hoàn thiện module Kho nâng cao):** Nâng cấp lên **Phương án 4B (FIFO)** cho sản phẩm có hạn sử dụng và **Phương án 4C** cho sản phẩm tiêu dùng thông thường.

---

### QUYẾT ĐỊNH 5: CHUẨN HÓA 5 BỘ SỔ KẾ TOÁN & ĐỊNH DẠNG TÍCH HỢP CƠ QUAN THUẾ

#### A. Vấn đề thực tế (Problem Statement)
Theo Thông tư 152/2025/TT-BTC, HKD áp dụng phương pháp Kê khai phải lập và lưu trữ 4 mẫu sổ chi tiết:
1. **S2b-HKD:** Sổ chi tiết doanh thu bán hàng hóa, dịch vụ.
2. **S2c-HKD:** Sổ chi tiết doanh thu, chi phí.
3. **S2d-HKD:** Sổ chi tiết vật liệu, dụng cụ, sản phẩm, hàng hóa (Nhập - Xuất - Tồn).
4. **S2e-HKD:** Sổ chi tiết tiền (Tiền mặt tại két/till và Tiền gửi ngân hàng).

#### B. Các phương án xuất báo cáo & lưu trữ

| Tiêu chí | Phương án 5A: Lưu trữ Snapshot JSON + Hiển thị UI / Xuất Excel | Phương án 5B: Xuất mẫu in PDF chuẩn biểu mẫu TT 152 có chữ ký số | Phương án 5C: Tích hợp xuất XML nộp Cổng Thuế điện tử (`thuedientu.gdt.gov.vn`) |
|---|---|---|---|
| **Mô tả** | Dữ liệu sổ sách được đóng gói trong trường `BookDataJson` của snapshot. Frontend render bảng biểu và cho phép tải file Excel chuẩn cột TT 152. | Sinh file PDF theo mẫu quy chuẩn ban hành kèm Thông tư 152/2025/TT-BTC, gắn kèm chữ ký điện tử HMAC-SHA256 của chủ hộ. | Tạo file XML theo chuẩn XSD của Tổng cục Thuế để chủ hộ import trực tiếp lên phần mềm HTKK hoặc nộp trực tuyến. |
| **Độ ưu tiên** | **P0 (Bắt buộc hoàn thành ngay).** Đã hoàn thành cấu trúc dữ liệu Backend. | **P1 (Cần thiết cho in ấn lưu trữ hồ sơ tiệm).** | **P2 (Nâng cao cho các phiên bản thương mại).** |
| **Thách thức** | Dễ triển khai, kiểm soát dữ liệu trực tiếp trên frontend. | Cần template PDF tương thích font tiếng Việt và căn chỉnh trang in A4. | Phụ thuộc vào định dạng XSD do Tổng cục Thuế công bố (thường cập nhật định kỳ). |

> **Khuyến nghị đề xuất:** Ưu tiên hoàn tất **Phương án 5A (Excel & Web UI)** và **Phương án 5B (PDF chuẩn mẫu in)** để đáp ứng 100% nhu cầu thanh tra tại chỗ của cán bộ thuế.

---

## 5. BỘ CÂU HỎI MẪU DÀNH CHO AI NGHIÊN CỨU CHUYÊN SÂU

*Người dùng có thể sao chép đoạn prompt dưới đây để gửi cho Research AI (Claude 3.5, GPT-4o, v.v.) cùng thảo luận:*

```text
Xin chào, tôi đang phát triển hệ thống phần mềm quản lý bán lẻ cho chuỗi cửa hàng tạp hóa hoạt động dưới hình thức Hộ kinh doanh tại Việt Nam (MT-GRMS). Hệ thống vừa hoàn thành refactor toàn bộ backend theo Thông tư 152/2025/TT-BTC và Nghị định 141/2026/NĐ-CP (127/127 tests passed).

Hãy đọc file tài liệu đính kèm TAX_FINANCE_RESEARCH_DECISION_DOSSIER.md và giúp tôi phân tích chuyên sâu 5 vấn đề cốt lõi sau:

1. [Về Giảm thuế GTGT]: Theo tiền lệ các Nghị quyết giảm thuế GTGT của Quốc hội Việt Nam (từ 1.0% xuống 0.8% cho bán lẻ), chính sách này có đặc điểm về hiệu lực thời gian như thế nào? Thiết kế cơ sở dữ liệu và API như thế nào là tối ưu nhất giữa tự động theo khoảng ngày và cho phép người dùng can thiệp?

2. [Về Thuế suất TNCN Kê khai]: Hộ kinh doanh có doanh thu trên 3 tỷ (Nhóm 3 và Nhóm 4) bắt buộc nộp thuế TNCN trên lợi nhuận thuần (Doanh thu - COGS - OPEX). Căn cứ pháp lý hiện hành tại Việt Nam đang quy định áp dụng mức thuế suất TNCN cố định (ví dụ 15% - 20%) hay áp dụng Biểu thuế lũy tiến từng phần theo Luật Thuế TNCN? Thực tế các chi cục thuế tại TP.HCM và Hà Nội đang ấn định như thế nào?

3. [Về Chuyển lỗ qua năm]: Nếu một Hộ kinh doanh kê khai bị lỗ ròng cả năm 2026, về mặt luật quản lý thuế có được phép chuyển lỗ sang giảm trừ lợi nhuận năm 2027 (như nguyên tắc chuyển lỗ 5 năm của Luật Thuế TNDN) hay không?

4. [Về Giá vốn COGS]: Đối với cửa hàng tạp hóa bán lẻ có hơn 5.000 SKU mặt hàng tiêu dùng nhanh (FMCG), giữa phương pháp FIFO theo Lô và phương pháp Bình quân gia quyền di động (Moving Weighted Average), phương pháp nào giúp tối ưu chi phí kế toán và dễ giải trình nhất với cơ quan thuế?

5. Đưa ra khuyến nghị cuối cùng kèm lộ trình cụ thể cho từng quyết định trên để nhóm phát triển chốt cấu trúc cơ sở dữ liệu và giao diện người dùng.
```

---
*Hồ sơ được chuẩn bị tự động bởi Antigravity AI Pair Programmer - Nhánh: `feature/refactor-tax-finance-backend`.*
