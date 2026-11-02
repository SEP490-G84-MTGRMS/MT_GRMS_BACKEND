# WORKLOG: FE-12, FE-13 - POS Checkout, Pre-Order, Return & Exchange, and Invoice Management

- **Ngày thực hiện**: 2026-11-02
- **Người thực hiện**: Son Nguyen Ba (@sonnguyenn10)
- **Nhánh Git (Branch)**: `feature/pos-v3`
- **Mã tính năng liên kết**: [FE-12 - POS Checkout & Sales](../../../DOCS_MT_GRMS/specs/FE-12/spec.md), [FE-13 - Return & Refund Management](../../../DOCS_MT_GRMS/specs/FE-13/spec.md)
- **Loại thay đổi**: Feature & Integration

---

### 1. Mục Đích Thay Đổi (Why)
- Triển khai phân hệ POS quầy siêu thị hoàn chỉnh: xử lý đơn bán lẻ thanh toán tiền mặt/VietQR/Ghi nợ, tạo và quản lý đơn đặt trước giữ kho FEFO (Pre-Order) với đặt cọc tối thiểu 10%, hoàn tất giao nhận đơn đặt trước.
- Hỗ trợ in hóa đơn nhiệt máy in POS-58 (khổ 43-58mm) và K80 phục vụ in bill trực tiếp tại quầy thu ngân.
- Cung cấp API tra cứu khách hàng thuần túy bằng Số Điện Thoại (`CustomersController`) và tìm kiếm nhanh sản phẩm qua Barcode/SKU cho máy quét USB HID.
- Hỗ trợ đổi/trả hàng chuẩn nghiệp vụ bán lẻ và đồng bộ dữ liệu giao dịch đa chi nhánh cho chuỗi siêu thị.

---

### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)
- `src/MT_GRMS.Api/Controllers/OrdersController.cs`: Bổ sung các API tạo đơn bán lẻ, tạo đơn đặt trước giữ kho, hoàn tất nhận hàng đặt trước, và đổi trả hàng.
- `src/MT_GRMS.Api/Controllers/CustomersController.cs`: API tìm kiếm khách hàng nhanh chóng qua số điện thoại và quản lý công nợ.
- `src/MT_GRMS.Api/Controllers/ProductsController.cs`: Bổ sung endpoint tra cứu sản phẩm nhanh bằng mã Barcode/SKU (`GET /api/v1/products/barcode/{barcode}`).
- `src/MT_GRMS.Application/PosOrders/Dtos/OrderDtos.cs`: Định nghĩa DTO cho Order, Pre-order, ReturnExchange và in ấn hóa đơn.
- `src/MT_GRMS.Application/PosOrders/IOrderService.cs` & `OrderService.cs`: Nghiệp vụ tính tiền, kiểm tra tồn kho FEFO, ghi nhận cọc, thanh toán nốt đơn đặt trước.
- `src/MT_GRMS.Application/PosOrders/IThermalPrintService.cs`: Hợp đồng dịch vụ in ấn biên lai nhiệt.
- `src/MT_GRMS.Infrastructure/Services/Printer/RawPrinterHelper.cs` & `ThermalPrintService.cs`: Cài đặt driver Winspool gửi lệnh RAW ESC/POS đến máy in hóa đơn.
- `src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`: Bổ sung seed dữ liệu đơn hàng mẫu, đơn đặt trước và tồn kho cho cả 3 chi nhánh (`BR-HN-01`, `BR-HN-02`, `BR-HCM-01`).
- `tests/MT_GRMS.UnitTests/PosOrderServiceTests.cs`: 100% Unit test cho OrderService covering logic đặt hàng, kiểm tra cọc và thanh toán.

---

### 3. API Endpoints & Contract
- `POST /api/v1/orders/pos`: Tạo hóa đơn bán lẻ tại quầy (tiền mặt / VietQR / nợ).
- `POST /api/v1/orders/pre-order`: Tạo đơn đặt trước giữ kho FEFO kèm tiền đặt cọc ($\ge 10\%$).
- `POST /api/v1/orders/pre-order/{id}/fulfill`: Hoàn tất giao nhận đơn đặt trước và thu phần tiền còn lại.
- `POST /api/v1/orders/{id}/return-exchange`: Xử lý đổi trả hàng hóa, hoàn tiền hoặc thu thêm chênh lệch.
- `GET /api/v1/orders/pos/invoices`: Danh sách hóa đơn và đơn đặt trước có phân trang, lọc theo chi nhánh.
- `GET /api/v1/products/barcode/{barcode}`: Tra cứu thông tin sản phẩm bằng Barcode/SKU.
- `GET /api/v1/customers/search`: Tìm kiếm khách hàng theo SĐT.

---

### 4. Kiểm Thử & Xác Nhận (Verification)
- [x] Đã tạo file worklog markdown trong thư mục `docs/worklogs/`.
- [x] Chạy biên dịch toàn bộ solution `MT_GRMS_BACKEND`: Thành công (0 lỗi).
- [x] Đã chạy unit test `MT_GRMS.UnitTests`: 168/168 tests PASS (100%).
- [x] Tuân thủ Clean Architecture: Controller chỉ giao tiếp qua Service và DTO.
- [x] Đã merge nhánh `dev` mới nhất và giải quyết toàn bộ xung đột (0 conflict).

---

### 5. Ghi Chú Cho PM & Frontend
- Frontend đã có nhánh tương ứng `add/ui-pos-v3` đồng bộ 100% với các endpoint trên.
- Hệ thống hỗ trợ đa chi nhánh: khi gọi API cần gửi kèm `X-Tenant-Id` hoặc thông tin đăng nhập hợp lệ.
