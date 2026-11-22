using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261122020000_AddBranchConversionRulesAndNotifications")]
public sealed class AddBranchConversionRulesAndNotifications : Migration
{
    internal const string UpSql = """
        ALTER TABLE "Branches" ADD COLUMN IF NOT EXISTS "ConversionRulesJson" text;

        CREATE TABLE IF NOT EXISTS "Notifications" (
            "Id" uuid PRIMARY KEY,
            "TenantId" varchar(50) NOT NULL,
            "RecipientUserId" varchar(64) NOT NULL,
            "Type" varchar(50) NOT NULL,
            "Title" varchar(200) NOT NULL,
            "Message" varchar(500) NOT NULL,
            "ActionUrl" varchar(300),
            "IsRead" boolean NOT NULL DEFAULT false,
            "ReadAtUtc" timestamp without time zone,
            "CreatedAt" timestamp without time zone NOT NULL,
            "UpdatedAt" timestamp without time zone,
            "CreatedBy" uuid,
            "IsDeleted" boolean NOT NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_Notifications_TenantId_RecipientUserId_IsRead_CreatedAt"
            ON "Notifications" ("TenantId", "RecipientUserId", "IsRead", "CreatedAt");
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    // Both objects may predate the migration and already hold application data.
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Rolling back this migration would remove branch rules and notification history.");
}
