# Kiểm tra dữ liệu nhập trên các form quản trị

Ngày 23/11/2026 · Codex · nhánh `reafator/validate-subcription`

## Quy tắc áp dụng

Các form tạo cửa hàng, khóa và gia hạn, cấu hình gói dịch vụ, hàng mẫu và cấu hình nền tảng kiểm tra các trường bắt buộc cùng giới hạn tương ứng trong EF Core. Tên cửa hàng, tên gói, tên hàng mẫu, tên ngân hàng và tên chủ tài khoản cần ít nhất 2 ký tự sau khi bỏ khoảng trắng hai đầu. Lý do khóa hoặc gia hạn cần 10–500 ký tự. Tiền dùng giới hạn 18 chữ số với 2 chữ số thập phân. Hệ số quy đổi phải lớn hơn 1, tối đa 4 chữ số thập phân.

## Thay đổi backend

- Các DTO và validator trong `Application/Admin` kiểm tra dữ liệu của API tạo và cập nhật. Trường giá bắt buộc trong JSON chấp nhận số 0. Khi tạo gói, thuộc tính `isActive` tùy chọn mặc định là `true`.
- `TenantService` kiểm tra thông tin trước khi cập nhật. `MasterCatalogService` kiểm tra toàn bộ danh sách đơn vị trước khi sửa hàng mẫu. `MasterCatalogExcelService` dùng chung validator và báo lỗi theo dòng, kể cả lỗi định dạng giá, thuế và quy cách.
- Căn cứ schema là cấu hình EF Core hiện có của tenant, gói dịch vụ, hàng mẫu, đơn vị và cấu hình nền tảng.

## Kiểm chứng

- Backend chạy 155 unit test thuộc nhóm admin và các validator liên quan, tất cả đạt.
- Frontend chạy 80 test cho admin và thông tin cửa hàng, tất cả đạt. TypeScript build đạt.
- Kiểm chứng bao gồm dữ liệu thiếu, rỗng, toàn khoảng trắng, cận dưới, cận trên, vượt độ dài hoặc độ chính xác, JSON thiếu trường và Excel lỗi. Dữ liệu thử dùng mock hoặc cơ sở dữ liệu kiểm thử.
