# Worklog: Cấu hình Render Cloud Deployment & Kích hoạt Swagger trên Cùng Cổng (Single Port)

- **Mã Task**: BE-DEPLOY
- **Tác giả**: AI Assistant
- **Ngày thực hiện**: 2026-11-05
- **Trạng thái**: Hoàn thành (Verified PASS)

---

## 1. Mục đích (Why)
- Người dùng thắc mắc về việc Render chỉ cấp phát duy nhất 1 cổng (thông qua biến môi trường `PORT`), và hỏi liệu có thể chạy Swagger cùng port với API không.
- Phát hiện và xử lý lỗi nghiêm trọng trong log Render: `42P01: relation "Users" does not exist` khi chạy `MigrateAsync()` trên cơ sở dữ liệu mới (Neon/Cloud).
- Phân tích nguyên nhân:
  1. Trong ASP.NET Core (.NET 8), Kestrel phục vụ cả API và Swagger trên **cùng một port duy nhất** (Swagger là HTTP middleware tại `/swagger`).
  2. `launchSettings.json` chỉ có hiệu lực ở máy dev (Visual Studio/IIS Express), không được sử dụng trong Docker/Cloud. Trên Render, container tự động lắng nghe ở cổng `8080` và Render đã kết nối thành công.
  3. Khi kết nối vào cơ sở dữ liệu Cloud mới tinh, lệnh `MigrateAsync()` cố chạy delta migration `AddTaxAndFinanceModules` (chứa `ALTER TABLE "Users"`). Vì database chưa có bảng `Users`, hệ thống ném ngoại lệ và dừng khởi tạo database.
  4. Cần cấu hình `DbInitializer` tự động nhận biết database mới và dùng `IRelationalDatabaseCreator.CreateTablesAsync()` sinh toàn bộ 34 bảng trực tiếp từ DbContext model, đồng thời đánh dấu `__EFMigrationsHistory` để tránh lỗi `ALTER TABLE`.

---

## 2. Danh sách tệp & Nội dung thay đổi (What)
- **`project/MT_GRMS_BACKEND/src/MT_GRMS.Api/Program.cs`**:
  - Tự động gán URL lắng nghe dựa trên biến `PORT`: `builder.WebHost.UseUrls($"http://+:{renderPort}");`.
  - Bật `app.UseSwagger()` và `app.UseSwaggerUI()` vô điều kiện (hỗ trợ cả Development lẫn Production trên Cloud).
  - Thêm endpoint chuyển hướng `app.MapGet("/", () => Results.Redirect("/swagger"));`.
  - Bỏ lệnh gọi trực tiếp `await dbContext.Database.MigrateAsync();` để tránh crash khi schema chưa sẵn sàng, ủy quyền toàn bộ cho `DbInitializer`.
- **`project/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`**:
  - Kiểm tra bảng `"Users"` trước khi chạy:
    - Nếu bảng `"Users"` chưa tồn tại (Database mới): Dùng `IRelationalDatabaseCreator.CreateTablesAsync()` / `EnsureCreatedAsync()` tạo đủ 34 bảng và ghi danh các migration vào `__EFMigrationsHistory`.
    - Nếu bảng `"Users"` đã tồn tại: Chạy `MigrateAsync()` an toàn.
- **`project/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/DependencyInjection.cs`**:
  - In thông tin kết nối CSDL động (Host, Port, Database name) từ chuỗi kết nối thay vì chuỗi hardcoded localhost.
- **`project/MT_GRMS_BACKEND/tests/MT_GRMS.UnitTests/Admin/TenantStoreControllerTests.cs`**:
  - Gán số dư ví khởi tạo cho `tenant-01` trong test `PurchaseSubscription_ValidRequest_ExtendsSubscriptionAndReturns200Ok`.

---

## 3. Chi tiết kỹ thuật (Technical Highlights)
- **Port Matching**: API (`/api/v1/...`) và Swagger (`/swagger`) cùng chia sẻ một process Kestrel và cùng port `$PORT`.
- **Render Compatibility**: Khớp 100% với cơ chế container hosting của Render (chỉ mở 1 HTTP port public).

---

## 4. Kết quả kiểm thử (Verification)
- `dotnet build`: Build thành công, 0 Error(s).
- `dotnet test`: Toàn bộ 407 Unit Tests và 43 Integration Tests đều PASS (Tổng cộng 450 tests PASS).

---

## 5. Hướng dẫn cho Manager khi merge & deploy (Manager Merge Notes)
- **Mức độ rủi ro**: Thấp (Low).
- **Cấu hình trên Render Dashboard**:
  - Environment Variables:
    - `ConnectionStrings__DefaultConnection`: Chuỗi kết nối PostgreSQL (Neon.tech).
    - `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`: Khóa bí mật JWT.
    - `Sepay__ApiToken`, `Sepay__AccountNumber`: Thông tin SePay.
- Sau khi deploy, truy cập `https://<ten-dich-vu>.onrender.com` sẽ tự động mở Swagger UI.

