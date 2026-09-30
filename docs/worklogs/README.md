# THƯ MỤC QUẢN LÝ WORKLOG NỘI BỘ BACKEND

> **Quy định**: Mọi commit hoặc Pull Request trong repository này **BẮT BUỘC** phải có 1 file worklog `.md` lưu trong thư mục này.  
> **Quy tắc gốc**: Xem [`DOCS_MT_GRMS/rules/RULE_WORKLOG_AND_COMMIT.md`](../../../DOCS_MT_GRMS/rules/RULE_WORKLOG_AND_COMMIT.md)

---

## 📌 Cách Tạo File Worklog

1. Sao chép mẫu từ file [`WORKLOG_TEMPLATE.md`](./WORKLOG_TEMPLATE.md).
2. Đặt tên file theo cú pháp:
   ```text
   YYYY-MM-DD_FE-<ID>_<AUTHOR>_<MOTA_NGAN>.md
   ```
   *Ví dụ*:
   - `2026-10-01_FE-02_DUC_fefo-batch-engine.md`
   - `2026-10-02_FE-12_NHAT_pos-order-service.md`
   - `2026-10-03_BUG-01_TAM_fix-stock-onhand-query.md`
3. Điền đầy đủ: Mục đích, tệp đã sửa, API contract mới, kết quả test.
4. Commit file `.md` này cùng với các file code C# của bạn.
5. Sau khi PM merge PR, file này sẽ được PM dẫn link vào [`DOCS_MT_GRMS/PM_TRACKING_DASHBOARD.md`](../../../DOCS_MT_GRMS/PM_TRACKING_DASHBOARD.md).
