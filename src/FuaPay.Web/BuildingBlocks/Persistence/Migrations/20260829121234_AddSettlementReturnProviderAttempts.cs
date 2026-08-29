using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSettlementReturnProviderAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "settlement_return_provider_attempts",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    settlement_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    operation = table.Column<int>(type: "integer", nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    diagnostic = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments_return_provider_attempts", x => x.id);
                    table.CheckConstraint("ck_payments_return_provider_attempts_diagnostic_consistent", "(state IN (4, 5) AND diagnostic IS NOT NULL AND length(btrim(diagnostic)) > 0) OR (state IN (1, 2) AND diagnostic IS NULL) OR state = 3");
                    table.CheckConstraint("ck_payments_return_provider_attempts_id_not_empty", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_payments_return_provider_attempts_operation_valid", "operation IN (1, 2)");
                    table.CheckConstraint("ck_payments_return_provider_attempts_provider_valid", "provider IN (1, 2)");
                    table.CheckConstraint("ck_payments_return_provider_attempts_reference_not_blank", "length(btrim(provider_reference)) > 0");
                    table.CheckConstraint("ck_payments_return_provider_attempts_return_not_empty", "settlement_return_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_payments_return_provider_attempts_state_consistent", "(state = 1 AND started_at IS NULL AND finished_at IS NULL AND updated_at = created_at) OR (state = 2 AND started_at IS NOT NULL AND finished_at IS NULL AND updated_at = started_at) OR (state = 3 AND started_at IS NOT NULL AND finished_at IS NOT NULL AND updated_at = finished_at) OR (state = 4 AND finished_at IS NOT NULL AND updated_at = finished_at) OR (state = 5 AND started_at IS NOT NULL AND finished_at IS NULL AND updated_at >= started_at)");
                    table.CheckConstraint("ck_payments_return_provider_attempts_state_valid", "state IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("ck_payments_return_provider_attempts_timestamps_ordered", "updated_at >= created_at AND (started_at IS NULL OR (started_at >= created_at AND updated_at >= started_at)) AND (finished_at IS NULL OR (finished_at >= created_at AND updated_at >= finished_at AND (started_at IS NULL OR finished_at >= started_at)))");
                    table.CheckConstraint("ck_payments_return_provider_attempts_version_positive", "version > 0");
                    table.ForeignKey(
                        name: "fk_payments_return_provider_attempts_settlement_return",
                        column: x => x.settlement_return_id,
                        principalSchema: "payments",
                        principalTable: "settlement_returns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payments_return_provider_attempts_history",
                schema: "payments",
                table: "settlement_return_provider_attempts",
                columns: new[] { "settlement_return_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "uq_payments_return_provider_attempts_sequence",
                schema: "payments",
                table: "settlement_return_provider_attempts",
                column: "settlement_return_id",
                unique: true,
                filter: "state IN (1, 2, 3, 5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "settlement_return_provider_attempts",
                schema: "payments");
        }
    }
}
