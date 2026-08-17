using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddM1FinancialCommandIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "uq_payments_open_job",
                schema: "payments",
                table: "payments",
                newName: "uq_payments_blocking_job");

            migrationBuilder.AddColumn<Guid>(
                name: "creation_request_id",
                schema: "payments",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE payments.payments " +
                "SET creation_request_id = id " +
                "WHERE purpose_type = 1;");

            migrationBuilder.CreateTable(
                name: "adjustment_commands",
                schema: "credits",
                columns: table => new
                {
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    administrator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signed_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credits_adjustment_commands", x => x.command_id);
                    table.CheckConstraint("ck_credits_adjustment_commands_administrator_not_empty", "administrator_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_adjustment_commands_amount_allowed", "signed_amount_minor_units <> 0 AND signed_amount_minor_units BETWEEN -10000000 AND 10000000");
                    table.CheckConstraint("ck_credits_adjustment_commands_id_not_empty", "command_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_adjustment_commands_owner_not_empty", "owner_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_adjustment_commands_reason_not_empty", "length(btrim(reason)) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "uq_payments_creation_request",
                schema: "payments",
                table: "payments",
                column: "creation_request_id",
                unique: true,
                filter: "creation_request_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_creation_request_consistent",
                schema: "payments",
                table: "payments",
                sql: "(purpose_type = 1 AND creation_request_id IS NOT NULL AND creation_request_id <> '00000000-0000-0000-0000-000000000000'::uuid) OR (purpose_type = 2 AND creation_request_id IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "adjustment_commands",
                schema: "credits");

            migrationBuilder.DropIndex(
                name: "uq_payments_creation_request",
                schema: "payments",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_creation_request_consistent",
                schema: "payments",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "creation_request_id",
                schema: "payments",
                table: "payments");

            migrationBuilder.RenameIndex(
                name: "uq_payments_blocking_job",
                schema: "payments",
                table: "payments",
                newName: "uq_payments_open_job");
        }
    }
}
