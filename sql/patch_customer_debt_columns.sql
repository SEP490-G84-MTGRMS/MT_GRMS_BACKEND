-- Đồng bộ các cột công nợ cho bảng Customers nếu chưa tồn tại
ALTER TABLE "Customers" ADD COLUMN IF NOT EXISTS "LastRepaymentAtUtc" timestamp with time zone;
ALTER TABLE "Customers" ADD COLUMN IF NOT EXISTS "Notes" text;
ALTER TABLE "Customers" ADD COLUMN IF NOT EXISTS "RowVersion" bytea;

