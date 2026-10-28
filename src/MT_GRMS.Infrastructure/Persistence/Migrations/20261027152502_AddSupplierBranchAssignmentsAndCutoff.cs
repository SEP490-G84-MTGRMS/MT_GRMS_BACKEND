using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierBranchAssignmentsAndCutoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CutoffDayOffset",
                table: "supplier_delivery_configs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "LeadTimeDays",
                table: "supplier_delivery_configs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "OrderCutoffTime",
                table: "supplier_delivery_configs",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "17:00");

            migrationBuilder.CreateTable(
                name: "supplier_branch_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_branch_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_branch_assignments_branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_branch_assignments_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_branch_assignments_BranchId",
                table: "supplier_branch_assignments",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_branch_assignments_SupplierId",
                table: "supplier_branch_assignments",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_branch_assignments_TenantId_SupplierId_BranchId",
                table: "supplier_branch_assignments",
                columns: new[] { "TenantId", "SupplierId", "BranchId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_branch_assignments");

            migrationBuilder.DropColumn(
                name: "CutoffDayOffset",
                table: "supplier_delivery_configs");

            migrationBuilder.DropColumn(
                name: "LeadTimeDays",
                table: "supplier_delivery_configs");

            migrationBuilder.DropColumn(
                name: "OrderCutoffTime",
                table: "supplier_delivery_configs");
        }
    }
}
