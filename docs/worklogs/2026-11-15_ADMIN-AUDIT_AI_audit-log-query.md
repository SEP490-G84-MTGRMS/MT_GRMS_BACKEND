# Nhật ký kiểm toán Admin

## Mục tiêu

Thay dữ liệu audit giả trên giao diện bằng truy vấn bảng AuditLogs hiện có.

## Thay đổi

- Thêm AdminAuditLogsController với GET /api/admin/audit-logs và phân quyền RoleConstants.AdminOnly.
- Truy vấn chỉ đọc, bỏ bản ghi soft-delete, sắp xếp thời gian và ID giảm dần.
- Kiểm tra tham số, giới hạn trang tối đa 100 bản ghi, lọc hành động và tìm người thực hiện, mã đối tượng, IP.
- Lọc hôm nay, 7 ngày và 30 ngày theo lịch Việt Nam UTC+7.
- Che thuộc tính JSON có tên chứa password, token, secret, otp, cookie hoặc authorization trước khi trả về.
- Giao diện chỉ hiển thị dữ liệu đã ghi nhận, xuất CSV trang đang xem và hiển thị lỗi thật khi API thất bại.
- Bỏ phần giám sát/kích hoạt cron giả lập khỏi trang audit. Database hiện chưa lưu mức độ rủi ro nên giao diện dùng các thuộc tính audit đã có.

## Xác minh

- dotnet build src/MT_GRMS.Api/MT_GRMS.Api.csproj --no-restore -m:1 -p:UseAppHost=false --output src/MT_GRMS.Api/bin/AdminAuditVerify -v:quiet thành công, 0 lỗi và 22 cảnh báo ở các file khác.
- Frontend npm run build thành công.
- Lần npx tsc -b cuối sau đó phát hiện thay đổi đồng thời ở AdminSidebar.tsx (Package không dùng) và MasterCatalogPage.tsx (sai contract AdminToast/ToastState). Vì vậy frontend toàn workspace hiện chưa đạt TypeScript, các lỗi được báo nằm ngoài các file của tác vụ audit.
- AuditLogPage.test.tsx có 3 kiểm thử đạt về dữ liệu API, bộ lọc và lỗi/thử lại.
- Chưa kiểm tra trực tiếp với tài khoản Admin trong trình duyệt hoặc dữ liệu database chạy thật. Cần khởi động lại backend để nạp endpoint mới.
