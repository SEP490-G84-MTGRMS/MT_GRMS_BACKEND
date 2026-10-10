# THƯ MỤC QUẢN LÝ WORKLOG NỘI BỘ BACKEND

> 📢 **THÔNG BÁO CHUYỂN ĐỔI QUẢN LÝ**:  
> Toàn bộ cơ chế ghi chép nhật ký công việc (worklog), phân chia task theo từng thành viên, kiểm tra review đa chiều và hỗ trợ Admin/PM duyệt merge code **ĐÃ ĐƯỢC CHUYỂN GIAO TOÀN DIỆN SANG AI TEAM KIT (SEP KIT & OPENSPEC)**.  
> Vị trí trung tâm: [`ai-team-kit/team-ai-knowledge/openspec/`](../../../ai-team-kit/team-ai-knowledge/openspec/)

---

## 📌 Cơ Chế Mới: Quản Lý Tập Trung Qua AI Team Kit (SEP Kit)

Thay vì tạo file `.md` thủ công trong thư mục này, mọi thành viên và AI Agent thực hiện theo quy trình chuẩn **SEP 6 bước**:

1. **Khởi tạo tính năng/sửa lỗi**:
   ```bash
   /sep-spec <Mô tả yêu cầu cần làm>
   ```
   *MCP tự động tạo Change trong OpenSpec, gắn người phụ trách (`assignee: @github_username`).*

2. **Quy trình 6 bước có Gate kiểm soát (Bắt buộc Approve trước khi qua bước)**:
   - **Bước 1 (`/sep-spec`)**: Đề xuất mục tiêu & phạm vi (`proposal.md`).
   - **Bước 2 (`/sep-brainstorm`)**: Thiết kế kỹ thuật & Delta Specs (`exploration.md`, `design-brief.md`, `specs.md`).
   - **Bước 3 (`/sep-verify-spec`)**: **Persona Review** phản biện 5 chiều (`review/skeptic.md`, `review/codebase.md`, ...) và lập danh sách task chi tiết (`tasks.md`).
   - **Bước 4 (`/sep-apply`)**: Thực hiện viết code C# và tick hoàn thành checklist trong `tasks.md`.
   - **Bước 5 (`/sep-test`)**: Báo cáo kiểm thử tự động `dotnet build` & `dotnet test` với kết quả bắt buộc: `verdict: PASS` (`test-report.md`).
   - **Bước 6 (`/sep-archive`)**: Đóng change, gộp spec vào spec gốc sau khi Admin đã merge code.

---

## 👨‍💻 Dành Cho Admin / PM Khi Review & Merge Pull Request

Admin / Tech Lead không cần lục tìm từng file worklog rời rạc:
1. Xem toàn bộ tiến độ của từng thành viên:
   ```bash
   cd ai-team-kit/team-ai-knowledge
   npm run sep -- list --all
   ```
2. Xem chi tiết change và checklist:
   ```bash
   npm run sep -- status <tên-change>
   ```
3. **Tiêu chí Merge PR**:
   - Persona review (`review/skeptic.md`) đã được giải quyết thoả đáng.
   - Toàn bộ task trong `tasks.md` đã được đánh dấu hoàn thành `[x]`.
   - `test-report.md` xác nhận `verdict: PASS` (Build 0 lỗi, Test 100% Pass).

---

## 📂 Lịch Sử Cũ
- Các file log giai đoạn trước (ví dụ: [`2026-09-30_INIT_ARCH_skeleton-setup.md`](./2026-09-30_INIT_ARCH_skeleton-setup.md)) đã được lưu trữ vào Knowledge Base tại: [`ai-team-kit/team-ai-knowledge/projects/MT-GRMS/sessions/2026-09-30-arch-clean-architecture-skeleton-setup.md`](../../../ai-team-kit/team-ai-knowledge/projects/MT-GRMS/sessions/2026-09-30-arch-clean-architecture-skeleton-setup.md).
