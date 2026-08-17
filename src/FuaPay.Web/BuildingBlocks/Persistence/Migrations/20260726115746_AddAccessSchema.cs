using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "access");

            migrationBuilder.CreateTable(
                name: "users",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_users", x => x.id);
                    table.CheckConstraint("ck_access_users_display_name_not_empty", "length(btrim(display_name)) > 0");
                    table.CheckConstraint("ck_access_users_email_not_empty", "email IS NULL OR length(btrim(email)) > 0");
                    table.CheckConstraint("ck_access_users_id_not_empty", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_access_users_last_seen_valid", "last_seen_at >= created_at");
                    table.CheckConstraint("ck_access_users_status_valid", "status IN (1, 2)");
                    table.CheckConstraint("ck_access_users_version_positive", "version > 0");
                });

            migrationBuilder.CreateTable(
                name: "external_identities",
                schema: "access",
                columns: table => new
                {
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tenant = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_external_identities", x => new { x.provider, x.tenant, x.subject });
                    table.CheckConstraint("ck_access_external_identities_provider_not_empty", "length(btrim(provider)) > 0");
                    table.CheckConstraint("ck_access_external_identities_subject_not_empty", "length(btrim(subject)) > 0");
                    table.CheckConstraint("ck_access_external_identities_tenant_not_empty", "length(btrim(tenant)) > 0");
                    table.CheckConstraint("ck_access_external_identities_user_not_empty", "user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "fk_access_external_identities_user",
                        column: x => x.user_id,
                        principalSchema: "access",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_assignments",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by_type = table.Column<int>(type: "integer", nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    granted_by_process_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_type = table.Column<int>(type: "integer", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_by_process_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_role_assignments", x => x.id);
                    table.CheckConstraint("ck_access_role_assignments_granted_actor_valid", "(\n    granted_by_type = 1\n    AND granted_by_user_id IS NOT NULL\n    AND granted_by_process_name IS NULL\n)\nOR\n(\n    granted_by_type = 2\n    AND granted_by_user_id IS NULL\n    AND granted_by_process_name IS NOT NULL\n    AND length(btrim(granted_by_process_name)) > 0\n)");
                    table.CheckConstraint("ck_access_role_assignments_id_not_empty", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_access_role_assignments_revoked_actor_valid", "(\n    revoked_at IS NULL\n    AND revoked_by_type IS NULL\n    AND revoked_by_user_id IS NULL\n    AND revoked_by_process_name IS NULL\n)\nOR\n(\n    revoked_at IS NOT NULL\n    AND revoked_at >= granted_at\n    AND\n    (\n        (\n            revoked_by_type = 1\n            AND revoked_by_user_id IS NOT NULL\n            AND revoked_by_process_name IS NULL\n        )\n        OR\n        (\n            revoked_by_type = 2\n            AND revoked_by_user_id IS NULL\n            AND revoked_by_process_name IS NOT NULL\n            AND length(btrim(revoked_by_process_name)) > 0\n        )\n    )\n)");
                    table.CheckConstraint("ck_access_role_assignments_role_valid", "role IN (1, 2, 3)");
                    table.CheckConstraint("ck_access_role_assignments_user_not_empty", "user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "fk_access_role_assignments_granted_by_user",
                        column: x => x.granted_by_user_id,
                        principalSchema: "access",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_role_assignments_revoked_by_user",
                        column: x => x.revoked_by_user_id,
                        principalSchema: "access",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_role_assignments_user",
                        column: x => x.user_id,
                        principalSchema: "access",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_external_identities_user",
                schema: "access",
                table: "external_identities",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_role_assignments_granted_by_user",
                schema: "access",
                table: "role_assignments",
                column: "granted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_role_assignments_revoked_by_user",
                schema: "access",
                table: "role_assignments",
                column: "revoked_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_role_assignments_user_granted_at",
                schema: "access",
                table: "role_assignments",
                columns: new[] { "user_id", "granted_at" });

            migrationBuilder.CreateIndex(
                name: "uq_access_role_assignments_active_role",
                schema: "access",
                table: "role_assignments",
                columns: new[] { "user_id", "role" },
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_access_users_email",
                schema: "access",
                table: "users",
                column: "email");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_identities",
                schema: "access");

            migrationBuilder.DropTable(
                name: "role_assignments",
                schema: "access");

            migrationBuilder.DropTable(
                name: "users",
                schema: "access");
        }
    }
}
