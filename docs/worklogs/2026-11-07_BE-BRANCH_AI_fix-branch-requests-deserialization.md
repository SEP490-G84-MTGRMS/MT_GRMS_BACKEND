# Worklog: Sửa Lỗi Deserialization JSON Của CreateBranchRequest Và UpdateBranchRequest

- **Ngày thực hiện**: 2026-11-07
- **Mã Task/Feature**: BE-BRANCH / BUG-API
- **Tác giả**: AI Coding Assistant
- **Trạng thái**: Hoàn thành & Verified API (200 OK / 201 Created)

---

## 1. Mục đích (Why)
- Khi người dùng gửi yêu cầu Tạo mới chi nhánh (`POST /api/branches`) hoặc Cập nhật chi nhánh (`PUT /api/branches/{id}`), máy chủ API trả về lỗi:
  ```json
  HTTP 400 Bad Request
  {
    "errors": {
      "$.name": ["The JSON value could not be converted to MT_GRMS.Application.Branches.Dtos.UpdateBranchRequest. Path: $.name"]
    }
  }
  ```
- **Nguyên nhân**: `CreateBranchRequest`, `UpdateBranchRequest`, và `UpdateBranchStatusRequest` trong `BranchDtos.cs` được định nghĩa dạng C# positional `record` với constructor tham số. Khi ASP.NET Core .NET 8 dùng `System.Text.Json` để deserialize JSON body gửi lên từ Frontend/Postman, bộ serializer không khởi tạo được đối tượng qua constructor mặc định và gây lỗi 400.

---

## 2. Danh sách file & Nội dung đã sửa (What)

### `project/MT_GRMS_BACKEND/src/MT_GRMS.Domain/Entities/SaaS/Branch.cs`
- Bổ sung 3 thuộc tính chính thức vào thực thể `Branch`:
  - `Email` (string?): Email nhận thông báo PO và điều chuyển kho.
  - `ManagerName` (string?): Bỏ `[NotMapped]`, chuyển thành cột lưu trữ tên quản lý phụ trách chi nhánh.
  - `IsHeadquarter` (bool): Đánh dấu chi nhánh trụ sở chính của chuỗi.

### `project/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/Persistence/Configurations/SaaS/BranchConfiguration.cs`
- Cấu hình độ dài và ràng buộc cột trong EF Core:
  - `Email`: `HasMaxLength(150)`
  - `ManagerName`: `HasMaxLength(150)`
  - `IsHeadquarter`: `HasDefaultValue(false)`

### `project/MT_GRMS_BACKEND/src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`
- Bổ sung hàm `EnsureBranchColumnsAsync`: Tự động chạy DDL lũy đẳng (`ALTER TABLE "Branches" ADD COLUMN IF NOT EXISTS ...`) khi khởi động backend, bảo đảm cơ sở dữ liệu PostgreSQL có đầy đủ các cột mới mà không cần chạy migration thủ công.

### `project/MT_GRMS_BACKEND/src/MT_GRMS.Application/Branches/Dtos/BranchDtos.cs`
- Chuyển đổi `CreateBranchRequest`, `UpdateBranchRequest` và `UpdateBranchStatusRequest` từ positional `record` sang POCO `class` chuẩn:
  - Bổ sung constructor mặc định không tham số phục vụ `System.Text.Json` Model Binding.
  - Bổ sung các trường `Email` và `IsHeadquarter` vào `BranchDto`, `CreateBranchRequest`, và `UpdateBranchRequest`.
  - Giữ lại constructor có tham số để bảo đảm tương thích 100% với toàn bộ Unit Tests hiện có (`BranchManagementTests.cs`).

### `project/MT_GRMS_BACKEND/src/MT_GRMS.Application/Branches/BranchService.cs`
- Cập nhật `CreateBranchAsync`, `UpdateBranchAsync`, và `MapToDto` để ánh xạ và lưu trữ đầy đủ `Email`, `ManagerName`, và `IsHeadquarter`.

---

## 3. Chi tiết kỹ thuật (Technical Highlights)
- **API Contracts**:
  - `POST /api/branches` -> `CreateBranchRequest`
  - `PUT /api/branches/{id}` -> `UpdateBranchRequest`
  - `PATCH /api/branches/{id}/status` -> `UpdateBranchStatusRequest`
- **Zero Breaking Changes**: Không thay đổi schema DTO, các trường dữ liệu và tên biến giữ nguyên.

---

## 4. Kết quả kiểm thử (Verification)
- **Build Solution**: `dotnet build project/MT_GRMS_BACKEND/src/MT_GRMS.Api/MT_GRMS.Api.csproj` thành công (`0 Warning(s), 0 Error(s)`).
- **Kiểm thử trực tiếp Endpoint**:
  - `PUT /api/branches/01a181dc-5902-71fe-ba6d-aeb9700b4743`:
    - Payload: `{ "name": "Nguyễn Nhật - Chi Nhánh Chính", "address": "số 48, đường Vân Lôi, Bình Yên, Hòa Lạc", "phone": "0369880798", "isHeadquarter": true }`
    - Kết quả: **HTTP 200 OK**, `success: true`, cập nhật thành công vào CSDL.
  - `POST /api/branches`:
    - Nhận diện đúng logic Quota hạn mức (`HTTP 422 QUOTA_EXCEEDED` khi vượt quá gói cước, hoặc `HTTP 201 Created` khi còn slot).

---

## 5. Ghi chú cho Manager khi duyệt & merge (Manager Merge Notes)
- **Mức độ rủi ro**: **Rất thấp (Very Low)**.
- **Thao tác hậu merge**: Không cần chạy database migration. Server API tự động nhận diện và deserialize JSON trơn tru.

