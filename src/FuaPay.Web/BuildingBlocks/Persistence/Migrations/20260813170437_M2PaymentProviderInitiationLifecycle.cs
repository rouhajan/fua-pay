using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M2PaymentProviderInitiationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_number_sequence",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    last_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments_order_number_sequence", x => x.id);
                    table.CheckConstraint("ck_payments_order_number_sequence_singleton", "id = 1");
                    table.CheckConstraint("ck_payments_order_number_sequence_value_valid", "last_value BETWEEN 1 AND 9999999999");
                });

            migrationBuilder.CreateTable(
                name: "payment_initiations",
                schema: "payments",
                columns: table => new
                {
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    order_number = table.Column<long>(type: "bigint", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    process_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments_payment_initiations", x => x.payment_id);
                    table.CheckConstraint("ck_payments_initiations_correlation_not_empty", "correlation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_payments_initiations_error_consistent", "(state = 4 AND last_error IS NOT NULL AND length(btrim(last_error)) > 0) OR (state <> 4 AND last_error IS NULL)");
                    table.CheckConstraint("ck_payments_initiations_order_number_valid", "order_number BETWEEN 1 AND 9999999999");
                    table.CheckConstraint("ck_payments_initiations_payment_not_empty", "payment_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_payments_initiations_process_uri_consistent", "(state = 3) OR process_uri IS NULL");
                    table.CheckConstraint("ck_payments_initiations_provider_valid", "provider IN (1, 2)");
                    table.CheckConstraint("ck_payments_initiations_state_consistent", "(state = 1 AND started_at IS NULL AND finished_at IS NULL) OR (state = 2 AND started_at IS NOT NULL AND finished_at IS NULL) OR (state IN (3, 4) AND started_at IS NOT NULL AND finished_at IS NOT NULL)");
                    table.CheckConstraint("ck_payments_initiations_state_valid", "state IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_payments_initiations_timestamps_ordered", "updated_at >= created_at AND (started_at IS NULL OR started_at >= created_at) AND (finished_at IS NULL OR (started_at IS NOT NULL AND finished_at >= started_at))");
                    table.CheckConstraint("ck_payments_initiations_version_positive", "version > 0");
                    table.ForeignKey(
                        name: "fk_payments_initiations_payment",
                        column: x => x.payment_id,
                        principalSchema: "payments",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payments_initiations_state_updated_at",
                schema: "payments",
                table: "payment_initiations",
                columns: new[] { "state", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "uq_payments_payment_initiations_correlation",
                schema: "payments",
                table: "payment_initiations",
                column: "correlation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_payments_payment_initiations_order_number",
                schema: "payments",
                table: "payment_initiations",
                column: "order_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_number_sequence",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payment_initiations",
                schema: "payments");
        }
    }
}
