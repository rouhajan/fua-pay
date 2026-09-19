using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLegacySafeQCreditTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "legacy_safeq_credit_transfers",
                schema: "credits",
                columns: table => new
                {
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    administrator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    safeq_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    snapshot_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credits_legacy_safeq_credit_transfers", x => x.command_id);
                    table.CheckConstraint("ck_credits_legacy_safeq_transfers_administrator_not_empty", "administrator_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_legacy_safeq_transfers_amount_allowed", "amount_minor_units BETWEEN 1 AND 10000000");
                    table.CheckConstraint("ck_credits_legacy_safeq_transfers_command_not_empty", "command_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_legacy_safeq_transfers_owner_not_empty", "owner_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_legacy_safeq_transfers_safeq_user_not_empty", "length(btrim(safeq_user_id)) > 0");
                    table.CheckConstraint("ck_credits_legacy_safeq_transfers_snapshot_sha256", "snapshot_sha256 ~ '^[0-9A-F]{64}$'");
                });

            migrationBuilder.CreateIndex(
                name: "ux_credits_legacy_safeq_credit_transfers_safeq_user_id",
                schema: "credits",
                table: "legacy_safeq_credit_transfers",
                column: "safeq_user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "legacy_safeq_credit_transfers",
                schema: "credits");
        }
    }
}
