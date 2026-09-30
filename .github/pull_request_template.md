## 📝 Mô Tả Pull Request (Backend)

- **Mã tính năng / Bug**: `FE-xx` hoặc `BUG-xx`
- **Người thực hiện**: @author
- **Tệp Worklog nội bộ đính kèm**: [`docs/worklogs/YYYY-MM-DD_...md`](./docs/worklogs/)

---

### 🔍 Tóm Tắt Thay Đổi
1. ...
2. ...

---

### 🧪 Checklist Tự Kiểm Tra (Self-Verification)
- [ ] Đã tạo file worklog markdown trong thư mục `docs/worklogs/`.
- [ ] Chạy `dotnet build MT_GRMS_BACKEND.sln`: Thành công (0 lỗi, 0 cảnh báo).
- [ ] Chạy `dotnet test MT_GRMS_BACKEND.sln`: Đạt 100%.
- [ ] Tuân thủ Clean Architecture: Controller không được trỏ vào Domain Entity; chỉ dùng DTO.
- [ ] Không có merge conflict với nhánh `develop`.

---

### 📌 Ghi Chú Cho PM Khi Review
- ...
