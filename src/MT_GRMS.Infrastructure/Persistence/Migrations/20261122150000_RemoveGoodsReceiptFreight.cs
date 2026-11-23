using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261122150000_RemoveGoodsReceiptFreight")]
public partial class RemoveGoodsReceiptFreight : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "FreightFee",
            table: "GoodsReceipts");

        migrationBuilder.DropColumn(
            name: "FreightAllocated",
            table: "GoodsReceiptLines");

        migrationBuilder.RenameColumn(
            name: "LandedUnitCost",
            table: "GoodsReceiptLines",
            newName: "PurchaseUnitCost");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "PurchaseUnitCost",
            table: "GoodsReceiptLines",
            newName: "LandedUnitCost");

        migrationBuilder.AddColumn<decimal>(
            name: "FreightFee",
            table: "GoodsReceipts",
            type: "numeric(18,2)",
            nullable: false,
            defaultValue: 0.00m);

        migrationBuilder.AddColumn<decimal>(
            name: "FreightAllocated",
            table: "GoodsReceiptLines",
            type: "numeric(18,2)",
            nullable: false,
            defaultValue: 0.00m);
    }
}
