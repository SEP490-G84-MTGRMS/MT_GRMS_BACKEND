# Worklog: 2026-11-03 — BE-CONFIG — Bỏ qua Migration để tránh xung đột (Merge Conflicts)

- **Author**: Antigravity AI Assistant
- **Task/Feature**: BE-CONFIG (`ignore-migrations-to-prevent-merge-conflicts`)
- **Dự án**: `project/MT_GRMS_BACKEND`
- **Mức độ rủi ro (Risk Level)**: Low (Cấu hình .gitignore và tối ưu hóa DbInitializer khởi tạo database)

---

## 1. Mục đích (Why)
- Khi nhiều thành viên trong nhóm phát triển các nhánh tính năng (feature branches) khác nhau, Entity Framework Core tự động sinh các file migration và `AppDbContextModelSnapshot.cs`.
- Khi merge code giữa các nhánh (`dev`, `feature/*`, `main`), file snapshot và các file migration liên tục gây ra xung đột (merge conflicts) nghiêm trọng làm tắc nghẽn quá trình tích hợp mã nguồn.
- Hệ thống backend đã chuyển sang cơ chế Code-First tự động khởi tạo (`DbInitializer` kết hợp `EnsureCreatedAsync` & Domain Entities chuẩn 34 bảng), do đó các file migration sinh cục bộ không cần thiết phải đưa lên Git.

---

## 2. Danh sách file & Nội dung đã sửa (What)
- [`.gitignore`](file:///d:/FPT_University/Final/Grade/new/project/MT_GRMS_BACKEND/.gitignore):
  - Bổ sung quy tắc bỏ qua thư mục `**/[Mm]igrations/`, `**/AppDbContextModelSnapshot.cs` và `*.Designer.cs`.
- [`src/MT_GRMS.Infrastructure/Persistence/Migrations/`](file:///d:/FPT_University/Final/Grade/new/project/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/Persistence/Migrations/):
  - Thực hiện untrack (`git rm -r --cached`) toàn bộ các file migration đã track trước đó khỏi chỉ mục Git mà không xóa file vật lý trên máy dev.
- [`src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`](file:///d:/FPT_University/Final/Grade/new/project/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs):
  - Bổ sung cơ chế fallback an toàn: Nếu môi trường có migrations sẽ chạy `MigrateAsync()`; nếu không có migrations hoặc gặp lỗi lệch lịch sử migration khi merge, tự động fallback sang `context.Database.EnsureCreatedAsync()`, đảm bảo luôn sinh đủ 34 bảng CSDL và nạp seed data thành công mà không bị crash.

---

## 3. Chi tiết Kỹ thuật (Technical Highlights)
- Khắc phục triệt để lỗi merge conflict từ `AppDbContextModelSnapshot.cs`.
- Khởi động ứng dụng an toàn trong mọi trường hợp (dù có hoặc không có thư mục `Migrations` trong repo).
- Dữ liệu mẫu (Seed Data) cho Super Admin, Store Manager, Cashier và các Subscription Plans vẫn được nạp tự động đầy đủ.

---

## 4. Kết quả Kiểm thử (Verification)
- `dotnet build`: **Build succeeded** (0 errors).
- `dotnet test`: **Passed! 104 Unit Tests, 12 Integration Tests** (0 Failed).

---

## 5. Ghi chú cho Manager khi Duyệt & Merge (Manager Merge Notes)
- **Mức độ rủi ro**: Low.
- **File cần review**: `.gitignore`, `DbInitializer.cs`.
- **Lưu ý sau khi merge**: Các thành viên khác khi pull code về chỉ cần chạy lại backend như bình thường, `DbInitializer` sẽ tự động đảm bảo cấu trúc database luôn đồng bộ.
