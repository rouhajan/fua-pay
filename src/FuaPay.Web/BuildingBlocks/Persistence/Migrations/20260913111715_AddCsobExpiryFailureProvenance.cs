using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCsobExpiryFailureProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_failure_consistent",
                schema: "payments",
                table: "payments");

            migrationBuilder.AddColumn<int>(
                name: "failure_provenance",
                schema: "payments",
                table: "payments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_failure_consistent",
                schema: "payments",
                table: "payments",
                sql: "(status = 4 AND failure_reason IS NOT NULL AND length(btrim(failure_reason)) > 0 AND (failure_provenance IS NULL OR failure_provenance = 1)) OR (status <> 4 AND failure_reason IS NULL AND failure_provenance IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_failure_consistent",
                schema: "payments",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "failure_provenance",
                schema: "payments",
                table: "payments");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_failure_consistent",
                schema: "payments",
                table: "payments",
                sql: "(status = 4 AND failure_reason IS NOT NULL AND length(btrim(failure_reason)) > 0) OR (status <> 4 AND failure_reason IS NULL)");
        }
    }
}
