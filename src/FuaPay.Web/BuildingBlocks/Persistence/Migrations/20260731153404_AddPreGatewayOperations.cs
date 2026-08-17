using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreGatewayOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.CreateTable(
                name: "events",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_process_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                    table.CheckConstraint("ck_audit_events_actor_consistent", "(actor_user_id IS NOT NULL AND actor_process_name IS NULL) OR (actor_user_id IS NULL AND actor_process_name IS NOT NULL)");
                    table.CheckConstraint("ck_audit_events_id_not_empty", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_audit_events_text_not_blank", "length(btrim(action)) > 0 AND length(btrim(entity_type)) > 0 AND length(btrim(entity_id)) > 0 AND length(btrim(description)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    subject = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications_outbox", x => x.id);
                    table.CheckConstraint("ck_notifications_outbox_attempt_count_nonnegative", "attempt_count >= 0");
                    table.CheckConstraint("ck_notifications_outbox_id_not_empty", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_notifications_outbox_recipient_not_empty", "recipient_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose_type = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount_positive", "amount_minor_units > 0");
                    table.CheckConstraint("ck_payments_completion_consistent", "(status IN (1, 2) AND completed_at IS NULL) OR (status IN (3, 4, 5, 6) AND completed_at IS NOT NULL)");
                    table.CheckConstraint("ck_payments_customer_not_empty", "customer_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_payments_failure_consistent", "(status = 4 AND failure_reason IS NOT NULL AND length(btrim(failure_reason)) > 0) OR (status <> 4 AND failure_reason IS NULL)");
                    table.CheckConstraint("ck_payments_id_not_empty", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_payments_provider_reference_consistent", "(status = 1 AND provider_reference IS NULL) OR (status <> 1 AND provider_reference IS NOT NULL AND length(btrim(provider_reference)) > 0)");
                    table.CheckConstraint("ck_payments_provider_valid", "provider IN (1, 2)");
                    table.CheckConstraint("ck_payments_purpose_consistent", "(purpose_type = 1 AND job_id IS NULL) OR (purpose_type = 2 AND job_id IS NOT NULL AND job_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
                    table.CheckConstraint("ck_payments_purpose_valid", "purpose_type IN (1, 2)");
                    table.CheckConstraint("ck_payments_status_valid", "status IN (1, 2, 3, 4, 5, 6)");
                    table.CheckConstraint("ck_payments_timestamps_ordered", "updated_at >= created_at AND (completed_at IS NULL OR completed_at >= created_at)");
                    table.CheckConstraint("ck_payments_version_positive", "version > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_actor_user",
                schema: "audit",
                table: "events",
                columns: new[] { "actor_user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_entity",
                schema: "audit",
                table: "events",
                columns: new[] { "entity_type", "entity_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_occurred_at",
                schema: "audit",
                table: "events",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_outbox_pending",
                schema: "notifications",
                table: "outbox",
                columns: new[] { "sent_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_outbox_recipient_created",
                schema: "notifications",
                table: "outbox",
                columns: new[] { "recipient_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_customer_created_at",
                schema: "payments",
                table: "payments",
                columns: new[] { "customer_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_status_created_at",
                schema: "payments",
                table: "payments",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "uq_payments_open_job",
                schema: "payments",
                table: "payments",
                column: "job_id",
                unique: true,
                filter: "job_id IS NOT NULL AND status IN (1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "uq_payments_provider_reference",
                schema: "payments",
                table: "payments",
                columns: new[] { "provider", "provider_reference" },
                unique: true,
                filter: "provider_reference IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "events",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "outbox",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "payments");
        }
    }
}
