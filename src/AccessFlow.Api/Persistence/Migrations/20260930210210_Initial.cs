using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AccessFlow.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "access_requests");

            migrationBuilder.EnsureSchema(
                name: "directory");

            migrationBuilder.CreateTable(
                name: "users",
                schema: "directory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "systems",
                schema: "directory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProvisioningUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_systems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_systems_users_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "directory",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "access_requests",
                schema: "access_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequesterId = table.Column<Guid>(type: "uuid", nullable: false),
                    BeneficiaryId = table.Column<Guid>(type: "uuid", nullable: false),
                    SystemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Justification = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_access_requests_systems_SystemId",
                        column: x => x.SystemId,
                        principalSchema: "directory",
                        principalTable: "systems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_access_requests_users_BeneficiaryId",
                        column: x => x.BeneficiaryId,
                        principalSchema: "directory",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_access_requests_users_RequesterId",
                        column: x => x.RequesterId,
                        principalSchema: "directory",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "directory",
                table: "users",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { new Guid("0198f3a0-0000-7000-8000-000000000001"), "Alice" },
                    { new Guid("0198f3a0-0000-7000-8000-000000000002"), "Bob" },
                    { new Guid("0198f3a0-0000-7000-8000-000000000003"), "Carol" },
                    { new Guid("0198f3a0-0000-7000-8000-000000000004"), "Dave" }
                });

            migrationBuilder.InsertData(
                schema: "directory",
                table: "systems",
                columns: new[] { "Id", "Name", "OwnerId", "ProvisioningUrl" },
                values: new object[,]
                {
                    { new Guid("0198f3a0-0000-7000-8000-000000000101"), "Jira", new Guid("0198f3a0-0000-7000-8000-000000000001"), "http://localhost:5199/provisioning/jira" },
                    { new Guid("0198f3a0-0000-7000-8000-000000000102"), "GitLab", new Guid("0198f3a0-0000-7000-8000-000000000002"), "http://localhost:5199/provisioning/gitlab" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_access_requests_BeneficiaryId",
                schema: "access_requests",
                table: "access_requests",
                column: "BeneficiaryId");

            migrationBuilder.CreateIndex(
                name: "IX_access_requests_RequesterId",
                schema: "access_requests",
                table: "access_requests",
                column: "RequesterId");

            migrationBuilder.CreateIndex(
                name: "IX_access_requests_SystemId",
                schema: "access_requests",
                table: "access_requests",
                column: "SystemId");

            migrationBuilder.CreateIndex(
                name: "IX_systems_OwnerId",
                schema: "directory",
                table: "systems",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_requests",
                schema: "access_requests");

            migrationBuilder.DropTable(
                name: "systems",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "users",
                schema: "directory");
        }
    }
}
