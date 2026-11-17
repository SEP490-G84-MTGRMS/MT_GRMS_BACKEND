using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261114193000_AddTaxPaymentSnapshotReference")]
    public partial class AddTaxPaymentSnapshotReference : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "HouseholdTaxSnapshotId",
                table: "ExpenseVouchers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseVouchers_TenantId_HouseholdTaxSnapshotId",
                table: "ExpenseVouchers",
                columns: new[] { "TenantId", "HouseholdTaxSnapshotId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseVouchers_HouseholdTaxSnapshots_HouseholdTaxSnapshotId",
                table: "ExpenseVouchers",
                column: "HouseholdTaxSnapshotId",
                principalTable: "HouseholdTaxSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // PostgreSQL considers NULL values distinct in a normal unique index. This expression index
            // ensures tenant-wide snapshots (BranchId = NULL) also have one finalized winner per period.
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ""UX_HouseholdTaxSnapshots_Tenant_Period_NullSafe""
ON ""HouseholdTaxSnapshots"" (
    ""TenantId"",
    COALESCE(""BranchId"", '00000000-0000-0000-0000-000000000000'::uuid),
    ""PeriodType"",
    ""Year"",
    ""PeriodNumber"");");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseVouchers_HouseholdTaxSnapshots_HouseholdTaxSnapshotId",
                table: "ExpenseVouchers");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseVouchers_TenantId_HouseholdTaxSnapshotId",
                table: "ExpenseVouchers");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"UX_HouseholdTaxSnapshots_Tenant_Period_NullSafe\";");

            migrationBuilder.DropColumn(
                name: "HouseholdTaxSnapshotId",
                table: "ExpenseVouchers");
        }
    }
}