using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManualCreditTopUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "manual_topup_commands",
                schema: "credits",
                columns: table => new
                {
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    administrator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credits_manual_topup_commands", x => x.command_id);
                    table.CheckConstraint("ck_credits_manual_topup_commands_administrator_not_empty", "administrator_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_manual_topup_commands_amount_allowed", "amount_minor_units BETWEEN 1 AND 10000000");
                    table.CheckConstraint("ck_credits_manual_topup_commands_id_not_empty", "command_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_manual_topup_commands_note_not_empty", "length(btrim(note)) > 0");
                    table.CheckConstraint("ck_credits_manual_topup_commands_owner_not_empty", "owner_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "manual_topup_commands",
                schema: "credits");
        }
    }
}
