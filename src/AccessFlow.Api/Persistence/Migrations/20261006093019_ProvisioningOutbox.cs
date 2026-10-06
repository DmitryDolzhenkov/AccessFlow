using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessFlow.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProvisioningOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "provisioning_outbox",
                schema: "access_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provisioning_outbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_provisioning_outbox_access_requests_AccessRequestId",
                        column: x => x.AccessRequestId,
                        principalSchema: "access_requests",
                        principalTable: "access_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_provisioning_outbox_AccessRequestId",
                schema: "access_requests",
                table: "provisioning_outbox",
                column: "AccessRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_provisioning_outbox_CreatedAt",
                schema: "access_requests",
                table: "provisioning_outbox",
                column: "CreatedAt",
                filter: "\"ProcessedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provisioning_outbox",
                schema: "access_requests");
        }
    }
}
