using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M2PaymentReconciliationRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "observed_process_uri",
                schema: "payments",
                table: "payment_initiations",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "observed_provider_reference",
                schema: "payments",
                table: "payment_initiations",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "csob_payment_reconciliation",
                schema: "payments",
                columns: table => new
                {
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    state = table.Column<int>(type: "integer", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_browser_return_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_gateway_payment_status = table.Column<int>(type: "integer", nullable: true),
                    last_result_code = table.Column<int>(type: "integer", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_csob_payment_reconciliation", x => x.payment_id);
                    table.CheckConstraint("ck_csob_reconciliation_attempt_count_valid", "attempt_count >= 0");
                    table.CheckConstraint("ck_csob_reconciliation_completion_consistent", "(state = 4 AND completed_at IS NOT NULL) OR (state <> 4 AND completed_at IS NULL)");
                    table.CheckConstraint("ck_csob_reconciliation_lease_consistent", "(state = 2 AND lease_token IS NOT NULL AND lease_expires_at IS NOT NULL) OR (state <> 2 AND lease_token IS NULL AND lease_expires_at IS NULL)");
                    table.CheckConstraint("ck_csob_reconciliation_payment_not_empty", "payment_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_csob_reconciliation_reference_consistent", "(provider_reference IS NULL AND state = 3) OR (provider_reference IS NOT NULL AND length(btrim(provider_reference)) > 0)");
                    table.CheckConstraint("ck_csob_reconciliation_state_valid", "state IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_csob_reconciliation_timestamps_ordered", "updated_at >= created_at AND next_attempt_at >= created_at AND (last_attempt_at IS NULL OR last_attempt_at >= created_at) AND (last_browser_return_at IS NULL OR last_browser_return_at >= created_at) AND (completed_at IS NULL OR completed_at >= created_at)");
                    table.CheckConstraint("ck_csob_reconciliation_version_positive", "version > 0");
                    table.ForeignKey(
                        name: "fk_csob_reconciliation_payment",
                        column: x => x.payment_id,
                        principalSchema: "payments",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_initiations_observation_consistent",
                schema: "payments",
                table: "payment_initiations",
                sql: "(state <> 4 AND observed_provider_reference IS NULL AND observed_process_uri IS NULL) OR (state = 4 AND (observed_process_uri IS NULL OR observed_provider_reference IS NOT NULL))");

            migrationBuilder.CreateIndex(
                name: "ix_csob_reconciliation_due",
                schema: "payments",
                table: "csob_payment_reconciliation",
                columns: new[] { "state", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "uq_csob_reconciliation_reference",
                schema: "payments",
                table: "csob_payment_reconciliation",
                column: "provider_reference",
                unique: true,
                filter: "provider_reference IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "csob_payment_reconciliation",
                schema: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_initiations_observation_consistent",
                schema: "payments",
                table: "payment_initiations");

            migrationBuilder.DropColumn(
                name: "observed_process_uri",
                schema: "payments",
                table: "payment_initiations");

            migrationBuilder.DropColumn(
                name: "observed_provider_reference",
                schema: "payments",
                table: "payment_initiations");
        }
    }
}
