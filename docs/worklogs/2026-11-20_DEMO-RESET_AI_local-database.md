# Reset database demo

`Program.cs` nhận lệnh reset riêng và chỉ cho phép chạy ở Development với hai cờ xác nhận. `DbInitializer.cs` giới hạn đích là PostgreSQL local, database `mtgrms_demo`, rồi dựng lại schema từ model EF Core và nạp bộ dữ liệu hiện có trong cùng một transaction. Luồng khởi động cho database hiện hữu giữ cơ chế đồng bộ cũ.

Tạo database trống `mtgrms_demo` và đặt `ConnectionStrings__DefaultConnection` trỏ đến database đó bằng tài khoản có quyền trên schema. Từ thư mục backend chạy:

```powershell
dotnet run --project src/MT_GRMS.Api --launch-profile http -- --reset-demo --confirm-reset
```

Lệnh xóa toàn bộ nội dung schema `public` của database demo và thoát sau khi nạp dữ liệu. Dùng cùng connection string khi chạy API bình thường. Đây là đường dựng schema demo từ model hiện tại, không phải baseline migration cho database triển khai.
