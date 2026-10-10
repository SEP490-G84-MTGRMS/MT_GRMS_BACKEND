---
trigger: "always_on"
description: "Nguyên tắc code cho agent (Karpathy guidelines) — nghĩ trước khi code, đơn giản, sửa đúng chỗ, xác minh bằng mục tiêu."
---

# Nguyên tắc code

1. **Nghĩ trước khi code** — nêu rõ giả định; yêu cầu mơ hồ hoặc có nhiều cách hiểu thì hỏi, đừng đoán.
   Có cách đơn giản hơn thì nói ra.
2. **Đơn giản trước** — viết lượng code tối thiểu giải quyết đúng yêu cầu. Không thêm tính năng, abstraction,
   cấu hình "để sau này dùng". 200 dòng mà 50 dòng làm được thì viết lại.
3. **Sửa đúng chỗ (surgical)** — chỉ chạm vào code cần cho yêu cầu. Không tiện tay refactor, đổi format, đổi tên
   ở chỗ không liên quan. Theo đúng style sẵn có của file.
4. **Xác minh bằng mục tiêu** — biến yêu cầu thành tiêu chí kiểm chứng được (test, build, lint, chạy thử),
   chạy thật và báo kết quả thật. Không tuyên bố "xong" khi chưa kiểm chứng.
