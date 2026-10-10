---
trigger: "glob"
<<<<<<< HEAD
description: "Convention kỹ thuật của MT-GRMS (backend .NET 8 Clean Architecture, frontend React 19 KiotViet Style) khi đọc/sửa code."
=======
description: "Convention kỹ thuật của MT-GRMS (backend .NET 8 Clean Architecture, frontend React 19 + antd) khi đọc/sửa code."
>>>>>>> fe14afd66b98e56ff5debae6a17c6104425dac5a
globs: "**/*.{cs,csproj,ts,tsx}"
---

# Convention MT-GRMS

<<<<<<< HEAD
## Backend (MT_GRMS_BACKEND.sln — .NET 8)
- Clean Architecture 4 tầng: `MT_GRMS.Domain` (entity đóng gói, enum) → `MT_GRMS.Application` (use case, DTO, FluentValidation)
  → `MT_GRMS.Infrastructure` (EF Core 8 + SQL Server Code-First, repository) → `MT_GRMS.Api` (controller mỏng chỉ nhận/trả DTO, JWT, Swagger).
- Mọi truy vấn lọc theo `TenantId` qua Global Query Filter; kiểm tra role/ownership ở Application.
- Lỗi trả ProblemDetails; không lộ stack trace. Tiền: `decimal(18,2)`. Thời gian lưu UTC.
- Đổi DB → EF Core migration mới; không sửa migration đã merge. Test: xUnit (`MT_GRMS.UnitTests`, `MT_GRMS.IntegrationTests`).

## Frontend (Vite + React 19 + TypeScript)
- **Mã màu & Phong cách chủ đạo**: Bắt buộc tuân thủ bảng màu chuẩn KiotViet Brand Blue từ HomePage (`#0066FF`, `#F7FAFF`, `#00C2FF`, `#00C853`, `#FF9500`, `#FF3B30`).
- **Phông chữ**: `Be Vietnam Pro` cho toàn bộ văn bản và `JetBrains Mono` cho tiền tệ, mã đơn hàng (`tabular-nums`).
- **Bo tròn**: Full Pill (`border-radius: 9999px`) cho mọi nút/badge; Card lớn bo góc `24px - 28px`.
- **Cấm animation nhún nhảy**: Tuyệt đối không dùng animation vô tận, bồng bềnh (`floatBounce`), rung lắc trên thẻ và badges.
- **Quy tắc Component hóa**: MỖI THẺ (Card, Stat, Modal, Banner, Table Item...) PHẢI được bóc tách thành một Component riêng biệt với TypeScript props, cấm viết monolithic code hàng trăm dòng trong file Page.
- **Kiến trúc thư mục**: Code theo feature: `src/features/<feature>/{pages,components,lib,types}` hoặc `src/pages/<page>/components/`.
- **Data & State**: TanStack Query, Zustand, React Router DOM.
- **Kiểm thử**: Chạy `npm run lint` (oxlint) và `npm run build` trước khi báo xong.
=======
## Backend (SGMS.sln — .NET 8)
- Clean Architecture: `SGMS.Domain` (entity, không phụ thuộc gì) → `SGMS.Application` (use case, DTO, FluentValidation)
  → `SGMS.Infrastructure` (EF Core 8 + SQL Server, repository) → `SGMS.API` (controller mỏng, JWT, Swagger).
- Mọi truy vấn lọc theo `TenantId`; kiểm tra role/ownership ở Application.
- Lỗi trả ProblemDetails; không lộ stack trace. Tiền: `decimal(18,2)`. Thời gian lưu UTC.
- Đổi DB → EF Core migration mới; không sửa migration đã merge. Test: xUnit (`SGMS.UnitTests`, `SGMS.IntegrationTests`).

## Frontend (Vite + React 19 + TS)
- Code theo feature: `src/features/<feature>/{pages,components,lib,types}`.
- UI nghiệp vụ dùng antd; style bằng Tailwind CSS v4 (không thêm file .css mới). Data: TanStack Query; state: Zustand.
- Lint bằng `npm run lint` (oxlint), build `npm run build` trước khi báo xong.
>>>>>>> fe14afd66b98e56ff5debae6a17c6104425dac5a
