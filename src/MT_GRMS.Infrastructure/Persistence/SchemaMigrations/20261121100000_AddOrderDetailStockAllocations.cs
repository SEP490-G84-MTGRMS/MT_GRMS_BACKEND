using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261121100000_AddOrderDetailStockAllocations")]
public partial class AddOrderDetailStockAllocations : Migration
{
    internal const string UpSql = "ALTER TABLE \"OrderDetails\" ADD COLUMN IF NOT EXISTS \"StockAllocationsJson\" text NULL;";

    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql(UpSql);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("ALTER TABLE \"OrderDetails\" DROP COLUMN IF EXISTS \"StockAllocationsJson\";");
}
