using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersistentPrintCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "print_credentials",
                schema: "credits",
                columns: table => new
                {
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    code_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credits_print_credentials", x => x.owner_id);
                    table.CheckConstraint("ck_credits_print_credentials_email_normalized", "normalized_email = lower(btrim(normalized_email)) AND length(normalized_email) > 0");
                    table.CheckConstraint("ck_credits_print_credentials_changed_at_valid", "changed_at >= created_at");
                    table.CheckConstraint("ck_credits_print_credentials_owner_id_not_empty", "owner_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_print_credentials_revoked_at_valid", "revoked_at IS NULL OR revoked_at = changed_at");
                    table.CheckConstraint("ck_credits_print_credentials_version_positive", "version > 0");
                    table.ForeignKey(
                        name: "fk_credits_print_credentials_access_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "access",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_credits_print_credentials_normalized_email",
                schema: "credits",
                table: "print_credentials",
                column: "normalized_email",
                unique: true,
                filter: "revoked_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "print_credentials",
                schema: "credits");
        }
    }
}
