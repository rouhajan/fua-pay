using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkEntraIdentitySafely : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "uq_access_external_identities_user_provider_tenant",
                schema: "access",
                table: "external_identities",
                columns: new[] { "user_id", "provider", "tenant" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_access_external_identities_user_provider_tenant",
                schema: "access",
                table: "external_identities");
        }
    }
}
