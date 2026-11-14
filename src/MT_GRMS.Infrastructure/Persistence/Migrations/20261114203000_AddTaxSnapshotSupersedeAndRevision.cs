using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AppDbContext))]
    [Migration("20261114203000_AddTaxSnapshotSupersedeAndRevision")]
    public partial class AddTaxSnapshotSupersedeAndRevision : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "HouseholdTaxSnapshots",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "RevisionNumber",
                table: "HouseholdTaxSnapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersededSnapshotId",
                table: "HouseholdTaxSnapshots",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RectificationReason",
                table: "HouseholdTaxSnapshots",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_HouseholdTaxSnapshots_HouseholdTaxSnapshots_SupersededSnapshotId",
                table: "HouseholdTaxSnapshots",
                column: "SupersededSnapshotId",
                principalTable: "HouseholdTaxSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(@"
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_indexes WHERE tablename = 'HouseholdTaxSnapshots' AND indexname LIKE 'IX_HouseholdTaxSnapshots_TenantId_BranchId_PeriodType_Year%') THEN
        EXECUTE (SELECT 'DROP INDEX ' || quote_ident(indexname) FROM pg_indexes WHERE tablename = 'HouseholdTaxSnapshots' AND indexname LIKE 'IX_HouseholdTaxSnapshots_TenantId_BranchId_PeriodType_Year%' LIMIT 1);
    END IF;
END $$;
");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdTaxSnapshots_Tenant_Period_Revision",
                table: "HouseholdTaxSnapshots",
                columns: new[] { "TenantId", "BranchId", "PeriodType", "Year", "PeriodNumber", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdTaxSnapshots_Tenant_Period_Active",
                table: "HouseholdTaxSnapshots",
                columns: new[] { "TenantId", "BranchId", "PeriodType", "Year", "PeriodNumber" },
                unique: true,
                filter: "\"Status\" = 1");

            // PostgreSQL null-safe expression indexes: drop old single-period index and add revision + active finalized indexes
            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS ""UX_HouseholdTaxSnapshots_Tenant_Period_NullSafe"";

CREATE UNIQUE INDEX ""UX_HouseholdTaxSnapshots_ActiveFinalized_NullSafe""
ON ""HouseholdTaxSnapshots"" (
    ""TenantId"",
    COALESCE(""BranchId"", '00000000-0000-0000-0000-000000000000'::uuid),
    ""PeriodType"",
    ""Year"",
    ""PeriodNumber"")
WHERE ""Status"" = 1;

CREATE UNIQUE INDEX ""UX_HouseholdTaxSnapshots_Revision_NullSafe""
ON ""HouseholdTaxSnapshots"" (
    ""TenantId"",
    COALESCE(""BranchId"", '00000000-0000-0000-0000-000000000000'::uuid),
    ""PeriodType"",
    ""Year"",
    ""PeriodNumber"",
    ""RevisionNumber"");
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS ""UX_HouseholdTaxSnapshots_ActiveFinalized_NullSafe"";
DROP INDEX IF EXISTS ""UX_HouseholdTaxSnapshots_Revision_NullSafe"";

CREATE UNIQUE INDEX ""UX_HouseholdTaxSnapshots_Tenant_Period_NullSafe""
ON ""HouseholdTaxSnapshots"" (
    ""TenantId"",
    COALESCE(""BranchId"", '00000000-0000-0000-0000-000000000000'::uuid),
    ""PeriodType"",
    ""Year"",
    ""PeriodNumber"");
");

            migrationBuilder.DropIndex(
                name: "IX_HouseholdTaxSnapshots_Tenant_Period_Active",
                table: "HouseholdTaxSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_HouseholdTaxSnapshots_Tenant_Period_Revision",
                table: "HouseholdTaxSnapshots");

            migrationBuilder.DropForeignKey(
                name: "FK_HouseholdTaxSnapshots_HouseholdTaxSnapshots_SupersededSnapshotId",
                table: "HouseholdTaxSnapshots");

            migrationBuilder.DropColumn(
                name: "RectificationReason",
                table: "HouseholdTaxSnapshots");

            migrationBuilder.DropColumn(
                name: "SupersededSnapshotId",
                table: "HouseholdTaxSnapshots");

            migrationBuilder.DropColumn(
                name: "RevisionNumber",
                table: "HouseholdTaxSnapshots");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "HouseholdTaxSnapshots");
        }
    }
}
