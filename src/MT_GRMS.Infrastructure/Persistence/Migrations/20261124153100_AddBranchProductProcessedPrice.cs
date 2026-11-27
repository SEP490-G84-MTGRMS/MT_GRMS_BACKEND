using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MT_GRMS.Infrastructure.Persistence;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// Thêm cột ProcessedSellingPricePerKg trên BranchProducts để giá đóng khay (đ/kg)
/// độc lập theo từng chi nhánh, không ghi đè Master Product toàn chuỗi (BP-04). Idempotent.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261124153100_AddBranchProductProcessedPrice")]
public partial class AddBranchProductProcessedPrice : Migration
{
    internal const string UpSql = """
        ALTER TABLE IF EXISTS "BranchProducts"
          ADD COLUMN IF NOT EXISTS "ProcessedSellingPricePerKg" numeric(18,2);
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE IF EXISTS "BranchProducts"
          DROP COLUMN IF EXISTS "ProcessedSellingPricePerKg";
        """);
}
