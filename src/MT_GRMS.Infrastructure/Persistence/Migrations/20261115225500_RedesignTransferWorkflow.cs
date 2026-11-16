using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// Expands the transfer workflow without rewriting historical inventory movements.
/// Existing Received rows become Completed; in-transit rows remain physically in transit.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261115225500_RedesignTransferWorkflow")]
public partial class RedesignTransferWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(name: "CancelledAt", table: "Transfers", type: "timestamp without time zone", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "CancelledByUserId", table: "Transfers", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(name: "CompletionResult", table: "Transfers", type: "character varying(40)", maxLength: 40, nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "DispatchedByUserId", table: "Transfers", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<bool>(name: "HasDiscrepancy", table: "Transfers", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>(name: "IsStockReserved", table: "Transfers", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>(name: "Origin", table: "Transfers", type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "OwnerAllocation");
        migrationBuilder.AddColumn<string>(name: "Priority", table: "Transfers", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Normal");
        migrationBuilder.AddColumn<Guid>(name: "ReceivedByUserId", table: "Transfers", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "RelatedOrderId", table: "Transfers", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "SourceConfirmedAt", table: "Transfers", type: "timestamp without time zone", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "SourceConfirmedByUserId", table: "Transfers", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "SubmittedAt", table: "Transfers", type: "timestamp without time zone", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "SubmittedByUserId", table: "Transfers", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "TransferRequestId", table: "Transfers", type: "uuid", nullable: true);

        migrationBuilder.CreateTable(
            name: "TransferRequests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "text", nullable: false),
                RequestCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ToBranchId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Open"),
                Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Urgent"),
                RelatedOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                RequiredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                CancelledAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                CancelReason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_TransferRequests", x => x.Id));

        migrationBuilder.CreateTable(
            name: "TransferRequestDetails",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "text", nullable: false),
                TransferRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                RequestedQuantity = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                AllocatedQuantity = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                ReceivedQuantity = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TransferRequestDetails", x => x.Id);
                table.ForeignKey("FK_TransferRequestDetails_TransferRequests_TransferRequestId", x => x.TransferRequestId, "TransferRequests", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TransferStatusHistories",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "text", nullable: false),
                TransferId = table.Column<Guid>(type: "uuid", nullable: false),
                FromStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                ToStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorBranchId = table.Column<Guid>(type: "uuid", nullable: true),
                ActorRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TransferStatusHistories", x => x.Id);
                table.ForeignKey("FK_TransferStatusHistories_Transfers_TransferId", x => x.TransferId, "Transfers", "TransferID", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_TransferRequests_TenantId_RequestCode", "TransferRequests", new[] { "TenantId", "RequestCode" }, unique: true);
        migrationBuilder.CreateIndex("IX_TransferRequestDetails_TransferRequestId_ProductId", "TransferRequestDetails", new[] { "TransferRequestId", "ProductId" }, unique: true);
        migrationBuilder.CreateIndex("IX_Transfers_TransferRequestId", "Transfers", "TransferRequestId");
        migrationBuilder.CreateIndex("IX_TransferStatusHistories_TransferId_CreatedAt", "TransferStatusHistories", new[] { "TransferId", "CreatedAt" });
        migrationBuilder.AddForeignKey("FK_Transfers_TransferRequests_TransferRequestId", "Transfers", "TransferRequestId", "TransferRequests", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql("UPDATE \"Transfers\" SET \"Status\" = 'Completed', \"CompletionResult\" = CASE WHEN COALESCE(\"DiscrepancyNote\", '') = '' THEN 'Received' ELSE 'ReceivedWithDiscrepancy' END WHERE \"Status\" = 'Received';");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_Transfers_TransferRequests_TransferRequestId", "Transfers");
        migrationBuilder.DropTable("TransferStatusHistories");
        migrationBuilder.DropTable("TransferRequestDetails");
        migrationBuilder.DropTable("TransferRequests");
        migrationBuilder.Sql("UPDATE \"Transfers\" SET \"Status\" = 'Received' WHERE \"Status\" = 'Completed';");

        foreach (var column in new[]
        {
            "CancelledAt", "CancelledByUserId", "CompletionResult", "DispatchedByUserId",
            "HasDiscrepancy", "IsStockReserved", "Origin", "Priority", "ReceivedByUserId", "RelatedOrderId",
            "SourceConfirmedAt", "SourceConfirmedByUserId", "SubmittedAt", "SubmittedByUserId",
            "TransferRequestId"
        })
        {
            migrationBuilder.DropColumn(name: column, table: "Transfers");
        }
    }
}
