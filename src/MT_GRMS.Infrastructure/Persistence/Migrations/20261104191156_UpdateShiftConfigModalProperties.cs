using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateShiftConfigModalProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "DurationHours",
                table: "ShiftConfigs",
                type: "numeric(4,2)",
                nullable: false,
                defaultValue: 4.00m,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,2)",
                oldDefaultValue: 8.00m);

            migrationBuilder.AddColumn<string>(
                name: "BranchIds",
                table: "ShiftConfigs",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCheckInNextDay",
                table: "ShiftConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BranchIds",
                table: "ShiftConfigs");

            migrationBuilder.DropColumn(
                name: "IsCheckInNextDay",
                table: "ShiftConfigs");

            migrationBuilder.AlterColumn<decimal>(
                name: "DurationHours",
                table: "ShiftConfigs",
                type: "numeric(4,2)",
                nullable: false,
                defaultValue: 8.00m,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,2)",
                oldDefaultValue: 4.00m);
        }
    }
}
