# WORKLOG: SUBSCRIPTION-STORAGE - Hạn mức 2 GB và mua thêm dung lượng

- **Ngày thực hiện**: 2026-11-12
- **Người thực hiện**: AI Agent
- **Change liên kết**: `subscription-tier-quota-redesign`
- **Loại thay đổi**: Feature

## Mục đích

Chuẩn hóa gói STANDARD và PRO có 2 GB lưu trữ cơ bản, cho phép chủ gian hàng mua thêm dung lượng với giá 15.000 đ/GB/tháng (chưa VAT), đồng thời kiểm soát quota theo tenant.

## File chính đã thay đổi

- `Tenant.cs`, `TenantMedia.cs`, `AppDbContext.cs`, `DbInitializer.cs`: lưu quota mua thêm, ngày hết hạn, dung lượng đã dùng và metadata media.
- `TenantStorageController.cs`: API usage, báo giá và mua thêm giữa chu kỳ; trừ ví và chống giao dịch lặp.
- `TenantStoreController.cs`: tính phí dung lượng khi mua/gia hạn gói.
- `TenantBranchAddonController.cs`: báo giá và mua thêm chi nhánh giữa kỳ riêng cho Gói PRO.
- `MediaController.cs`: thư mục Cloudinary theo tenant, chặn vượt quota, ghi nhận upload và hoàn quota khi xóa.
- `PlanDtos.cs`, `SubscriptionPlanService.cs`: trả hạn mức 2 GB cho STANDARD/PRO.
- `MediaUploadTests.cs`: cập nhật test theo tenant context và persistence.

## Xác minh

- `dotnet build MT_GRMS_BACKEND.sln -c Release --no-restore`: PASS (còn warning có sẵn ở module khác).
- `dotnet test tests/MT_GRMS.UnitTests/MT_GRMS.UnitTests.csproj -c Release --no-build`: được chạy trong phiên apply.
- Frontend `npm.cmd run build`: PASS.

## Ghi chú triển khai

`DbInitializer` tự bổ sung các cột/bảng cần thiết khi khởi động. Dữ liệu media cũ không có metadata kích thước cần được backfill riêng nếu muốn phản ánh chính xác vào `StorageUsedBytes`.
