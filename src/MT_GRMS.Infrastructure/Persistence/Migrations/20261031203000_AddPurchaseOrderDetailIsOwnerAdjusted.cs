using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AppDbContext))]
    [Migration("20261031203000_AddPurchaseOrderDetailIsOwnerAdjusted")]
    public partial class AddPurchaseOrderDetailIsOwnerAdjusted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "PurchaseOrderDetails"
                ADD COLUMN IF NOT EXISTS "IsOwnerAdjusted" boolean NOT NULL DEFAULT FALSE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "PurchaseOrderDetails" DROP COLUMN IF EXISTS "IsOwnerAdjusted";
                """);
        }
    }
}
