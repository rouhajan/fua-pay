using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M2PaymentLongOpenDiscoveryIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_payments_csob_pending_long_open",
                schema: "payments",
                table: "payments",
                columns: new[] { "updated_at", "id" },
                filter: "provider = 2 AND status = 2 AND provider_reference IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payments_csob_pending_long_open",
                schema: "payments",
                table: "payments");
        }
    }
}
