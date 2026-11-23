using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration(MigrationId)]
public sealed class MergeBranchAttendancePoliciesIntoBranches : Migration
{
    internal const string MigrationId = "20261122030000_MergeBranchAttendancePoliciesIntoBranches";

    internal const string UpSql = """
        ALTER TABLE "Branches"
            ADD COLUMN IF NOT EXISTS "StandardDayHours" numeric(4,2) NOT NULL DEFAULT 8.00,
            ADD COLUMN IF NOT EXISTS "IsConvertHoursToDays" boolean NOT NULL DEFAULT true,
            ADD COLUMN IF NOT EXISTS "ConversionRulesJson" text,
            ADD COLUMN IF NOT EXISTS "EnableLateThreshold" boolean NOT NULL DEFAULT true,
            ADD COLUMN IF NOT EXISTS "LateThresholdMinutes" integer NOT NULL DEFAULT 0,
            ADD COLUMN IF NOT EXISTS "EnableEarlyLeaveThreshold" boolean NOT NULL DEFAULT true,
            ADD COLUMN IF NOT EXISTS "EarlyLeaveThresholdMinutes" integer NOT NULL DEFAULT 0,
            ADD COLUMN IF NOT EXISTS "IsAttendancePolicyCustomized" boolean NOT NULL DEFAULT false;

        DO $$
        BEGIN
            IF to_regclass('"BranchAttendancePolicies"') IS NULL THEN
                RETURN;
            END IF;

            -- Keep the source rows, including settings no longer represented in Branches.
            LOCK TABLE "BranchAttendancePolicies" IN SHARE MODE;

            IF EXISTS (
                SELECT 1 FROM "BranchAttendancePolicies" p WHERE NOT p."IsDeleted"
                GROUP BY p."TenantID", p."BranchID" HAVING count(*) > 1
            ) THEN
                RAISE EXCEPTION 'Multiple active attendance policies for one branch';
            END IF;

            IF EXISTS (
                SELECT 1 FROM "BranchAttendancePolicies" p
                LEFT JOIN "Branches" b ON b."BranchID" = p."BranchID" AND b."TenantID" = p."TenantID"
                WHERE NOT p."IsDeleted" AND (b."BranchID" IS NULL OR b."IsDeleted")
            ) THEN
                RAISE EXCEPTION 'An active attendance policy has no non-deleted branch in its tenant';
            END IF;

            IF EXISTS (
                SELECT 1 FROM "BranchAttendancePolicies" p
                JOIN "Branches" b ON b."BranchID" = p."BranchID" AND b."TenantID" = p."TenantID"
                WHERE NOT p."IsDeleted"
                  AND (b."IsAttendancePolicyCustomized"
                       OR (b."StandardDayHours", b."IsConvertHoursToDays", b."ConversionRulesJson",
                           b."EnableLateThreshold", b."LateThresholdMinutes", b."EnableEarlyLeaveThreshold",
                           b."EarlyLeaveThresholdMinutes")
                       IS DISTINCT FROM (8.00::numeric, true, NULL::text, true, 0, true, 0))
                  AND (b."StandardDayHours", b."IsConvertHoursToDays", b."ConversionRulesJson",
                       b."EnableLateThreshold", b."LateThresholdMinutes", b."EnableEarlyLeaveThreshold",
                       b."EarlyLeaveThresholdMinutes")
                  IS DISTINCT FROM (p."StandardDayHours", p."IsConvertHoursToDays", p."ConversionRulesJson",
                       p."EnableLateThreshold", p."LateThresholdMinutes", p."EnableEarlyLeaveThreshold",
                       p."EarlyLeaveThresholdMinutes")
            ) THEN
                RAISE EXCEPTION 'The branch and legacy attendance policy differ; reconcile before migration';
            END IF;

            UPDATE "Branches" b SET
                "StandardDayHours" = p."StandardDayHours",
                "IsConvertHoursToDays" = p."IsConvertHoursToDays",
                "ConversionRulesJson" = p."ConversionRulesJson",
                "EnableLateThreshold" = p."EnableLateThreshold",
                "LateThresholdMinutes" = p."LateThresholdMinutes",
                "EnableEarlyLeaveThreshold" = p."EnableEarlyLeaveThreshold",
                "EarlyLeaveThresholdMinutes" = p."EarlyLeaveThresholdMinutes",
                "IsAttendancePolicyCustomized" = true
            FROM "BranchAttendancePolicies" p
            WHERE NOT p."IsDeleted" AND b."BranchID" = p."BranchID" AND b."TenantID" = p."TenantID"
              AND NOT b."IsAttendancePolicyCustomized";
        END $$;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Restore branch attendance settings from a verified backup before rolling back.");
}
