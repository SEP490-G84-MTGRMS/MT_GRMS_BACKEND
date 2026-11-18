using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261117130000_RemoveUnusedCashTransit")]
public partial class RemoveUnusedCashTransit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS \"CashInTransits\";");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS "CashInTransits" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" text NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                "IsDeleted" boolean NOT NULL DEFAULT false,
                "CreatedBy" uuid NULL,
                "TransitCode" character varying(50) NOT NULL,
                "SourceAccountId" uuid NOT NULL,
                "SourceAccountType" integer NOT NULL,
                "TargetBankAccountId" uuid NOT NULL,
                "AmountSent" numeric(18,2) NOT NULL,
                "AmountReceived" numeric(18,2) NULL,
                "DiscrepancyAmount" numeric(18,2) NOT NULL DEFAULT 0,
                "CarrierUserId" uuid NOT NULL,
                "Status" integer NOT NULL,
                "Note" text NULL,
                "ConfirmedAtUtc" timestamp with time zone NULL,
                "ConfirmedByUserId" uuid NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_CashInTransits_TenantId_Status" ON "CashInTransits" ("TenantId", "Status");
        """);
    }
}
