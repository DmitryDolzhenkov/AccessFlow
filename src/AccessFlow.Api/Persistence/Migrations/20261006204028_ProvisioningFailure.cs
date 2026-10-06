using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessFlow.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProvisioningFailure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_provisioning_outbox_CreatedAt",
                schema: "access_requests",
                table: "provisioning_outbox");

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                schema: "access_requests",
                table: "provisioning_outbox",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                schema: "access_requests",
                table: "provisioning_outbox",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Entries saved before retries existed are due from their creation.
            migrationBuilder.Sql("""UPDATE access_requests.provisioning_outbox SET "NextAttemptAt" = "CreatedAt";""");

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningFailureReason",
                schema: "access_requests",
                table: "audit_log",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_provisioning_outbox_NextAttemptAt",
                schema: "access_requests",
                table: "provisioning_outbox",
                column: "NextAttemptAt",
                filter: "\"ProcessedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_provisioning_outbox_NextAttemptAt",
                schema: "access_requests",
                table: "provisioning_outbox");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                schema: "access_requests",
                table: "provisioning_outbox");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                schema: "access_requests",
                table: "provisioning_outbox");

            migrationBuilder.DropColumn(
                name: "ProvisioningFailureReason",
                schema: "access_requests",
                table: "audit_log");

            migrationBuilder.CreateIndex(
                name: "IX_provisioning_outbox_CreatedAt",
                schema: "access_requests",
                table: "provisioning_outbox",
                column: "CreatedAt",
                filter: "\"ProcessedAt\" IS NULL");
        }
    }
}
