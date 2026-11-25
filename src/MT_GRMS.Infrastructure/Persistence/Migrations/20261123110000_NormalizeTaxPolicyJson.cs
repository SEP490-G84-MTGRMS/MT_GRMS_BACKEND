using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261123110000_NormalizeTaxPolicyJson")]
public class NormalizeTaxPolicyJson : Migration
{
    public const string UpSql = """
        DO $$
        BEGIN
            IF to_regclass('public."HouseholdTaxSnapshots"') IS NOT NULL THEN
                ALTER TABLE public."HouseholdTaxSnapshots" ADD COLUMN IF NOT EXISTS "TaxPolicyJson" jsonb;
                UPDATE public."HouseholdTaxSnapshots" SET "TaxPolicyJson" = '{}'::jsonb WHERE "TaxPolicyJson" IS NULL;
                ALTER TABLE public."HouseholdTaxSnapshots" ALTER COLUMN "TaxPolicyJson" SET DEFAULT '{}'::jsonb;
                ALTER TABLE public."HouseholdTaxSnapshots" ALTER COLUMN "TaxPolicyJson" SET NOT NULL;
            END IF;
        END $$;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public."HouseholdTaxSnapshots" ALTER COLUMN "TaxPolicyJson" DROP DEFAULT;
        """);
}
