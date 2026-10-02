# AGENTS.md — Quy Định Dành Cho AI Agent & Developer tại MT_GRMS_BACKEND

> **Dự án**: MT_GRMS_BACKEND (ASP.NET Core 8 Web API / Clean Architecture / EF Core Code-First)  
> **Quy tắc cốt lõi**: `RULE-WORKLOG-AND-COMMIT`

---

## 📌 QUY TẮC BẮT BUỘC: GHI FILE WORKLOG (.MD) KHI CODE & COMMIT

Bất kỳ khi nào AI Agent hoặc Developer thực hiện viết code, sửa code, refactor hoặc chuẩn bị commit trong repository này:

### 1. Hành Động Bắt Buộc:
1. **Tạo / Cập nhật file Markdown tóm tắt** tại `docs/worklogs/`:
   - Tên file: `YYYY-MM-DD_<MÃ_TÍNH_NĂNG>_<AUTHOR>_<MÔ_TẢ_NGẮN>.md`
   - *Ví dụ*: `2026-10-02_FE-02_DUC_fefo-batch-engine.md`, `2026-10-02_BUG-05_AI_fix-tenant-filter.md`.
   - Có thể sao chép khung mẫu chuẩn từ [`docs/worklogs/WORKLOG_TEMPLATE.md`](./docs/worklogs/WORKLOG_TEMPLATE.md).

2. **Nội Dung Bắt Buộc Phải Có Trong File Worklog**:
   - **Mục đích thay đổi (Why)**: Bài toán giải quyết, lý do sửa đổi.
   - **Danh sách file & Tóm tắt chi tiết (What)**: Liệt kê cụ thể từng file trong `src/` hoặc `tests/` và tóm tắt những gì đã sửa đổi/thêm mới (method, class, DTO, configuration).
   - **API Contracts & Database**:
     - Endpoint HTTP (`[GET / POST / PUT / DELETE] /api/v1/...`).
     - Request / Response DTO.
     - Database changes: Entity mới, Fluent API configuration, EF Core Migration (nếu có).
     - Breaking Changes: Ảnh hưởng đến Frontend hay service khác.
   - **Kết quả kiểm thử**:
     - `dotnet build MT_GRMS_BACKEND.sln`: 0 lỗi, 0 cảnh báo.
     - `dotnet test MT_GRMS_BACKEND.sln`: Pass 100%.
   - **Chỉ Dẫn Dành Cho Manager Khi Duyệt & Merge PR**:
     - Đánh giá mức độ rủi ro (Low / Medium / High).
     - Các file Manager cần review kỹ nhất.
     - Thao tác cần thực hiện sau khi merge (chạy `dotnet ef database update`, cập nhật `appsettings.json`, báo Frontend).

3. **Cập Nhật CHANGELOG.md**:
   - Thêm 1-2 dòng tóm tắt công việc vào [`CHANGELOG.md`](./CHANGELOG.md) dưới mục `[Unreleased]`.

4. **Khi Commit Code**:
   - File worklog markdown PHẢI được `git add` và commit cùng lượt với các file code C#.
   - Commit message mẫu:
     ```text
     feat(FE-xx): <tóm tắt ngắn> [worklog: docs/worklogs/<tên-file>.md]
     ```

5. **Khi Mở Pull Request**:
   - Dán đường dẫn file worklog markdown vào phần mô tả PR theo [`pull_request_template.md`](./.github/pull_request_template.md).
   - Manager chỉ duyệt và bấm Merge sau khi đọc file tóm tắt này và xác nhận an toàn.
