using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFieldBranchAttendancePolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableOtAllActualTime",
                table: "BranchAttendancePolicies");

            migrationBuilder.DropColumn(
                name: "RecordLateEarlyIfHalfDay",
                table: "BranchAttendancePolicies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableOtAllActualTime",
                table: "BranchAttendancePolicies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RecordLateEarlyIfHalfDay",
                table: "BranchAttendancePolicies",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
