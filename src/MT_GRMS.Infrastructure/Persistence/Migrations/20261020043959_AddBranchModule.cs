using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "branches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ManagerName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Active"),
                    PendingOrdersCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastOrderDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_branches_TenantId",
                table: "branches",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_branches_TenantId_Code",
                table: "branches",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            migrationBuilder.InsertData(
                table: "branches",
                columns: new[] { "Id", "TenantId", "Code", "Name", "Address", "Phone", "ManagerName", "Status", "PendingOrdersCount", "LastOrderDate", "CreatedAt", "IsDeleted" },
                values: new object[,]
                {
                    { Guid.Parse("11111111-1111-1111-1111-111111111111"), "default-tenant", "BR-01", "Chi nhánh Cầu Giấy", "123 Đường Cầu Giấy, Q. Cầu Giấy, Hà Nội", "024.3768.9999", "Nguyễn Văn An", "Active", 3, new DateTime(2026, 10, 18, 10, 30, 0, DateTimeKind.Utc), now, false },
                    { Guid.Parse("22222222-2222-2222-2222-222222222222"), "default-tenant", "BR-02", "Chi nhánh Đống Đa", "45 Phố Thái Hà, Q. Đống Đa, Hà Nội", "024.3857.2222", "Trần Thị Mai", "Active", 1, new DateTime(2026, 10, 19, 14, 15, 0, DateTimeKind.Utc), now, false },
                    { Guid.Parse("33333333-3333-3333-3333-333333333333"), "default-tenant", "BR-03", "Chi nhánh Hà Đông", "78 Đường Quang Trung, Q. Hà Đông, Hà Nội", "024.3355.8888", "Lê Hoàng Long", "Active", 5, new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc), now, false },
                    { Guid.Parse("44444444-4444-4444-4444-444444444444"), "default-tenant", "BR-04", "Chi nhánh Ba Đình", "12 Phố Đội Cấn, Q. Ba Đình, Hà Nội", "024.3845.6666", "Phạm Quỳnh Nga", "Inactive", 0, new DateTime(2026, 10, 15, 16, 45, 0, DateTimeKind.Utc), now, false },
                    { Guid.Parse("55555555-5555-5555-5555-555555555555"), "default-tenant", "DC-01", "Kho tổng DC1 (Long Biên)", "Lô CN-05 KCN Sài Đồng B, Q. Long Biên, Hà Nội", "024.3678.1111", "Vũ Đức Thịnh", "Active", 8, new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc), now, false }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "branches");
        }
    }
}
