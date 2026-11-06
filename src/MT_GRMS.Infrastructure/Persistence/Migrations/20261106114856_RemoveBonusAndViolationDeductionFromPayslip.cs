using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBonusAndViolationDeductionFromPayslip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bonus",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "ViolationDeduction",
                table: "Payslips");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Bonus",
                table: "Payslips",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0.00m);

            migrationBuilder.AddColumn<decimal>(
                name: "ViolationDeduction",
                table: "Payslips",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0.00m);
        }
    }
}
