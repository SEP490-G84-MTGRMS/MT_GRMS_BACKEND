using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MT_GRMS.Infrastructure.Persistence;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// Thêm cột ImageUrlsJson (text) vào bảng Products để lưu toàn bộ danh sách ảnh sản phẩm
/// thay vì chỉ lưu duy nhất ImageUrl (PROD-03). Idempotent.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261124153000_AddProductImageUrlsJson")]
public partial class AddProductImageUrlsJson : Migration
{
    internal const string UpSql = """
        ALTER TABLE IF EXISTS "Products"
          ADD COLUMN IF NOT EXISTS "ImageUrlsJson" text;

        UPDATE "Products"
        SET "ImageUrlsJson" = json_build_array("ImageUrl")::text
        WHERE "ImageUrlsJson" IS NULL
          AND "ImageUrl" IS NOT NULL
          AND btrim("ImageUrl") <> '';
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE IF EXISTS "Products"
          DROP COLUMN IF EXISTS "ImageUrlsJson";
        """);
}
