# Worklog: Sửa lỗi mtgrms-pgweb CrashLoop không truy cập được cổng 8081

- **Ngày thực hiện**: 2026-11-04
- **Tác giả**: AI Assistant (Pair Programming)
- **Mã Task**: BE-DOCKER (Fix Docker Network & PGWeb CrashLoop)
- **Dự án**: `project/MT_GRMS_BACKEND`

---

## 1. Mục Đích Thay Đổi (Why)
- **Vấn đề**: Người dùng thường xuyên gặp lỗi không vào được giao diện quản lý database `pgweb` tại cổng `http://localhost:8081`. Trên giao diện Docker Desktop, container `mtgrms-pgweb` liên tục hiển thị trạng thái xoay vòng hoặc khởi động lại (`Restarting (1)`).
- **Nguyên nhân gốc rễ (Root Cause)**:
  1. Khi Docker Desktop khởi động lại hoặc khi bấm nút Start từng container lẻ trên giao diện Docker Desktop, `mtgrms-postgres` bị mất kết nối khỏi Docker network (`Networks: {}`).
  2. Container `mtgrms-pgweb` khi start sử dụng DNS nội bộ Docker (`127.0.0.11`) để tìm máy chủ `postgres`, do `mtgrms-postgres` không nằm cùng bridge network nên DNS trả về lỗi `Error: dial tcp: lookup postgres on 127.0.0.11:53: no such host`.
  3. Ứng dụng Go của `pgweb` khi gặp lỗi dial lúc start sẽ thoát (exit 1). Docker có cờ `restart: unless-stopped` lại tự động restart nó sau 10–30 giây, dẫn đến vòng lặp vô tận (CrashLoop) khiến cổng 8081 không thể mở.
  4. Cấu hình cũ dùng biến môi trường cũ `DATABASE_URL` (bị deprecated) thay vì `PGWEB_DATABASE_URL`.

---

## 2. Danh Sách Tệp & Chi Tiết Thay Đổi (What)
- [`docker-compose.yml`](../../docker-compose.yml):
  - Khai báo một bridge network chuyên dụng được đặt tên cố định: `mtgrms_network`.
  - Gắn cả hai dịch vụ `postgres` và `pgweb` vào cùng `mtgrms_network`, đảm bảo Docker luôn liên kết DNS `postgres` sang IP của container Postgres dù có restart lại bao nhiêu lần.
  - Cập nhật biến môi trường sang `PGWEB_DATABASE_URL` theo tiêu chuẩn mới của `sosedoff/pgweb:latest`.

---

## 3. Kết Quả Kiểm Thử (Verification)
- Chạy `docker compose up -d`:
  - Mạng `mtgrms_network` được khởi tạo.
  - Cả 2 container recreate và kết nối thông suốt:
    ```text
    CONTAINER ID   IMAGE                   STATUS                    PORTS
    ec22db416222   sosedoff/pgweb:latest   Up (healthy/running)      0.0.0.0:8081->8081/tcp
    6414af664d0b   postgres:16-alpine      Up (healthy)              0.0.0.0:5432->5432/tcp
    ```
- Log `mtgrms-pgweb`:
  ```text
  Connecting to server...
  Connected to PostgreSQL 16.14
  Checking database objects...
  Starting server...
  To view database open http://0.0.0.0:8081/ in browser
  ```
- Kiểm tra kết nối mạng:
  - Cổng PostgreSQL 5432: Đang mở (Open).
  - Cổng PGWeb 8081: Trả về HTTP 200 OK (`http://localhost:8081/`).

---

## 4. Ghi Chú Dành Cho Manager & Lập Trình Viên (Usage Notes)
- Để tránh lỗi khi mở máy tính hoặc khởi động Docker Desktop:
  - Nên chạy lệnh tại thư mục backend: `docker compose up -d`
  - Tránh bấm nút bật/tắt từng container lẻ trong UI Docker Desktop vì có thể khiến container bị văng khỏi network dùng chung.

