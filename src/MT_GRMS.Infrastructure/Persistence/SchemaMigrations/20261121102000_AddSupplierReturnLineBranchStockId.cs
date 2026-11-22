using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261121102000_AddSupplierReturnLineBranchStockId")]
public partial class AddSupplierReturnLineBranchStockId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("ALTER TABLE \"SupplierReturnLines\" ADD COLUMN IF NOT EXISTS \"BranchStockId\" uuid NULL;");

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("ALTER TABLE \"SupplierReturnLines\" DROP COLUMN IF EXISTS \"BranchStockId\";");
}
