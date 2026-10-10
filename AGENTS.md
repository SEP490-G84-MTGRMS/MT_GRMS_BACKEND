# AGENTS.md — Quy Định Dành Cho AI Agent & Developer tại MT_GRMS_BACKEND

> **Dự án**: MT_GRMS_BACKEND (ASP.NET Core 8 Web API / Clean Architecture / EF Core Code-First)  
> **Cơ chế quản lý tập trung**: AI Team Kit (SEP Kit 6 Bước & OpenSpec)

---

## 📌 QUY TRÌNH PHÁT TRIỂN & QUẢN LÝ CODE TẬP TRUNG (AI TEAM KIT)

Toàn bộ quy trình từ đặc tả, chia việc theo người phụ trách, phản biện review, kiểm soát code hoàn thành và nghiệm thu merge PR được quản lý tập trung qua **AI Team Kit** (`ai-team-kit/team-ai-knowledge/openspec/`).

### 1. Phân Chia Công Việc Theo Từng Thành Viên (Assignee)
- Mỗi tính năng hoặc bugfix là một **Change** trong OpenSpec:
  - Khi bắt đầu, dev/agent chạy:
    ```bash
    /sep-spec <Mô tả chức năng/lỗi cần làm>
    ```
  - MCP Server tự động ghi nhận người phụ trách (`assignee: @github_username`).
  - Mỗi thành viên (Đức `@Ortus24`, Tâm, Sơn, An, Nhật...) làm việc trên Change tương ứng của mình.

### 2. Đẩy Lên Review Đa Chiều (Persona Multi-Agent Review)
- Trước khi viết bất kỳ dòng code nào, ở bước **3: `/sep-verify-spec`**, hệ thống bắt buộc chạy review 5 chiều:
  - **Skeptic** (`review/skeptic.md` - Bắt buộc): Phản biện lỗ hổng logic, edge cases, rủi ro đa người thuê (Multi-Tenancy).
  - **Codebase Reviewer** (`review/codebase.md`): Kiểm tra quy chuẩn Clean Architecture (Entity đóng gói, DTO, Fluent API, không vi phạm layer).
  - **Guardian** (`review/guardian.md`): Đảm bảo an ninh, RBAC, Data Isolation.
  - **Advocate** (`review/advocate.md`): Đảm bảo trải nghiệm dev và API contract nhất quán với Frontend.

### 3. Quản Lý Tiến Độ Code Đã Làm (Tasks Checklist)
- Các đầu việc được bóc tách chi tiết trong `tasks.md` theo thứ tự:
  1. Database (Fluent API configurations / Migrations).
  2. Domain & Application (Entity methods, DTOs, Service interfaces, Validators).
  3. API Controllers & Routes.
  4. Unit / Integration Tests.
- Ở bước **4: `/sep-apply`**, chỉ khi toàn bộ checklist trong `tasks.md` được đánh dấu `[x]` thì mới được chuyển sang bước kiểm thử.

### 4. Nghiệm Thu Kiểm Thử Tự Động (Test Report Verification)
- Ở bước **5: `/sep-test`**, Agent/Dev bắt buộc chạy kiểm thử thực tế:
  ```bash
  dotnet build MT_GRMS_BACKEND.sln
  dotnet test MT_GRMS_BACKEND.sln
  ```
- Kết quả được ghi vào `test-report.md` với `verdict: PASS` (0 Warning, 0 Error, 100% Tests Passed).

<<<<<<< HEAD
5. **Khi Mở Pull Request**:
   - Dán đường dẫn file worklog markdown vào phần mô tả PR theo [`pull_request_template.md`](./.github/pull_request_template.md).
   - Manager chỉ duyệt và bấm Merge sau khi đọc file tóm tắt này và xác nhận an toàn.
=======
### 5. Hỗ Trợ Admin & Tech Lead Review & Merge PR An Toàn
Admin / PM chỉ cần thực hiện 2 thao tác để kiểm tra trước khi bấm Merge PR trên GitHub:
1. **Kiểm tra danh sách và tiến độ toàn team**:
   ```bash
   cd ai-team-kit/team-ai-knowledge
   npm run sep -- list --all
   ```
2. **Kiểm tra chi tiết change cần merge**:
   ```bash
   npm run sep -- status <tên-change>
   ```
3. **Tiêu chí Admin cho phép Merge**:
   - [x] Change đã vượt qua bước Verify Spec và có persona review đầy đủ.
   - [x] 100% tasks trong `tasks.md` đã tick `[x]`.
   - [x] `test-report.md` đạt `verdict: PASS`.
   - [x] Không có xung đột Git với nhánh chính.
4. **Sau khi Merge**: Chạy `/sep-archive <tên-change>` để đưa delta spec vào spec gốc của dự án.
>>>>>>> fe14afd66b98e56ff5debae6a17c6104425dac5a

<!-- sep-kit:start -->
## Quy trình SEP (team-ai-knowledge)

Repo này làm theo quy trình SEP 6 bước: `/sep-spec` → `/sep-brainstorm` → `/sep-verify-spec` → `/sep-apply` → `/sep-test` → `/sep-archive`
(xem tiến độ: `/sep-status`). Mỗi bước kết thúc bằng Approve/Reject của user. Artifact nằm trong
<<<<<<< HEAD
`D:/FPT UNIVERSITY/FALL_2026_SEASON9/SEP490/sep490-repo/ai-team-kit/team-ai-knowledge/openspec/` và chỉ được ghi qua MCP server "sep".
=======
`D:/FPT_University/Final/Grade/new/project/ai-team-kit/team-ai-knowledge/openspec/` và chỉ được ghi qua MCP server "sep".
>>>>>>> fe14afd66b98e56ff5debae6a17c6104425dac5a

Rules chi tiết: `.cursor/rules/sep-kit-*.mdc` (Cursor), `.agents/rules/sep-kit-*.md` (Antigravity) —
sinh từ `sep-kit/rules` (karpathy-coding, mt-grms-stack, multi-agent-review, sep-knowledge, sep-workflow). Cập nhật: `npm run sep -- init <repo>`.
<!-- sep-kit:end -->
