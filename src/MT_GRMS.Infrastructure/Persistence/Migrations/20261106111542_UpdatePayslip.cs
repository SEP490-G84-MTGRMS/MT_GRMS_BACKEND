using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePayslip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TravelAllowance",
                table: "Payslips",
                newName: "TotalAllowance");

            migrationBuilder.RenameColumn(
                name: "LunchAllowance",
                table: "Payslips",
                newName: "RemainingDebt");

            migrationBuilder.RenameColumn(
                name: "ContractBaseSalary",
                table: "Payslips",
                newName: "BaseRate");

            migrationBuilder.AddColumn<string>(
                name: "AllowancesDetailJson",
                table: "Payslips",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "Payslips",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "Payslips",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountHolder",
                table: "Payslips",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumber",
                table: "Payslips",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "Payslips",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchID",
                table: "Payslips",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CashbookTransactionId",
                table: "Payslips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CashbookVoucherCode",
                table: "Payslips",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                table: "Payslips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "Payslips",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaidBy",
                table: "Payslips",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidLeaveDays",
                table: "Payslips",
                type: "numeric(5,2)",
                nullable: false,
                defaultValue: 0.00m);

            migrationBuilder.AddColumn<string>(
                name: "SalaryType",
                table: "Payslips",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "MONTHLY");

            migrationBuilder.AddColumn<decimal>(
                name: "StandardDayHours",
                table: "Payslips",
                type: "numeric(5,2)",
                nullable: false,
                defaultValue: 8.00m);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Payslips",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TotalEarlyLeaveMinutes",
                table: "Payslips",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalLateMinutes",
                table: "Payslips",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalShifts",
                table: "Payslips",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalWorkHours",
                table: "Payslips",
                type: "numeric(7,2)",
                nullable: false,
                defaultValue: 0.00m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnpaidLeaveDays",
                table: "Payslips",
                type: "numeric(5,2)",
                nullable: false,
                defaultValue: 0.00m);

            migrationBuilder.CreateTable(
                name: "CashbookTransaction",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionCode = table.Column<string>(type: "text", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    ReferenceCode = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashbookTransaction", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_BranchID",
                table: "Payslips",
                column: "BranchID");

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_CashbookTransactionId",
                table: "Payslips",
                column: "CashbookTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_TenantId_BranchID_PeriodMonth",
                table: "Payslips",
                columns: new[] { "TenantId", "BranchID", "PeriodMonth" });

            migrationBuilder.AddForeignKey(
                name: "FK_Payslips_Branches_BranchID",
                table: "Payslips",
                column: "BranchID",
                principalTable: "Branches",
                principalColumn: "BranchID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payslips_CashbookTransaction_CashbookTransactionId",
                table: "Payslips",
                column: "CashbookTransactionId",
                principalTable: "CashbookTransaction",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payslips_Branches_BranchID",
                table: "Payslips");

            migrationBuilder.DropForeignKey(
                name: "FK_Payslips_CashbookTransaction_CashbookTransactionId",
                table: "Payslips");

            migrationBuilder.DropTable(
                name: "CashbookTransaction");

            migrationBuilder.DropIndex(
                name: "IX_Payslips_BranchID",
                table: "Payslips");

            migrationBuilder.DropIndex(
                name: "IX_Payslips_CashbookTransactionId",
                table: "Payslips");

            migrationBuilder.DropIndex(
                name: "IX_Payslips_TenantId_BranchID_PeriodMonth",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "AllowancesDetailJson",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "BankAccountHolder",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "BranchID",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "CashbookTransactionId",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "CashbookVoucherCode",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "PaidBy",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "PaidLeaveDays",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "SalaryType",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "StandardDayHours",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "TotalEarlyLeaveMinutes",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "TotalLateMinutes",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "TotalShifts",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "TotalWorkHours",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "UnpaidLeaveDays",
                table: "Payslips");

            migrationBuilder.RenameColumn(
                name: "TotalAllowance",
                table: "Payslips",
                newName: "TravelAllowance");

            migrationBuilder.RenameColumn(
                name: "RemainingDebt",
                table: "Payslips",
                newName: "LunchAllowance");

            migrationBuilder.RenameColumn(
                name: "BaseRate",
                table: "Payslips",
                newName: "ContractBaseSalary");
        }
    }
}
