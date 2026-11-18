using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// One-way security migration from legacy tenant-role aliases to the canonical
/// STORE_OWNER/STAFF model. Platform ADMIN users must not have tenant membership.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261116230000_CanonicalizeTenantRoles")]
public partial class CanonicalizeTenantRoles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TEMP TABLE "_CanonicalRoleAffectedUsers" (
                "UserID" text PRIMARY KEY
            ) ON COMMIT DROP;

            INSERT INTO "_CanonicalRoleAffectedUsers" ("UserID")
            SELECT DISTINCT tu."UserID"
            FROM "TenantUsers" tu
            INNER JOIN "Users" u ON u."UserID" = tu."UserID"
            WHERE u."IsAdmin" = TRUE
               OR UPPER(TRIM(tu."Role")) NOT IN ('STORE_OWNER', 'STAFF')
            ON CONFLICT DO NOTHING;

            DO $migration$
            DECLARE invalid_roles text;
            BEGIN
                SELECT string_agg(DISTINCT tu."Role", ', ' ORDER BY tu."Role")
                INTO invalid_roles
                FROM "TenantUsers" tu
                INNER JOIN "Users" u ON u."UserID" = tu."UserID"
                WHERE u."IsAdmin" = FALSE
                  AND UPPER(TRIM(tu."Role")) NOT IN (
                      'STORE_OWNER', 'STAFF',
                      'OWNER', 'STOREOWNER', 'STORE OWNER',
                      'MANAGER', 'BRANCH_MANAGER', 'BRANCH MANAGER', 'CASHIER', 'EMPLOYEE'
                  );

                IF invalid_roles IS NOT NULL THEN
                    RAISE EXCEPTION 'RBAC migration stopped: unknown tenant roles: %', invalid_roles;
                END IF;
            END
            $migration$;

            UPDATE "TenantUsers"
            SET "Role" = CASE
                WHEN UPPER(TRIM("Role")) IN ('STORE_OWNER', 'OWNER', 'STOREOWNER', 'STORE OWNER')
                    THEN 'STORE_OWNER'
                WHEN UPPER(TRIM("Role")) IN ('STAFF', 'MANAGER', 'BRANCH_MANAGER', 'BRANCH MANAGER', 'CASHIER', 'EMPLOYEE')
                    THEN 'STAFF'
                ELSE "Role"
            END
            WHERE UPPER(TRIM("Role")) IN (
                'STORE_OWNER', 'OWNER', 'STOREOWNER', 'STORE OWNER',
                'STAFF', 'MANAGER', 'BRANCH_MANAGER', 'BRANCH MANAGER', 'CASHIER', 'EMPLOYEE'
            );

            DELETE FROM "TenantUsers" tu
            USING "Users" u
            WHERE u."UserID" = tu."UserID"
              AND u."IsAdmin" = TRUE;

            UPDATE "Users" u
            SET "SecurityStamp" = md5(random()::text || clock_timestamp()::text || u."UserID"),
                "RefreshToken" = NULL,
                "RefreshTokenExpiryTime" = NULL
            WHERE u."UserID" IN (SELECT "UserID" FROM "_CanonicalRoleAffectedUsers");

            DO $migration$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM "TenantUsers" tu
                    INNER JOIN "Users" u ON u."UserID" = tu."UserID"
                    WHERE u."IsAdmin" = TRUE
                ) THEN
                    RAISE EXCEPTION 'RBAC migration failed: ADMIN still has tenant membership';
                END IF;

                IF EXISTS (
                    SELECT 1 FROM "TenantUsers"
                    WHERE "Role" NOT IN ('STORE_OWNER', 'STAFF')
                ) THEN
                    RAISE EXCEPTION 'RBAC migration failed: non-canonical tenant role remains';
                END IF;
            END
            $migration$;

            ALTER TABLE "TenantUsers"
            DROP CONSTRAINT IF EXISTS "CK_TenantUsers_Role_Canonical";

            ALTER TABLE "TenantUsers"
            ADD CONSTRAINT "CK_TenantUsers_Role_Canonical"
            CHECK ("Role" IN ('STORE_OWNER', 'STAFF'));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "TenantUsers"
            DROP CONSTRAINT IF EXISTS "CK_TenantUsers_Role_Canonical";
            """);
    }
}
