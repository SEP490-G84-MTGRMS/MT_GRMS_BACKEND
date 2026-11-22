using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261121101000_AddTransferDetailBranchStockId")]
public partial class AddTransferDetailBranchStockId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("ALTER TABLE \"TransferDetails\" ADD COLUMN IF NOT EXISTS \"BranchStockId\" uuid NULL;");

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("ALTER TABLE \"TransferDetails\" DROP COLUMN IF EXISTS \"BranchStockId\";");
}
