using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "jobs");

            migrationBuilder.CreateTable(
                name: "jobs",
                schema: "jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_type = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    price_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    production_status = table.Column<int>(type: "integer", nullable: false),
                    payment_status = table.Column<int>(type: "integer", nullable: false),
                    settlement_type = table.Column<int>(type: "integer", nullable: true),
                    settlement_reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    production_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ready_for_pickup_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs_jobs", x => x.id);
                    table.CheckConstraint("ck_jobs_jobs_customer_not_empty", "customer_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_jobs_jobs_description_not_empty", "length(btrim(description)) > 0");
                    table.CheckConstraint("ck_jobs_jobs_id_not_empty", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_jobs_jobs_lifecycle_consistent", "(\n    production_status = 1\n    AND published_at IS NULL\n    AND production_started_at IS NULL\n    AND ready_for_pickup_at IS NULL\n    AND completed_at IS NULL\n    AND cancelled_at IS NULL\n)\nOR\n(\n    production_status = 2\n    AND published_at IS NOT NULL\n    AND production_started_at IS NULL\n    AND ready_for_pickup_at IS NULL\n    AND completed_at IS NULL\n    AND cancelled_at IS NULL\n)\nOR\n(\n    production_status = 3\n    AND published_at IS NOT NULL\n    AND production_started_at IS NOT NULL\n    AND ready_for_pickup_at IS NULL\n    AND completed_at IS NULL\n    AND cancelled_at IS NULL\n)\nOR\n(\n    production_status = 4\n    AND published_at IS NOT NULL\n    AND production_started_at IS NOT NULL\n    AND ready_for_pickup_at IS NOT NULL\n    AND completed_at IS NULL\n    AND cancelled_at IS NULL\n)\nOR\n(\n    production_status = 5\n    AND published_at IS NOT NULL\n    AND production_started_at IS NOT NULL\n    AND ready_for_pickup_at IS NOT NULL\n    AND completed_at IS NOT NULL\n    AND cancelled_at IS NULL\n)\nOR\n(\n    production_status = 6\n    AND production_started_at IS NULL\n    AND ready_for_pickup_at IS NULL\n    AND completed_at IS NULL\n    AND cancelled_at IS NOT NULL\n)");
                    table.CheckConstraint("ck_jobs_jobs_paid_before_production", "(\n    production_status NOT IN (3, 4, 5)\n    OR payment_status = 2\n)\nAND\n(\n    production_status <> 6\n    OR payment_status = 1\n)");
                    table.CheckConstraint("ck_jobs_jobs_payment_status_valid", "payment_status IN (1, 2)");
                    table.CheckConstraint("ck_jobs_jobs_price_positive", "price_minor_units > 0");
                    table.CheckConstraint("ck_jobs_jobs_production_status_valid", "production_status IN (1, 2, 3, 4, 5, 6)");
                    table.CheckConstraint("ck_jobs_jobs_requester_not_empty", "requester_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_jobs_jobs_service_type_valid", "service_type IN (1, 2, 3)");
                    table.CheckConstraint("ck_jobs_jobs_settlement_consistent", "(\n    payment_status = 1\n    AND settlement_type IS NULL\n    AND settlement_reference_id IS NULL\n    AND settled_at IS NULL\n)\nOR\n(\n    payment_status = 2\n    AND settlement_type IN (1, 2)\n    AND settlement_reference_id IS NOT NULL\n    AND settlement_reference_id <>\n        '00000000-0000-0000-0000-000000000000'::uuid\n    AND settled_at IS NOT NULL\n    AND published_at IS NOT NULL\n    AND settled_at >= published_at\n)");
                    table.CheckConstraint("ck_jobs_jobs_timestamps_ordered", "(published_at IS NULL OR published_at >= created_at)\nAND\n(\n    settled_at IS NULL\n    OR\n    (\n        published_at IS NOT NULL\n        AND settled_at >= published_at\n    )\n)\nAND\n(\n    production_started_at IS NULL\n    OR\n    (\n        settled_at IS NOT NULL\n        AND production_started_at >= settled_at\n    )\n)\nAND\n(\n    ready_for_pickup_at IS NULL\n    OR\n    (\n        production_started_at IS NOT NULL\n        AND ready_for_pickup_at >= production_started_at\n    )\n)\nAND\n(\n    completed_at IS NULL\n    OR\n    (\n        ready_for_pickup_at IS NOT NULL\n        AND completed_at >= ready_for_pickup_at\n    )\n)\nAND\n(\n    cancelled_at IS NULL\n    OR cancelled_at >=\n        COALESCE(published_at, created_at)\n)");
                    table.CheckConstraint("ck_jobs_jobs_title_not_empty", "length(btrim(title)) > 0");
                    table.CheckConstraint("ck_jobs_jobs_version_positive", "version > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_jobs_customer_created_at",
                schema: "jobs",
                table: "jobs",
                columns: new[] { "customer_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_jobs_production_payment_status",
                schema: "jobs",
                table: "jobs",
                columns: new[] { "production_status", "payment_status" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_jobs_requester_created_at",
                schema: "jobs",
                table: "jobs",
                columns: new[] { "requester_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "uq_jobs_jobs_settlement_reference",
                schema: "jobs",
                table: "jobs",
                columns: new[] { "settlement_type", "settlement_reference_id" },
                unique: true,
                filter: "settlement_type IS NOT NULL AND settlement_reference_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "jobs",
                schema: "jobs");
        }
    }
}
