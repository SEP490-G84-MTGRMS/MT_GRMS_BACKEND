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
- **Request Body (DTO)**:
  ```json
  {}
  ```
- **Response Data (DTO)**:
  ```json
  {}
  ```

### 4. Kiểm Thử & Xác Nhận (Verification)
- [ ] Đã chạy `dotnet build MT_GRMS_BACKEND.sln`: Thành công (0 lỗi, 0 cảnh báo).
- [ ] Đã chạy `dotnet test MT_GRMS_BACKEND.sln`: Đạt 100%.
- [ ] Đã kiểm tra thử nghiệm qua Swagger UI.

### 5. Ghi Chú Cho PM & Frontend
- [Frontend cần lưu ý endpoint nào, kiểu dữ liệu nào đặc biệt]
- [Breaking changes hoặc dependencies cần cài thêm]
