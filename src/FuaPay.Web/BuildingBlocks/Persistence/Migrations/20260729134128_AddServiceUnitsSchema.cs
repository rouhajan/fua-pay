using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceUnitsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "service_units");

            migrationBuilder.CreateTable(
                name: "units",
                schema: "service_units",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    default_service_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_type = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_process_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deactivated_by_type = table.Column<int>(type: "integer", nullable: true),
                    deactivated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deactivated_by_process_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_units_units", x => x.id);
                    table.CheckConstraint("ck_service_units_units_code_valid", "code ~ '^[A-Z0-9]{2,8}$'");
                    table.CheckConstraint("ck_service_units_units_created_actor_valid", "(\n    created_by_type = 1\n    AND created_by_user_id IS NOT NULL\n    AND created_by_process_name IS NULL\n)\nOR\n(\n    created_by_type = 2\n    AND created_by_user_id IS NULL\n    AND created_by_process_name IS NOT NULL\n    AND length(btrim(created_by_process_name)) > 0\n)");
                    table.CheckConstraint("ck_service_units_units_deactivated_actor_valid", "(\n    status = 1\n    AND deactivated_at IS NULL\n    AND deactivated_by_type IS NULL\n    AND deactivated_by_user_id IS NULL\n    AND deactivated_by_process_name IS NULL\n)\nOR\n(\n    status = 2\n    AND deactivated_at IS NOT NULL\n    AND deactivated_at >= created_at\n    AND\n    ((\n    deactivated_by_type = 1\n    AND deactivated_by_user_id IS NOT NULL\n    AND deactivated_by_process_name IS NULL\n)\nOR\n(\n    deactivated_by_type = 2\n    AND deactivated_by_user_id IS NULL\n    AND deactivated_by_process_name IS NOT NULL\n    AND length(btrim(deactivated_by_process_name)) > 0\n)    )\n)");
                    table.CheckConstraint("ck_service_units_units_id_not_empty", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_service_units_units_name_not_blank", "length(btrim(display_name)) > 0");
                    table.CheckConstraint("ck_service_units_units_service_type_valid", "default_service_type IN (1, 2, 3)");
                    table.CheckConstraint("ck_service_units_units_status_valid", "status IN (1, 2)");
                    table.CheckConstraint("ck_service_units_units_version_positive", "version > 0");
                });

            migrationBuilder.CreateTable(
                name: "requester_assignments",
                schema: "service_units",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by_type = table.Column<int>(type: "integer", nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    granted_by_process_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_type = table.Column<int>(type: "integer", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_by_process_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_units_requester_assignments", x => x.id);
                    table.CheckConstraint("ck_service_units_assignments_granted_actor_valid", "(\n    granted_by_type = 1\n    AND granted_by_user_id IS NOT NULL\n    AND granted_by_process_name IS NULL\n)\nOR\n(\n    granted_by_type = 2\n    AND granted_by_user_id IS NULL\n    AND granted_by_process_name IS NOT NULL\n    AND length(btrim(granted_by_process_name)) > 0\n)");
                    table.CheckConstraint("ck_service_units_assignments_id_not_empty", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_service_units_assignments_revoked_actor_valid", "(\n    revoked_at IS NULL\n    AND revoked_by_type IS NULL\n    AND revoked_by_user_id IS NULL\n    AND revoked_by_process_name IS NULL\n)\nOR\n(\n    revoked_at IS NOT NULL\n    AND revoked_at >= granted_at\n    AND\n    ((\n    revoked_by_type = 1\n    AND revoked_by_user_id IS NOT NULL\n    AND revoked_by_process_name IS NULL\n)\nOR\n(\n    revoked_by_type = 2\n    AND revoked_by_user_id IS NULL\n    AND revoked_by_process_name IS NOT NULL\n    AND length(btrim(revoked_by_process_name)) > 0\n)    )\n)");
                    table.CheckConstraint("ck_service_units_assignments_unit_not_empty", "service_unit_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_service_units_assignments_user_not_empty", "user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_service_units_assignments_version_positive", "version > 0");
                    table.ForeignKey(
                        name: "fk_service_units_assignments_unit",
                        column: x => x.service_unit_id,
                        principalSchema: "service_units",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_service_units_assignments_user_granted_at",
                schema: "service_units",
                table: "requester_assignments",
                columns: new[] { "user_id", "granted_at" });

            migrationBuilder.CreateIndex(
                name: "uq_service_units_requester_assignments_active",
                schema: "service_units",
                table: "requester_assignments",
                columns: new[] { "service_unit_id", "user_id" },
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_service_units_units_status_name",
                schema: "service_units",
                table: "units",
                columns: new[] { "status", "display_name" });

            migrationBuilder.CreateIndex(
                name: "uq_service_units_units_code",
                schema: "service_units",
                table: "units",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requester_assignments",
                schema: "service_units");

            migrationBuilder.DropTable(
                name: "units",
                schema: "service_units");
        }
    }
}
