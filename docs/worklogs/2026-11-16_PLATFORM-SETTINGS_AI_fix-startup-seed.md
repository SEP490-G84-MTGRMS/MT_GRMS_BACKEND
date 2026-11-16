# Sửa khởi tạo PlatformSettings

- Nguyên nhân lỗi PostgreSQL 23502 là INSERT thủ công thiếu CreatedAt bắt buộc của BaseEntity. IsDeleted cũng cần được đồng bộ.
- Sửa DbInitializer.cs bổ sung hai cột nếu thiếu và truyền giá trị rõ ràng khi seed, bao gồm database đã có cột nhưng chưa có default.
- Giữ ON CONFLICT DO NOTHING để giữ cấu hình đang có khi khởi động lại.
- Xác minh dotnet build API với --no-restore, output riêng bin/PlatformSeedVerify thành công, 0 lỗi và 22 cảnh báo hiện có.
- Chưa chạy lại khởi tạo trên PostgreSQL thực tế. Cần khởi động lại backend để xác nhận log seed.
