# RULE: BẮT BUỘC GHI FILE WORKLOG TÓM TẮT (.MD) CHO BACKEND

> **Mã quy tắc**: `RULE-01-WORKLOG-BACKEND`  
> **Repository**: `MT_GRMS_BACKEND` (ASP.NET Core 8)  
> **Mục tiêu**: Giúp Quản lý (Manager / Lead Reviewer) dễ dàng kiểm soát thay đổi, review diff nhanh chóng và merge an toàn không phát sinh xung đột.

---

## 1. Hành Động Bắt Buộc Khi Code & Commit

Mỗi khi AI Coding Agent hoặc Developer:
- Viết mới hoặc sửa đổi code trong `src/` (Domain, Infrastructure, Application, Api) hoặc `tests/`.
- Thực hiện sửa lỗi (Bugfix), tái cấu trúc (Refactor), hoặc tạo Migrations.
- Chuẩn bị tạo Git Commit hoặc mở Pull Request.

**BẮT BUỘC THỰC HIỆN ĐỦ 4 BƯỚC:**

### Bước 1: Tạo / Cập Nhật File Tóm Tắt Markdown (.md)
- Lưu tại: `docs/worklogs/YYYY-MM-DD_<MÃ_TASK>_<AUTHOR>_<MÔ_TẢ_NGẮN>.md`
  *(Ví dụ: `2026-10-02_FE-02_DUC_fefo-batch-engine.md`)*
- Khung mẫu:
  ```markdown
  # WORKLOG: [MÃ_TÍNH_NĂNG] - [TIÊU_ĐỀ_NGẮN_GỌN]

  - **Ngày thực hiện**: YYYY-MM-DD
  - **Người thực hiện**: [Tên thành viên / AI Agent]
  - **Nhánh Git (Branch)**: `feat/FE-xx-...` hoặc `fix/...`
  - **Mã tính năng liên kết**: [FE-xx](../../../DOCS_MT_GRMS/specs/FE-xx/spec.md)
  - **Loại thay đổi**: [Feature / Bugfix / Refactor / Architecture]

  ---

  ### 1. Mục Đích Thay Đổi (Why)
  - [Giải thích ngắn gọn lý do thay đổi và bài toán được giải quyết]

  ### 2. Danh Sách Tệp Đã Sửa Đổi & Thêm Mới (What)
  - `src/MT_GRMS.Domain/Entities/...`: [Thêm/sửa Entity gì]
  - `src/MT_GRMS.Infrastructure/Persistence/Configurations/...`: [Thêm Fluent API gì]
  - `src/MT_GRMS.Application/.../Dtos/...`: [Thêm Request/Response DTO nào]
  - `src/MT_GRMS.Application/.../Services/...`: [Cài đặt method xử lý logic nào]
  - `src/MT_GRMS.Api/Controllers/...`: [Thêm endpoint HTTP nào]

  ### 3. API Endpoints & Contract
  - **Method & Route**: `[GET / POST / PUT / DELETE] /api/v1/...`
  - **Request Body (DTO)**: ...
  - **Response Data (DTO)**: ...
  - **Database Migration**: [Tên migration hoặc Không có]
  - **Breaking Changes**: [Mô tả nếu có, nếu không ghi "Không có"]

  ### 4. Kiểm Thử & Xác Nhận (Verification)
  - [x] Đã chạy `dotnet build MT_GRMS_BACKEND.sln`: Thành công (0 lỗi, 0 cảnh báo).
  - [x] Đã chạy `dotnet test MT_GRMS_BACKEND.sln`: Đạt 100%.
  - [x] Đã kiểm tra thử nghiệm qua Swagger UI.

  ### 5. Ghi Chú Cho Manager Khi Duyệt & Merge (Manager Merge Notes)
  - **Mức độ rủi ro**: [Thấp / Trung bình / Cao]
  - **File trọng yếu cần review**: `[Tên file]`
  - **Hành động sau Merge**: Chạy update database / Cập nhật cấu hình / Báo Frontend
  ```

### Bước 2: Cập Nhật CHANGELOG.md
- Thêm 1-2 gạch đầu dòng vào `CHANGELOG.md` dưới mục `[Unreleased]` tương ứng (Added / Changed / Fixed).
- Dẫn link tới file worklog vừa tạo.

### Bước 3: Commit Git Kèm File Worklog
- Bắt buộc `git add` file worklog `.md` chung lượt với code.
- Commit message:
  ```text
  <type>(<scope>): <mô tả ngắn> [worklog: docs/worklogs/<tên-file>.md]
  ```

### Bước 4: Tạo Pull Request
- Đính kèm link file worklog `.md` vào phần mô tả PR.
- Manager duyệt PR dựa trên tóm tắt này và tiến hành merge.
