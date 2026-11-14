# WORKLOG: FE-10 - Tổng quan Chủ cửa hàng bằng dữ liệu thật

- **Ngày thực hiện**: 2026-11-14
- **Người thực hiện**: AI Agent
- **Nhánh Git**: `refactor/store-owner-dashboard-real-data`
- **Change SEP**: `store-owner-dashboard-real-data`
- **Loại thay đổi**: Feature

## Mục đích

Màn hình Tổng quan Chủ cửa hàng đang hiển thị nhiều chỉ số mẫu. Bổ sung API tổng hợp đọc dữ liệu bán hàng, P&L, PO, phiếu nhập, kho và nhân viên theo tenant, kỳ và chi nhánh để frontend hiển thị số liệu thật.

## Tệp chính

- `src/MT_GRMS.Application/Reports/Dtos/StoreOwnerDashboardDtos.cs`: DTO trả dữ liệu dashboard.
- `src/MT_GRMS.Application/Reports/IStoreOwnerDashboardService.cs`: contract dịch vụ.
- `src/MT_GRMS.Infrastructure/Services/StoreOwnerDashboardService.cs`: tổng hợp dữ liệu từ P&L và các bảng vận hành hiện có.
- `src/MT_GRMS.Infrastructure/DependencyInjection.cs`: đăng ký dịch vụ.
- `src/MT_GRMS.Api/Controllers/StoreOwnerDashboardController.cs`: GET dashboard, kiểm tra role và tenant trong JWT.

## API và dữ liệu

- `GET /api/store-owner/dashboard?period=today|yesterday|last7days|thisMonth&branchId=<guid>`.
- Chỉ cho StoreOwner; TenantId đối chiếu JWT và context, branchId phải thuộc tenant.
- Chỉ số P&L dùng dịch vụ báo cáo sẵn có. PO/GRN/kho/nhân viên là truy vấn đọc có lọc tenant và branch.
- Khi chọn Hôm nay/Hôm qua, doanh thu được gom theo 24 giờ Việt Nam từ đơn hợp lệ và bút toán hoàn hàng; kỳ 7 ngày/tháng vẫn gom theo ngày.
- Không có migration hoặc breaking change của API hiện tại.

## Kiểm chứng

- `dotnet build src/MT_GRMS.Api/MT_GRMS.Api.csproj --no-restore -p:UseAppHost=false -p:BaseOutputPath=obj/dashboard-bin/ -m:1 --verbosity quiet`: thành công, 0 lỗi; còn 21 cảnh báo từ các file khác.
- Frontend `npm run build` và oxlint các file liên quan: thành công.
- Bổ sung biểu đồ theo giờ: build backend và frontend thành công, chưa có lỗi biên dịch mới.
- Đã rà soát công thức P&L dùng chung, trạng thái PO, mốc `ConfirmedAtUtc` của GRN, bộ lọc tenant/chi nhánh và các trạng thái giao diện. Unit test P&L hiện có không hoàn thành trong môi trường này vì testhost đứng sau khi khởi chạy; không ghi nhận kết quả PASS.
- Chưa xác nhận dữ liệu bằng phiên đăng nhập thực trên môi trường triển khai; cần đối chiếu KPI với P&L và chứng từ thật trong bước SEP Test.

## Ghi chú review

Kiểm tra mapping trạng thái PO và mốc `ConfirmedAtUtc` của GRN khi đối chiếu dữ liệu. Endpoint chỉ đọc, không thêm bảng mới.
