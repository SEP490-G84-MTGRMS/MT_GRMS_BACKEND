using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxCategoryAndOrderItemVat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TaxCategory",
                table: "products",
                type: "text",
                nullable: false,
                defaultValue: "STANDARD_1PCT");

            migrationBuilder.AddColumn<decimal>(
                name: "AppliedVatRate",
                table: "OrderItem",
                type: "numeric",
                nullable: false,
                defaultValue: 0.01m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatAmount",
                table: "OrderItem",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatDiscountAmount",
                table: "OrderItem",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxCategory",
                table: "products");

            migrationBuilder.DropColumn(
                name: "AppliedVatRate",
                table: "OrderItem");

            migrationBuilder.DropColumn(
                name: "VatAmount",
                table: "OrderItem");

            migrationBuilder.DropColumn(
                name: "VatDiscountAmount",
                table: "OrderItem");
        }
    }
}
