using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnitCodeFromProductUnit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductUnits_UnitCode",
                table: "ProductUnits");

            migrationBuilder.DropColumn(
                name: "UnitCode",
                table: "ProductUnits");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UnitCode",
                table: "ProductUnits",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnits_UnitCode",
                table: "ProductUnits",
                column: "UnitCode");
        }
    }
}
