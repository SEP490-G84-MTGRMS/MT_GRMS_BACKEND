# Bộ tài khoản demo THViet

Chỉ dùng cho phát triển và demo. Nạp vào database backend bằng lệnh sau từ `src/MT_GRMS.Api`.

```powershell
dotnet run --launch-profile http -- --seed-demo-only true
```

Lệnh này chỉ nạp demo và thoát, không chạy đồng bộ schema. Database cần được khởi tạo bằng backend trước.
Khởi động backend bình thường trong Development cũng nạp bộ mẫu nếu `Database:SeedSampleData` không bị tắt.

Mật khẩu các chủ cửa hàng và nhân viên demo là `Demo@123456`. Admin dùng `admin@mtgrms.vn`; chủ cửa hàng dùng `<tên đăng nhập>@mtgrms.vn` (ví dụ `demo.standard@mtgrms.vn`). Nhân viên giữ miền `.example`. tài khoản demo đã xác minh email và tắt 2FA để đăng nhập không gửi thư ra ngoài.

| Điện thoại | Tên đăng nhập | Kịch bản | Chi nhánh | Nhân viên |
|---|---|---|---|---|
| 0910000001 | demo.new | Chưa mua gói, chưa sử dụng trial | 1 | 0 |
| 0910000002 | demo.trial | Trial đúng 3 ngày | 1 | 3 |
| 0910000003 | demo.standard | Standard, mua thêm quản lý nhân viên, 1 tháng | 1 | 3 |
| 0910000004 | demo.pro | Pro 3 tháng | 2 | 6 |
| 0910000005 | demo.pro-extra | Pro 12 tháng, mua thêm 1 chi nhánh | 3 | 9 |
| 0910000006 | demo.expiring | Standard còn 2 ngày | 1 | 3 |
| 0910000007 | demo.grace | Standard hết hạn 2 ngày, còn 5 ngày ân hạn | 1 | 3 |
| 0910000008 | demo.locked | Pro hết hạn 10 ngày, khóa mềm | 2 | 6 |

Ngày hết hạn được chốt lúc nạp đầu tiên, không tự gia hạn khi khởi động lại. Tài khoản đã trial hoặc trả phí có `TrialConsumedAt` để không nhận trial lần hai.

Admin được tái sử dụng nếu database đã có Admin, không đổi mật khẩu hoặc tạo thêm. Với database mới, tạo `admin` / `0901000000`, mật khẩu `Admin@123456`.

Nhân viên có tên đăng nhập `demo.<kịch bản>.<số chi nhánh>.<số nhân viên>`, ví dụ `demo.pro.1.1`. Nhân viên số 1 là quản lý chi nhánh, số 2 và 3 là nhân viên. Có hồ sơ và lịch ca ngày đầu tiên.

Mỗi cửa hàng đã có gói có 5 sản phẩm với đơn vị cơ bản, gán danh mục theo tenant và tồn kho từng chi nhánh, bao gồm hàng tồn thấp, hết hàng và cận hạn. Mỗi chi nhánh có 7 hóa đơn bán hàng trong 7 ngày, 2 PO chờ duyệt/chờ nhập và chi phí vận hành. Dữ liệu này là fixture demo, không giả lập toàn bộ quy trình chốt ca, sổ cái và nhập hàng.

Các cửa hàng trả phí có chuỗi giao dịch nạp ví → mua gói → mua thêm, số dư khớp nhau. Giá gói lấy từ bảng SubscriptionPlans. Các kịch bản demo dùng giá mua thêm mặc định 30.000 đ/tháng cho nhân viên hoặc 80.000 đ/tháng cho chi nhánh, tiền mua gói gồm VAT 10%.

Seed chạy trong một transaction, không ghi đè tenant, gói hoặc tài khoản hiện có. Nhóm dữ liệu có ID `thviet-demo-*`, không xóa dữ liệu demo cũ vì có thể đã được dùng tạo giao dịch thật. Khi chạy lại không tạo trùng hoặc reset trạng thái/mật khẩu.
