---
trigger: "always_on"
description: "Quy trình SEP 6 bước (/sep-spec → /sep-archive) bắt buộc khi làm chức năng, sửa lỗi hoặc refactor trong dự án MT-GRMS."
---

# Quy trình SEP — bắt buộc

Mọi chức năng mới, sửa lỗi hoặc refactor đáng kể đi qua 6 bước, mỗi bước là một lệnh:

| # | Lệnh | Kết quả bắt buộc |
|---|---|---|
| 1 | `/sep-spec <mô tả>` | chọn schema (feature / bug-fix / refactor), `proposal.md` |
| 2 | `/sep-brainstorm` | `exploration.md`, `design-brief.md` (chỉ feature), `specs.md` dạng delta |
| 3 | `/sep-verify-spec` | review persona (`review/skeptic.md` bắt buộc), `tasks.md` |
| 4 | `/sep-apply` | code + tick hết `tasks.md` |
| 5 | `/sep-test` | `test-report.md` với `verdict: PASS` |
| 6 | `/sep-archive` | archive + merge specs vào `openspec/specs/` |

`/sep-status` xem tiến độ và chọn change đang làm.

## Luật
- User yêu cầu code một chức năng / sửa lỗi mà **chưa có change** → đề nghị bắt đầu bằng `/sep-spec` trước khi code.
  Chỉnh sửa rất nhỏ (typo, đổi text, format) thì không cần.
- Chỉ ghi file trong `openspec/` bằng tool MCP `sep_*` của server "sep". Không tạo/sửa tay, không sửa `.status`.
- Không bỏ qua bước. Tool trả lỗi ⛔ → dừng, báo user bước còn thiếu và lệnh cần chạy.
- **Gate duyệt**: kết thúc mỗi bước (1–5), tóm tắt và hỏi user Approve / Reject. Chỉ gọi `sep_duyet_buoc` sau khi user trả lời rõ — không bao giờ tự duyệt.
- Bỏ trống tên change = change đang làm (📌). Đổi change: `sep_chon_change`.
- Không thấy tool `sep_*` → bảo user chạy `npm run sep -- install` trong `team-ai-knowledge` rồi khởi động lại IDE.
- Tool báo "chưa push" → nhắc `git push` trong `team-ai-knowledge` để TeamSpec thấy tiến độ.
