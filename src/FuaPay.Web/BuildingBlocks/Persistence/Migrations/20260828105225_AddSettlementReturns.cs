using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSettlementReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "settlement_returns",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    original_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    administrator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments_settlement_returns", x => x.id);
                    table.CheckConstraint("ck_payments_settlement_returns_admin_not_empty", "administrator_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_payments_settlement_returns_amount_positive", "amount_minor_units > 0");
                    table.CheckConstraint("ck_payments_settlement_returns_currency_supported", "currency = 'CZK'");
                    table.CheckConstraint("ck_payments_settlement_returns_customer_not_empty", "customer_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_payments_settlement_returns_id_not_empty", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_payments_settlement_returns_job_not_empty", "job_id IS NULL OR job_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_payments_settlement_returns_kind_valid", "kind IN (1, 2, 3)");
                    table.CheckConstraint("ck_payments_settlement_returns_original_not_empty", "original_payment_id IS NULL OR original_payment_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_payments_settlement_returns_reason_not_blank", "length(btrim(reason)) > 0");
                    table.CheckConstraint("ck_payments_settlement_returns_request_not_empty", "request_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_payments_settlement_returns_source_consistent", "(kind = 1 AND original_payment_id IS NOT NULL AND job_id IS NOT NULL) OR (kind = 2 AND original_payment_id IS NULL AND job_id IS NOT NULL) OR (kind = 3 AND original_payment_id IS NOT NULL AND job_id IS NULL)");
                    table.CheckConstraint("ck_payments_settlement_returns_state_consistent", "(state = 1 AND started_at IS NULL AND completed_at IS NULL) OR (state = 2 AND started_at IS NOT NULL AND completed_at IS NULL) OR (state IN (3, 4) AND started_at IS NOT NULL AND completed_at IS NOT NULL) OR (state = 5 AND started_at IS NOT NULL AND completed_at IS NULL)");
                    table.CheckConstraint("ck_payments_settlement_returns_state_valid", "state IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("ck_payments_settlement_returns_timestamps_ordered", "updated_at >= requested_at AND (started_at IS NULL OR (started_at >= requested_at AND updated_at >= started_at)) AND (completed_at IS NULL OR (started_at IS NOT NULL AND completed_at >= started_at AND updated_at >= completed_at))");
                    table.CheckConstraint("ck_payments_settlement_returns_version_positive", "version > 0");
                    table.ForeignKey(
                        name: "fk_payments_settlement_returns_original_payment",
                        column: x => x.original_payment_id,
                        principalSchema: "payments",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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

            migrationBuilder.CreateIndex(
                name: "uq_payments_settlement_returns_request",
                schema: "payments",
                table: "settlement_returns",
                column: "request_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "settlement_returns",
                schema: "payments");
        }
    }
}
