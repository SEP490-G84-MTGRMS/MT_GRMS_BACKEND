# HỒ SƠ NGHIÊN CỨU & QUYẾT ĐỊNH THIẾT KẾ: PHÂN HỆ THUẾ - TÀI CHÍNH (MT-GRMS)
**Document Version:** 2.1 (Final Architecture & Decision Dossier)  
**Date:** 29/10/2026  
**Audience:** Ban Quản trị Dự án, Kế toán trưởng, Tech Lead & AI Coding Assistant  
**System Target:** MT-GRMS (Grocery Retail Management System - Chuỗi Tạp hóa & Bán lẻ Hộ Kinh Doanh)  
**Repositories:** `MT_GRMS_BACKEND` (.NET 8 WebAPI) & `MT_GRMS_FRONTEND` (React 18 / Vite / TypeScript)

---

## MỤC LỤC
1. [TỔNG QUAN & NGUYÊN TẮC THIẾT KẾ CỐT LÕI](#1-tổng-quan--nguyên-tắc-thiết-kế-cốt-lõi)
2. [CĂN CỨ PHÁP LÝ & CÁC ĐIỂM CẦN XÁC MINH THỰC TẾ](#2-căn-cứ-pháp-lý--các-điểm-cần-xác-minh-thực-tế)
3. [TỔNG HỢP CÁC QUYẾT ĐỊNH THIẾT KẾ NGHIỆP VỤ (DECISION MATRIX)](#3-tổng-hợp-các-quyết-định-thiết-kế-nghiệp-vụ-decision-matrix)
   - [Quyết định 1: Giảm thuế GTGT & Phân loại hàng hóa](#quyết-định-1-giảm-thuế-gtgt--phân-loại-hàng-hóa)
   - [Quyết định 2: Thuế suất TNCN & Tùy chọn 2 phương pháp cho Nhóm 2](#quyết-định-2-thuế-suất-tncn--tùy-chọn-2-phương-pháp-cho-nhóm-2)
   - [Quyết định 3: Phạm vi sản phẩm đối với Nhóm 4 (> 50 tỷ)](#quyết-định-3-phạm-vi-sản-phẩm-đối-với-nhóm-4--50-tỷ)
   - [Quyết định 4: Cơ chế bù trừ & Chuyển lỗ qua năm](#quyết-định-4-cơ-chế-bù-trừ--chuyển-lỗ-qua-năm)
   - [Quyết định 5: Chiến lược tính Giá vốn hàng bán (COGS Engine) theo TT 152](#quyết-định-5-chiến-lược-tính-giá-vốn-hàng-bán-cogs-engine-theo-tt-152)
   - [Quyết định 6: Danh mục Sổ kế toán & Định dạng trích xuất](#quyết-định-6-danh-mục-sổ-kế-toán--định-dạng-trích-xuất)
4. [THIẾT KẾ CƠ SỞ DỮ LIỆU TỐI GIẢN (KHÔNG THÊM BẢNG THỪA)](#4-thiết-kế-cơ-sở-dữ-liệu-tối-giản-không-thêm-bảng-thừa)

---

## 1. TỔNG QUAN & NGUYÊN TẮC THIẾT KẾ CỐT LÕI

Tài liệu này xác lập các quyết định thiết kế kiến trúc và chuẩn hóa mô hình dữ liệu cho phân hệ Thuế – Tài chính của **MT-GRMS**. Mọi điều chỉnh đều tuân thủ nghiêm ngặt 3 nguyên tắc nền tảng:

1. **Tối giản CSDL (KISS & No Redundant Tables):**
   - Tuyệt đối **không tạo bảng mới** khi có thể tận dụng hoặc mở rộng các trường trên các bảng hiện hữu (`Products`, `OrderItems`, `HouseholdTaxSnapshots`, `TenantSettings`).
   - Tránh over-engineering: Không tạo bảng quan hệ phức tạp cho các dữ liệu mang tính cấu hình hệ thống hoặc thống kê định kỳ.

2. **Minh bạch pháp lý & Thận trọng thiết kế:**
   - Phân biệt rành mạch giữa: (a) Căn cứ pháp lý đã ban hành, (b) Lựa chọn thiết kế thận trọng khi luật chưa nêu rõ, và (c) Quyết định phạm vi sản phẩm (Product Scope).
   - Không đưa các giả định chưa được kiểm chứng vào hệ thống dưới dạng khẳng định chắc chắn.

3. **Tính bất biến của dữ liệu kiểm toán (Audit Immutability):**
   - Toàn bộ mức thuế, tiền thuế trên từng dòng bán hàng và bản chụp cấu hình chính sách thuế tại thời điểm chốt kỳ phải được đóng băng bất biến.
   - Khi thanh tra hoặc kiểm tra lại quá khứ, kết quả tính toán độc lập hoàn toàn với việc cấu hình hệ thống bị sửa đổi sau này.

---

## 2. CĂN CỨ PHÁP LÝ & CÁC ĐIỂM CẦN XÁC MINH THỰC TẾ

| Văn bản / Quy định | Nội dung áp dụng trong MT-GRMS | Trạng thái xác minh & Điểm cần lưu ý |
| :--- | :--- | :--- |
| **Nghị quyết 204/2025/QH15** | Giảm 20% mức tỷ lệ % thuế GTGT (từ 1.0% xuống 0.8% cho bán lẻ). | **Cần đối chiếu toàn văn:** Cần kiểm tra kỹ thời hạn hiệu lực chính thức và danh mục loại trừ đầy đủ (ngoài thuế tiêu thụ đặc biệt như bia, rượu, thuốc lá còn có nhóm hàng hóa/dịch vụ nào khác không). |
| **Nghị định 141/2026/NĐ-CP** (sửa NĐ 68/2026/NĐ-CP) | Ngưỡng doanh thu miễn thuế GTGT và TNCN được nâng lên **1 tỷ đồng** (hiệu lực hồi tố từ 01/01/2026). | **Đã xác minh qua nguồn công khai:** Khớp với các hướng dẫn mới nhất của cơ quan thuế và tài liệu chuyên môn. |
| **Luật Thuế TNCN số 109/2025/QH15** | Thuế suất TNCN cố định theo doanh thu: đến 3 tỷ (15%), trên 3–50 tỷ (17%), trên 50 tỷ (20%). | **Đã xác minh:** Biểu lũy tiến 5–35% chỉ áp dụng cho tiền lương/tiền công; thu nhập kinh doanh áp dụng thuế suất cố định theo bậc. Bỏ phương án lũy tiến. |
| **Thông tư 152/2025/TT-BTC** | Chế độ kế toán cho Hộ kinh doanh. Bắt buộc duy nhất phương pháp **Bình quân gia quyền cuối kỳ** cho giá vốn hàng bán; bãi bỏ lựa chọn FIFO cho mục đích thuế. | **Đã xác minh:** Phương pháp tính giá vốn đã rõ. Danh mục sổ kế toán và tên gọi cụ thể cần đối chiếu thêm phụ lục TT 152. |

---

## 3. TỔNG HỢP CÁC QUYẾT ĐỊNH THIẾT KẾ NGHIỆP VỤ (DECISION MATRIX)

### Quyết định 1: Giảm thuế GTGT & Phân loại hàng hóa
* **Bản chất nghiệp vụ:** Mức giảm thuế GTGT (0.8% thay vì 1.0%) gắn liền với **từng dòng mặt hàng và ngày xuất hóa đơn**, không được cào bằng toàn bộ kỳ thuế (do hàng chịu thuế TTĐB như bia, rượu, thuốc lá vẫn chịu 1.0%).
* **Thiết kế thực thi:**
  - Bảng `Products` bổ sung cột phân loại thuế `TaxCategory` (chuỗi định danh, ví dụ: `STANDARD_1PCT`, `EXEMPT_REDUCTION`, `NON_TAXABLE`). Không dùng cột boolean đơn lẻ để sẵn sàng đáp ứng khi danh mục loại trừ của NQ 204 có nhiều nhóm khác nhau.
  - Khi xuất bán, dòng `OrderItems` chốt cứng `AppliedVatRate`, `VatAmount`, `VatDiscountAmount`.
  - **Quy tắc làm tròn:** Làm tròn số thuế cố định **theo từng dòng mặt hàng**: `Round(Quantity * UnitPrice * AppliedVatRate)`. Tổng thuế hóa đơn bằng tổng thuế các dòng (`SUM(LineVatAmount)`), triệt tiêu độ lệch số học khi tổng hợp kỳ.
  - **Dòng hàng trả lại (Return Item):** Bắt buộc kế thừa đúng `AppliedVatRate` từ dòng bán gốc ban đầu.

### Quyết định 2: Thuế suất TNCN & Tùy chọn 2 phương pháp cho Nhóm 2
* **Phân loại ngưỡng áp dụng:**
  - **Nhóm 1 (Doanh thu ≤ 1 tỷ):** Miễn thuế GTGT và TNCN. Ghi chép đơn giản theo Sổ S1a.
  - **Nhóm 2 (Doanh thu 1 – 3 tỷ):** Kê khai theo Quý. Cho phép chủ hộ chọn 1 trong 2 cách tính:
    - *Cách 1 (Mặc định):* `(Doanh thu năm - 1.000.000.000) * 0.5%`.
    - *Cách 2 (Kê khai trên lợi nhuận):* `(Doanh thu - Chi phí hợp lý) * 15%`.
  - **Nhóm 3 (Doanh thu 3 – 50 tỷ):** Thuế suất cố định **17%** trên thu nhập tính thuế: `(Doanh thu - Giá vốn - Chi phí hợp lý) * 17%`.
* **Cơ chế bảo vệ chủ hộ Nhóm 2 khi chọn Cách 2:**
  - Hệ thống hiển thị bảng so sánh trực quan số thuế phải nộp giữa Cách 1 và Cách 2 trước khi bấm chốt.
  - Hiển thị tỷ lệ chi phí có hóa đơn/chứng từ hợp lệ trên tổng chi phí; cảnh báo đỏ nếu tỷ lệ thấp (nguy cơ bị cơ quan thuế ấn định thuế do thiếu chứng từ đầu vào hợp pháp).
  - Bắt buộc chủ hộ tick xác nhận cam kết tự chịu trách nhiệm về tính hợp pháp của hóa đơn chứng từ trước khi kích hoạt Cách 2.

### Quyết định 3: Phạm vi sản phẩm đối với Nhóm 4 (> 50 tỷ)
* **Quyết định về phạm vi sản phẩm (Product Scope Decision):**
  - MT-GRMS định vị là giải pháp bán lẻ và vận hành cho chuỗi tạp hóa quy mô vừa và nhỏ (Nhóm 1, 2, 3).
  - Hệ thống **hoàn toàn không xây dựng phân hệ kế toán kép (Double-entry Ledger)** như doanh nghiệp để tránh biến hệ thống thành ERP cồng kềnh.
* **Xử lý khi cửa hàng vượt ngưỡng 50 tỷ giữa năm:**
  - Không chặn cứng hoạt động bán hàng; hệ thống vẫn cho phép tính và chốt kỳ bình thường cho đến hết năm tài chính.
  - Hiển thị cảnh báo tiến trình trên Dashboard ở mốc **80% (40 tỷ)** và **100% (50 tỷ)** để chủ hộ chuẩn bị chuyển đổi sang mô hình Doanh nghiệp hoặc chuyển đổi phần mềm kế toán.
  - **Nguyên tắc xác định ngưỡng doanh thu:** Cần xác nhận rõ ngưỡng tính theo **Mã số thuế cá nhân của chủ hộ (toàn bộ các chi nhánh cộng lại)** hay tính theo từng địa điểm kinh doanh riêng lẻ. MT-GRMS mặc định tổng hợp doanh thu theo toàn bộ chuỗi của Tenant để cảnh báo an toàn nhất.
* **Ghi chú về thuế GTGT Nhóm 4:** Đánh dấu **Cần xác nhận** (Một số tài liệu ghi nhận nhóm trên 50 tỷ vẫn áp dụng tỷ lệ % trên doanh thu nếu chưa chuyển đổi công ty, trái với nhận định áp dụng phương pháp khấu trừ).

### Quyết định 4: Cơ chế bù trừ & Chuyển lỗ qua năm
* **Lựa chọn thiết kế thận trọng (Không phải kết luận pháp lý):**
  - Hiện chưa tìm thấy văn bản quy phạm pháp luật cho phép hoặc cấm rõ ràng việc Hộ kinh doanh được chuyển lỗ sang năm sau (quy tắc chuyển lỗ 5 năm tại Điều 16 Luật Thuế TNDN 2025 chỉ điều chỉnh pháp nhân doanh nghiệp).
  - Do việc tự ý trừ lỗ năm trước khi thanh tra thuế có rủi ro bị truy thu rất cao, hệ thống chọn phương án thận trọng: **Chỉ cho phép bù trừ lỗ giữa các Quý trong cùng 1 năm tài chính (YTD True-up); mặc định KHÔNG chuyển lỗ sang năm sau.**
  - Thời hạn quyết toán năm dứt điểm chậm nhất là ngày 31/03 năm sau.
  - Lưu trữ trường dự phòng trong CSDL và giữ cờ tính năng `EnableLossCarryAcrossYears = false` trong cấu hình; chỉ kích hoạt khi chủ hộ có văn bản chấp thuận chính thức từ Chi cục Thuế quản lý.

### Quyết định 5: Chiến lược tính Giá vốn hàng bán (COGS Engine) theo TT 152
* **Phương pháp pháp lý:** Áp dụng **Bình quân gia quyền cuối kỳ** cho toàn bộ hàng hóa tính thuế và Sổ S2d. Bãi bỏ FIFO cho mục đích tính thuế (dữ liệu `Batches` chỉ dùng cho quản lý hạn dùng/truy xuất kho nội bộ).
* **Hiệu năng và lưu trữ cho chuỗi 5.000 SKU:**
  - Chạy truy vấn tổng hợp theo SKU tại thời điểm chốt kỳ: `Đơn giá = (Giá trị tồn đầu + Giá trị nhập) / (Số lượng tồn đầu + Số lượng nhập)`.
  - **Tối ưu CSDL:** Không lưu toàn bộ 5.000 dòng SKU vào cột JSONB của snapshot (tránh phình to dữ liệu). Snapshot chỉ lưu tổng giá vốn, số lượng SKU và **mã băm dữ liệu tính giá vốn (`CogsCalculationHash`)** để đối chiếu khi cần tính lại từ ledger kho.
* **Xử lý các tình huống thực tế:**
  - *Bán trước khi nhập (tồn âm tạm thời):* Nếu trong kỳ có phiếu nhập bổ sung thì công thức cuối kỳ tự động cân bằng. Nếu đến cuối kỳ vẫn chưa có phiếu nhập, hệ thống gắn cờ cảnh báo và tạm tính theo giá nhập gần nhất, không để giá trị âm trên sổ thuế.
  - *Hàng trả lại NCC:* Ghi giảm trực tiếp giá trị và số lượng nhập trong kỳ.
  - *Hàng hủy / Khuyến mại:* Bắt buộc đính kèm số biên bản hủy / quyết định khuyến mại; nếu không có chứng từ hợp lệ sẽ không được tính vào chi phí hợp lý.
  - *Fallback ước tính 70%:* **Chỉ dùng cho mục đích xem trước (preview)**. Nghiêm cấm hoặc chặn cảnh báo mạnh khi chốt kỳ chính thức; nếu chốt bằng số ước tính phải có xác nhận cam kết trách nhiệm của chủ hộ.

### Quyết định 6: Danh mục Sổ kế toán & Định dạng trích xuất
* **Danh mục sổ theo Thông tư 152/2025/TT-BTC:**
  - `S1a-HKD`: Sổ doanh thu bán hàng hóa, dịch vụ (Ghi chép đơn giản cho Nhóm 1).
  - `S2b-HKD`: Sổ chi tiết doanh thu bán hàng hóa, dịch vụ.
  - `S2c-HKD`: Sổ chi tiết doanh thu, chi phí *(Đánh dấu: Cần đối chiếu chính xác phụ lục mẫu biểu TT 152 trước khi cố định giao diện)*.
  - `S2d-HKD`: Sổ chi tiết vật liệu, dụng cụ, sản phẩm, hàng hóa (Nhập - Xuất - Tồn theo giá bình quân cuối kỳ).
  - `S2e-HKD`: **Sổ chi tiết tiền** *(tiền mặt, tiền gửi ngân hàng)*.
  - `S3a-HKD`: **Sổ theo dõi các nghĩa vụ thuế khác** *(thuế TTĐB, tài nguyên, BVMT, đất...)*.
* **Tờ khai thuế & Xác thực dữ liệu:**
  - Mẫu tờ khai nộp thuế: Ánh xạ theo **Mẫu 01/CNKD**.
  - Mã xác thực toàn vẹn: Trên bản in PDF thể hiện nhãn **"Mã kiểm tra toàn vẹn dữ liệu nội bộ (HMAC-SHA256)"** (không gọi là chữ ký số/chữ ký điện tử của chủ hộ vì đây là mã băm đối xứng nội bộ).
  - **Thời hạn lưu trữ:** Áp dụng chính sách lưu trữ và sao lưu dữ liệu Snapshot và Sổ kế toán tối thiểu **5 năm** theo quy định.

---

## 4. THIẾT KẾ CƠ SỞ DỮ LIỆU TỐI GIẢN (KHÔNG THÊM BẢNG THỪA)

Toàn bộ các yêu cầu trên được ánh xạ vào đúng **4 bảng hiện có** trong CSDL:

```
┌─────────────────────────────────┐           ┌─────────────────────────────────┐
│            Products             │           │           OrderItems            │
├─────────────────────────────────┤           ├─────────────────────────────────┤
│ + TaxCategory (varchar, indexed)│ ───1:N─── │ + AppliedVatRate (decimal)      │
│   (STANDARD_1PCT /              │           │ + VatAmount (decimal)           │
│    EXEMPT_REDUCTION / ...)      │           │ + VatDiscountAmount (decimal)   │
└─────────────────────────────────┘           └─────────────────────────────────┘
                                                               ▲
                                                               │ (Làm tròn theo dòng)
                                                               │
┌─────────────────────────────────┐           ┌─────────────────────────────────┐
│         TenantSettings          │           │      HouseholdTaxSnapshots      │
├─────────────────────────────────┤           ├─────────────────────────────────┤
│ + SettingKey (varchar)          │           │ + PeriodType (MONTH/QTR/YEAR)   │
│ + SettingValue (text/json)      │           │ + PolicySnapshotJson (text)     │
│   (Cấu hình tỷ lệ, Feature Flag)│           │ + CumulativeLossRemaining (dec) │
│                                 │           │ + CogsCalculationHash (varchar) │
│                                 │           │ + IntegrityCheckHash (varchar)  │
└─────────────────────────────────┘           └─────────────────────────────────┘
```

1. **Bảng `Products`:**
   - Thêm cột `TaxCategory` (kiểu string, có index): Lưu phân loại thuế hàng hóa (`STANDARD_1PCT`, `EXEMPT_REDUCTION`, `NON_TAXABLE`).
2. **Bảng `OrderItems`:**
   - `AppliedVatRate` (decimal, vd: 0.008 hoặc 0.010): Tỷ lệ thuế áp dụng tại thời điểm bán.
   - `VatAmount` (decimal): Số tiền thuế GTGT thực thu.
   - `VatDiscountAmount` (decimal): Số tiền thuế GTGT được giảm theo chính sách.
   - Dòng hàng trả lại (`OrderReturnItems`): Kế thừa nguyên vẹn các giá trị thuế từ dòng gốc.
3. **Bảng `HouseholdTaxSnapshots`:**
   - `PolicySnapshotJson` (text): Bản chụp đóng băng cấu hình chính sách đã dùng tại thời điểm chốt kỳ (`{ "vatRateStandard": 0.01, "vatRateReduced": 0.008, "pitRate": 0.17, "taxGroup": "GROUP_3", "appliedMethod": "PROFIT_BASED" }`). Đảm bảo tính bất biến tuyệt đối khi `TenantSettings` thay đổi sau này.
   - `CumulativeLossRemaining` (decimal): Lưu số lỗ lũy kế (dự phòng cho cơ chế bù trừ).
   - `CogsCalculationHash` (varchar): Mã băm xác thực nguồn tính giá vốn bình quân cuối kỳ.
   - `IntegrityCheckHash` (varchar): Mã HMAC-SHA256 bảo vệ toàn vẹn bản ghi snapshot.
4. **Bảng `TenantSettings`:**
   - Lưu trữ dạng JSON/Key-Value các tham số: cờ bật/tắt chính sách giảm GTGT, cờ cho phép chuyển lỗ qua năm (`EnableLossCarryAcrossYears: false`), phương pháp tính thuế Nhóm 2 (`Tier2PitMethod: "REVENUE_EXCESS"`).