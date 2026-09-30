using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AccessFlow.Directory.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "IX_systems_OwnerId",
                schema: "directory",
                table: "systems",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "systems",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "users",
                schema: "directory");
        }
    }
}
