---
trigger: "model_decision"
description: "Cách giao việc cho sub-agent / persona review (skeptic, guardian, advocate, codebase) trong /sep-verify-spec và khi cần review độc lập."
---

# Giao việc cho sub-agent

- Persona review nằm trong MCP (`sep_xem_persona`) và được cài sẵn thành sub-agent `sep-skeptic`, `sep-guardian`,
  `sep-advocate`, `sep-codebase` cho Cursor / Claude Code.
- Chạy **song song** các persona độc lập; mỗi sub-agent nhận đủ ngữ cảnh: tên change, đường dẫn
  `team-ai-knowledge/openspec/changes/<change>/`, đường dẫn repo code, và định dạng đầu ra của persona.
- Sub-agent **chỉ đọc**. Agent chính gộp kết quả và là người duy nhất ghi file qua `sep_luu_artifact`.
- IDE không có sub-agent (Antigravity…) → tự đóng vai lần lượt từng persona, độc lập với phần mình đã viết.
- Giới hạn: tối đa 4 sub-agent một lượt, tối đa 3 vòng review–sửa. Sub-agent lặp lại cùng nội dung hoặc
  vượt thời gian → dừng nó, ghi nhận phần đã có, báo user.
- Không tin mù quáng kết quả sub-agent: vấn đề HIGH phải kiểm lại trong specs/code trước khi sửa.
