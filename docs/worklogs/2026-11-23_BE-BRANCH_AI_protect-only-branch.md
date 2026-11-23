# WORKLOG — BE-BRANCH — Bảo vệ chi nhánh duy nhất

- **Ngày thực hiện** 2026-11-23
- **Người thực hiện** AI
- **Nhánh Git** `reafator/validate-subcription`
- **Loại thay đổi** Bugfix

## Mục đích

Gian hàng có một chi nhánh cần giữ chi nhánh đó làm trụ sở chính để chủ cửa hàng tiếp tục sử dụng các chức năng theo chi nhánh.

## Thay đổi

- `AuthService.cs` đánh dấu chi nhánh tạo lúc đăng ký làm trụ sở chính.
- `BranchService.cs` đánh dấu chi nhánh đầu tiên khi tạo, trả trụ sở chính trong API đọc khi gian hàng chỉ có một chi nhánh chưa xóa và giữ cờ khi lưu. API trạng thái và xóa kiểm tra tổng số chi nhánh trong transaction Serializable. Chi nhánh duy nhất đã ngừng hoạt động trước đây vẫn có thể được kích hoạt lại.
- `BranchManagementTests.cs` kiểm tra tạo chi nhánh đầu tiên, đọc và lưu chi nhánh duy nhất, chặn ngừng hoạt động và xóa, kích hoạt lại, và phân biệt danh sách lọc với tổng số chi nhánh.

## API và dữ liệu

`PATCH /api/branches/{id}/status` trả `CANNOT_DISABLE_ONLY_BRANCH` khi yêu cầu ngừng hoạt động chi nhánh duy nhất. `DELETE /api/branches/{id}` tiếp tục trả `CANNOT_DELETE_HEADQUARTER` với trụ sở chính đã lưu và trả `CANNOT_DELETE_ONLY_BRANCH` với chi nhánh duy nhất trong dữ liệu cũ chưa được đánh dấu. Thay đổi dùng cột `IsHeadquarter` hiện có.

## Kiểm thử

50 ca thuộc `BranchManagementTests` và `BranchesControllerTests` đạt khi chạy `dotnet test`. Hai ca tương tác giao diện đạt khi chạy Vitest.
