using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupportCardJobPartialRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_payments_settlement_returns_job",
                schema: "payments",
                table: "settlement_returns");

            migrationBuilder.DropIndex(
                name: "uq_payments_settlement_returns_original_payment",
                schema: "payments",
                table: "settlement_returns");

            migrationBuilder.CreateIndex(
                name: "ix_payments_settlement_returns_card_job_job",
                schema: "payments",
                table: "settlement_returns",
                column: "job_id",
                filter: "kind = 1");

            migrationBuilder.CreateIndex(
                name: "ix_payments_settlement_returns_card_job_payment",
                schema: "payments",
                table: "settlement_returns",
                columns: new[] { "original_payment_id", "state" },
                filter: "kind = 1");

            migrationBuilder.CreateIndex(
                name: "uq_payments_settlement_returns_job",
                schema: "payments",
                table: "settlement_returns",
                column: "job_id",
                unique: true,
                filter: "job_id IS NOT NULL AND kind = 2");

            migrationBuilder.CreateIndex(
                name: "uq_payments_settlement_returns_original_payment",
                schema: "payments",
                table: "settlement_returns",
                column: "original_payment_id",
                unique: true,
                filter: "original_payment_id IS NOT NULL AND kind = 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payments_settlement_returns_card_job_job",
                schema: "payments",
                table: "settlement_returns");

            migrationBuilder.DropIndex(
                name: "ix_payments_settlement_returns_card_job_payment",
                schema: "payments",
                table: "settlement_returns");

            migrationBuilder.DropIndex(
                name: "uq_payments_settlement_returns_job",
                schema: "payments",
                table: "settlement_returns");

            migrationBuilder.DropIndex(
                name: "uq_payments_settlement_returns_original_payment",
                schema: "payments",
                table: "settlement_returns");

            migrationBuilder.CreateIndex(
                name: "uq_payments_settlement_returns_job",
                schema: "payments",
                table: "settlement_returns",
                column: "job_id",
                unique: true,
                filter: "job_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_payments_settlement_returns_original_payment",
                schema: "payments",
                table: "settlement_returns",
                column: "original_payment_id",
                unique: true,
                filter: "original_payment_id IS NOT NULL");
        }
    }
}
