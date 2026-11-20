using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261118170000_MergeShiftParticipationIntoZReport")]
public partial class MergeShiftParticipationIntoZReport : Migration
{
    public const string MergeSql = """
            ALTER TABLE "ShiftZReports"
                ADD COLUMN IF NOT EXISTS "ParticipantsJson" jsonb NOT NULL DEFAULT '[]'::jsonb;

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM "ShiftCloseConfirmations"
                    WHERE NOT "IsDeleted"
                    GROUP BY "ShiftId" HAVING COUNT(*) > 1
                ) THEN
                    RAISE EXCEPTION 'Multiple close confirmations require manual reconciliation before shift merge';
                END IF;
            END $$;

            UPDATE "ShiftZReports" AS shift
            SET "ParticipantsJson" = participants.snapshot
            FROM (
                SELECT p."ShiftId",
                    jsonb_agg(jsonb_build_object(
                        'Id', p."Id", 'ShiftId', p."ShiftId", 'UserId', p."UserId",
                        'UserName', p."UserName", 'FullName', p."FullName",
                        'JoinedAtUtc', p."JoinedAtUtc", 'LastActiveAtUtc', p."LastActiveAtUtc"
                    ) ORDER BY p."JoinedAtUtc") AS snapshot
                FROM "ShiftParticipants" AS p
                WHERE NOT p."IsDeleted"
                GROUP BY p."ShiftId"
            ) AS participants
            WHERE shift."Id" = participants."ShiftId";

            UPDATE "ShiftZReports" AS shift
            SET "ClosedByUserId" = confirmation."UserId",
                "ClosedAtUtc" = COALESCE(shift."ClosedAtUtc", confirmation."ConfirmedAtUtc")
            FROM "ShiftCloseConfirmations" AS confirmation
            WHERE shift."Id" = confirmation."ShiftId"
              AND NOT confirmation."IsDeleted" AND shift."ClosedByUserId" IS NULL;

            DROP TABLE "ShiftCloseConfirmations";
            DROP TABLE "ShiftParticipants";
            """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(MergeSql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Restore shift participation from a verified backup before rolling this migration back.");
    }
}
