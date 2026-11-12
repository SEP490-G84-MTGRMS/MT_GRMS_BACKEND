using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchAttendancePolicyAndRemoveBonusViolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeAllowances_Employees_EmployeeID",
                table: "EmployeeAllowances");

            migrationBuilder.DropTable(
                name: "EmployeeBonusViolations");

            migrationBuilder.CreateTable(
                name: "BranchAttendancePolicies",
                columns: table => new
                {
                    BranchAttendancePolicyID = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchID = table.Column<Guid>(type: "uuid", nullable: false),
                    StandardDayHours = table.Column<decimal>(type: "numeric(4,2)", nullable: false, defaultValue: 8.00m),
                    IsConvertHoursToDays = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    ConversionRulesJson = table.Column<string>(type: "text", nullable: true),
                    RecordLateEarlyIfHalfDay = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    EnableLateThreshold = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    LateThresholdMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    EnableEarlyLeaveThreshold = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    EarlyLeaveThresholdMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    EnableOtBeforeShift = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    OtBeforeShiftMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    EnableOtAfterShift = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    OtAfterShiftMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    EnableOtAllActualTime = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantID = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchAttendancePolicies", x => x.BranchAttendancePolicyID);
                    table.ForeignKey(
                        name: "FK_BranchAttendancePolicies_Branches_BranchID",
                        column: x => x.BranchID,
                        principalTable: "Branches",
                        principalColumn: "BranchID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BranchAttendancePolicies_BranchID",
                table: "BranchAttendancePolicies",
                column: "BranchID");

            migrationBuilder.CreateIndex(
                name: "IX_BranchAttendancePolicies_TenantID_BranchID",
                table: "BranchAttendancePolicies",
                columns: new[] { "TenantID", "BranchID" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeAllowances_Employees_EmployeeID",
                table: "EmployeeAllowances",
                column: "EmployeeID",
                principalTable: "Employees",
                principalColumn: "EmployeeID",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeAllowances_Employees_EmployeeID",
                table: "EmployeeAllowances");

            migrationBuilder.DropTable(
                name: "BranchAttendancePolicies");

            migrationBuilder.CreateTable(
                name: "EmployeeBonusViolations",
                columns: table => new
                {
                    BonusViolationID = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeID = table.Column<Guid>(type: "uuid", nullable: false),
                    PayslipID = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0.00m),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    EvidenceUrl = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IncidentDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TenantID = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeBonusViolations", x => x.BonusViolationID);
                    table.ForeignKey(
                        name: "FK_EmployeeBonusViolations_Employees_EmployeeID",
                        column: x => x.EmployeeID,
                        principalTable: "Employees",
                        principalColumn: "EmployeeID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeBonusViolations_Payslips_PayslipID",
                        column: x => x.PayslipID,
                        principalTable: "Payslips",
                        principalColumn: "PayslipID",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeBonusViolations_EmployeeID",
                table: "EmployeeBonusViolations",
                column: "EmployeeID");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeBonusViolations_PayslipID",
                table: "EmployeeBonusViolations",
                column: "PayslipID");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeAllowances_Employees_EmployeeID",
                table: "EmployeeAllowances",
                column: "EmployeeID",
                principalTable: "Employees",
                principalColumn: "EmployeeID",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
