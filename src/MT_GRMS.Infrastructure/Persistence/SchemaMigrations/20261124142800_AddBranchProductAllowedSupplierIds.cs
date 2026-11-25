using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// Thêm cột AllowedSupplierIdsJson (text) trên BranchProducts để lưu danh sách NCC được gán tường minh
/// theo từng cặp (Chi nhánh, Sản phẩm). Idempotent.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261124142800_AddBranchProductAllowedSupplierIds")]
public partial class AddBranchProductAllowedSupplierIds : Migration
{
    internal const string UpSql = """
        ALTER TABLE IF EXISTS "BranchProducts"
          ADD COLUMN IF NOT EXISTS "AllowedSupplierIdsJson" text;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE IF EXISTS "BranchProducts"
          DROP COLUMN IF EXISTS "AllowedSupplierIdsJson";
        """);
}
