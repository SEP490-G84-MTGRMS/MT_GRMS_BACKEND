# Worklog: Sửa lỗi khóa tenant khi upload ảnh

## Mục tiêu

Khắc phục lỗi PostgreSQL `42703: column "Id" does not exist` khiến API upload ảnh nhân viên và xóa media trả HTTP 500 trước khi gọi Cloudinary.

## Nguyên nhân

Thuộc tính `Tenant.Id` được Entity Framework ánh xạ vào cột vật lý `Tenants.TenantID`, nhưng hai câu SQL khóa bản ghi bằng `FOR UPDATE` trong `MediaController` lại truy vấn cột `Id`.

## File đã thay đổi

- `src/MT_GRMS.Api/Controllers/MediaController.cs`: dùng đúng cột `TenantID` cho luồng upload và xóa ảnh.
- `tests/MT_GRMS.UnitTests/MediaUploadTests.cs`: bổ sung kiểm thử ánh xạ khóa chính tenant.
- `CHANGELOG.md`: ghi nhận bản sửa trong mục Unreleased.

## Xác minh

- Chạy nhóm unit test `MediaUploadTests`.
- Build backend để kiểm tra biên dịch.

## Ghi chú cho Lead

Cảnh báo `QuerySplittingBehavior.SingleQuery` trong log không gây lỗi upload; lỗi dừng luồng là PostgreSQL 42703 do sai tên cột.
