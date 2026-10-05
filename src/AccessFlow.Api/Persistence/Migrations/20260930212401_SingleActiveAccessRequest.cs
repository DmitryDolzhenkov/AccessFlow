using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessFlow.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SingleActiveAccessRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_access_requests_active_BeneficiaryId_SystemId",
                schema: "access_requests",
                table: "access_requests",
                columns: new[] { "BeneficiaryId", "SystemId" },
                unique: true,
                filter: "\"Status\" IN ('Pending', 'Approved')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_access_requests_active_BeneficiaryId_SystemId",
                schema: "access_requests",
                table: "access_requests");
        }
    }
}
