# Worklog: Cải Thiện API Cập Nhật Thông Tin Gian Hàng (Tenant Store Update)

- **Ngày thực hiện**: 2026-11-06
- **Mã task / Tính năng**: FE-STORE (Tenant Store Info Management)
- **Tác giả**: AI Coding Assistant
- **Mô tả ngắn**: Cải thiện API cập nhật thông tin gian hàng `PUT /api/tenant/store-info`, bổ sung endpoint `GET /api/tenant/subscriptions/history` phục vụ màn hình Lịch sử mua hàng gói dịch vụ của gian hàng, bổ sung cơ chế fallback `X-Tenant-Id` header và an toàn null-safety.

---

## 1. Mục đích (Why)
- Đảm bảo chủ gian hàng có thể cập nhật thông tin gian hàng (Tên chuỗi cửa hàng, Mã số thuế MST, Hotline CSKH, Tỉnh/Thành phố, Địa chỉ trụ sở chính) từ trang quản lý `/store-owner/subscriptions?tab=store-info`.
- Cung cấp API `GET /api/tenant/subscriptions/history` để trả về lịch sử mua hàng, đăng ký và gia hạn gói dịch vụ cho gian hàng (thay vì frontend dùng dữ liệu mock cứng).
- Xử lý linh hoạt khi claims `tenant_id` từ JWT token chưa sẵn sàng bằng cách fallback lấy từ `X-Tenant-Id` header của middleware hoặc query param.
- Đảm bảo an toàn null-safety khi người dùng cập nhật thông tin, không làm văng exception nếu `request.Name` trống.

---

## 2. Danh sách tệp & Nội dung thay đổi (What)
- [`src/MT_GRMS.Api/Controllers/TenantStoreController.cs`](file:///d:/FPT_University/Final/Grade/new/project/MT_GRMS_BACKEND/src/MT_GRMS.Api/Controllers/TenantStoreController.cs):
  - Viết method helper dùng chung `ResolveCurrentTenantAsync` để trích xuất tenant chính xác từ token hoặc header `X-Tenant-Id`.
  - Bổ sung action `[HttpGet("subscriptions/history")]` trả về danh sách lịch sử gói dịch vụ: kết hợp các giao dịch mua/gia hạn từ `WalletTransactions` và gói khởi tạo ban đầu khi mở gian hàng.
- [`src/MT_GRMS.Application/Admin/Dtos/TenantDtos.cs`](file:///d:/FPT_University/Final/Grade/new/project/MT_GRMS_BACKEND/src/MT_GRMS.Application/Admin/Dtos/TenantDtos.cs):
  - Định nghĩa DTO `SubscriptionOrderHistoryDto` (Code, PlanName, StartDate, EndDate, TotalAmount, PaymentMethod, Status, CreatedAt).
- [`src/MT_GRMS.Application/Admin/Services/TenantService.cs`](file:///d:/FPT_University/Final/Grade/new/project/MT_GRMS_BACKEND/src/MT_GRMS.Application/Admin/Services/TenantService.cs):
  - Bổ sung kiểm tra `!string.IsNullOrWhiteSpace(request.Name)` trước khi gán và trim `tenant.Name` để tránh lỗi `NullReferenceException`.

---

## 3. Chi tiết kỹ thuật (Technical Highlights)
- **API Endpoints**:
  - `PUT /api/tenant/store-info`
  - `GET /api/tenant/subscriptions/history`: Lấy lịch sử hợp đồng và giao dịch gói dịch vụ của gian hàng đang đăng nhập.
- **Request / Response Contracts**:
  - `SubscriptionOrderHistoryDto`: Chứa mã hợp đồng, tên gói cước, khoảng thời gian hiệu lực, tổng chi phí, phương thức thanh toán, trạng thái.
- **Breaking changes**: Không có, tương thích ngược 100%.

---

## 4. Kết quả kiểm thử (Verification)
- `dotnet build`: Build succeeded, 0 Error(s).
- API test: Gọi `GET /api/tenant/subscriptions/history` trả về HTTP 200 OK kèm dữ liệu hợp đồng gói cước chuẩn xác.

---

## 5. Ghi chú cho Manager (Manager Merge Notes)
- **Mức độ rủi ro**: Thấp (Low).
- **Lưu ý**: Thay đổi an toàn, tương thích ngược 100% với các client hiện hành.

