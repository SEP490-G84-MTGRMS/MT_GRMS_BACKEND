using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateEmployeeSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "EmployeeSchedules",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "draft",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldDefaultValue: "scheduled");

            migrationBuilder.AlterColumn<decimal>(
                name: "DurationHours",
                table: "EmployeeSchedules",
                type: "numeric(4,2)",
                nullable: false,
                defaultValue: 4.00m,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,2)",
                oldDefaultValue: 8.00m);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchID",
                table: "EmployeeSchedules",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                table: "EmployeeSchedules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EarlyLeaveMinutes",
                table: "EmployeeSchedules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsCheckInNextDay",
                table: "EmployeeSchedules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OvertimeMinutes",
                table: "EmployeeSchedules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ShiftConfigID",
                table: "EmployeeSchedules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "EmployeeSchedules",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSchedules_BranchID",
                table: "EmployeeSchedules",
                column: "BranchID");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSchedules_ShiftConfigID",
                table: "EmployeeSchedules",
                column: "ShiftConfigID");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSchedules_TenantId_BranchID_WorkDate",
                table: "EmployeeSchedules",
                columns: new[] { "TenantId", "BranchID", "WorkDate" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSchedules_TenantId_EmployeeID_WorkDate",
                table: "EmployeeSchedules",
                columns: new[] { "TenantId", "EmployeeID", "WorkDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeSchedules_Branches_BranchID",
                table: "EmployeeSchedules",
                column: "BranchID",
                principalTable: "Branches",
                principalColumn: "BranchID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeSchedules_ShiftConfigs_ShiftConfigID",
                table: "EmployeeSchedules",
                column: "ShiftConfigID",
                principalTable: "ShiftConfigs",
                principalColumn: "ShiftConfigID",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeSchedules_Branches_BranchID",
                table: "EmployeeSchedules");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeSchedules_ShiftConfigs_ShiftConfigID",
                table: "EmployeeSchedules");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeSchedules_BranchID",
                table: "EmployeeSchedules");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeSchedules_ShiftConfigID",
                table: "EmployeeSchedules");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeSchedules_TenantId_BranchID_WorkDate",
                table: "EmployeeSchedules");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeSchedules_TenantId_EmployeeID_WorkDate",
                table: "EmployeeSchedules");

            migrationBuilder.DropColumn(
                name: "BranchID",
                table: "EmployeeSchedules");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "EmployeeSchedules");

            migrationBuilder.DropColumn(
                name: "EarlyLeaveMinutes",
                table: "EmployeeSchedules");

            migrationBuilder.DropColumn(
                name: "IsCheckInNextDay",
                table: "EmployeeSchedules");

            migrationBuilder.DropColumn(
                name: "OvertimeMinutes",
                table: "EmployeeSchedules");

            migrationBuilder.DropColumn(
                name: "ShiftConfigID",
                table: "EmployeeSchedules");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "EmployeeSchedules");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "EmployeeSchedules",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "scheduled",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldDefaultValue: "draft");

            migrationBuilder.AlterColumn<decimal>(
                name: "DurationHours",
                table: "EmployeeSchedules",
                type: "numeric(4,2)",
                nullable: false,
                defaultValue: 8.00m,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,2)",
                oldDefaultValue: 4.00m);
        }
    }
}
