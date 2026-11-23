# BE-STORE — Kiểm tra dữ liệu cập nhật gian hàng

- Ngày thực hiện 2026-11-22
- Người thực hiện Codex
- Nhánh `dev`
- Loại thay đổi Bugfix

## Mục đích

Chặn dữ liệu vượt độ dài cột và số điện thoại sai định dạng trước khi lưu. Kiểm tra độ dài sau khi bỏ khoảng trắng đầu và cuối.

## Thay đổi

- `src/MT_GRMS.Application/Admin/Validators/TenantValidators.cs` kiểm tra tên bắt buộc (1–150 ký tự), số điện thoại tùy chọn (9–12 chữ số, có thể có dấu `+` ở đầu), mã số thuế (50 ký tự), tỉnh/thành phố (100 ký tự) và địa chỉ (255 ký tự).
- `src/MT_GRMS.Api/Controllers/TenantStoreController.cs` trả HTTP 400 với mã `VALIDATION_ERROR` cho dữ liệu không hợp lệ ở `PUT /api/tenant/store-info`.
- Unit test bao phủ giá trị rỗng, đúng biên, vượt biên và xác minh API trả lỗi mà không gọi dịch vụ cập nhật.

Thay đổi này không yêu cầu migration.

## Kiểm thử

- Biên dịch API và chạy riêng 33 ca backend bằng chương trình kiểm thử tạm. Tất cả đều đạt.
- Bộ unit test backend đầy đủ bị chặn lúc biên dịch bởi lỗi trong các test hiện có, gồm test thanh toán, ví và tồn kho.
- Frontend build thành công và đạt 12 ca kiểm thử màn hình thông tin gian hàng.
