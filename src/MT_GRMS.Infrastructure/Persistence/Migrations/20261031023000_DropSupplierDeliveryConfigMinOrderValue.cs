using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations
{
    [Migration("20261031023000_DropSupplierDeliveryConfigMinOrderValue")]
    public partial class DropSupplierDeliveryConfigMinOrderValue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """ALTER TABLE "SupplierDeliveryConfigs" DROP COLUMN IF EXISTS "MinOrderValue";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MinOrderValue",
                table: "SupplierDeliveryConfigs",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
