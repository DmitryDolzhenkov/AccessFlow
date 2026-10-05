using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessFlow.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccessRequestDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Comment",
                schema: "access_requests",
                table: "audit_log",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                schema: "access_requests",
                table: "audit_log",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Comment",
                schema: "access_requests",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                schema: "access_requests",
                table: "audit_log");
        }
    }
}
