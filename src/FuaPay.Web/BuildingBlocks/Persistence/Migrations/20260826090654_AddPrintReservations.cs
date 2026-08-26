using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrintReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "print_reservations",
                schema: "credits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    print_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_uuid = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    reserve_command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resolution_command_id = table.Column<Guid>(type: "uuid", nullable: true),
                    terminal_command_id = table.Column<Guid>(type: "uuid", nullable: true),
                    debit_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credits_print_reservations", x => x.id);
                    table.CheckConstraint("ck_credits_print_reservations_account_not_empty", "credit_account_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_print_reservations_amount_positive", "amount_minor_units > 0");
                    table.CheckConstraint("ck_credits_print_reservations_debit_operation_not_empty", "debit_operation_id IS NULL OR debit_operation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_print_reservations_id_not_empty", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_print_reservations_job_uuid_valid", "job_uuid ~ '^urn:uuid:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' AND job_uuid <> 'urn:uuid:00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_credits_print_reservations_reserve_command_not_empty", "reserve_command_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_print_reservations_resolution_command_not_empty", "resolution_command_id IS NULL OR resolution_command_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_print_reservations_source_not_empty", "print_source_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_print_reservations_state_consistent", "(\n    status = 1\n    AND resolution_command_id IS NULL\n    AND terminal_command_id IS NULL\n    AND debit_operation_id IS NULL\n)\nOR\n(\n    status = 2\n    AND debit_operation_id IS NULL\n)\nOR\n(\n    status = 3\n    AND terminal_command_id IS NOT NULL\n    AND debit_operation_id IS NOT NULL\n)\nOR\n(\n    status = 4\n    AND terminal_command_id IS NOT NULL\n    AND debit_operation_id IS NULL\n)");
                    table.CheckConstraint("ck_credits_print_reservations_status_valid", "status IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_credits_print_reservations_terminal_command_not_empty", "terminal_command_id IS NULL OR terminal_command_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_print_reservations_timestamps_ordered", "state_changed_at >= created_at");
                    table.CheckConstraint("ck_credits_print_reservations_version_positive", "version > 0");
                    table.ForeignKey(
                        name: "fk_credits_print_reservations_account",
                        column: x => x.credit_account_id,
                        principalSchema: "credits",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_credits_print_reservations_account_status",
                schema: "credits",
                table: "print_reservations",
                columns: new[] { "credit_account_id", "status" });

            migrationBuilder.CreateIndex(
                name: "uq_credits_print_reservations_debit_operation",
                schema: "credits",
                table: "print_reservations",
                column: "debit_operation_id",
                unique: true,
                filter: "debit_operation_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_credits_print_reservations_print_job",
                schema: "credits",
                table: "print_reservations",
                columns: new[] { "print_source_id", "job_uuid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_credits_print_reservations_reserve_command",
                schema: "credits",
                table: "print_reservations",
                columns: new[] { "print_source_id", "reserve_command_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_credits_print_reservations_resolution_command",
                schema: "credits",
                table: "print_reservations",
                columns: new[] { "print_source_id", "resolution_command_id" },
                unique: true,
                filter: "resolution_command_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_credits_print_reservations_terminal_command",
                schema: "credits",
                table: "print_reservations",
                columns: new[] { "print_source_id", "terminal_command_id" },
                unique: true,
                filter: "terminal_command_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "print_reservations",
                schema: "credits");
        }
    }
}
