using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforcePrintReservationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_credits_print_reservations_state_consistent",
                schema: "credits",
                table: "print_reservations");

            migrationBuilder.AddCheckConstraint(
                name: "ck_credits_print_reservations_state_consistent",
                schema: "credits",
                table: "print_reservations",
                sql: "(\n    status = 1\n    AND resolution_command_id IS NULL\n    AND terminal_command_id IS NULL\n    AND debit_operation_id IS NULL\n)\nOR\n(\n    status = 2\n    AND resolution_command_id IS NOT NULL\n    AND terminal_command_id IS NULL\n    AND debit_operation_id IS NULL\n)\nOR\n(\n    status = 3\n    AND terminal_command_id IS NOT NULL\n    AND debit_operation_id IS NOT NULL\n)\nOR\n(\n    status = 4\n    AND terminal_command_id IS NOT NULL\n    AND debit_operation_id IS NULL\n)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_credits_print_reservations_state_consistent",
                schema: "credits",
                table: "print_reservations");

            migrationBuilder.AddCheckConstraint(
                name: "ck_credits_print_reservations_state_consistent",
                schema: "credits",
                table: "print_reservations",
                sql: "(\n    status = 1\n    AND resolution_command_id IS NULL\n    AND terminal_command_id IS NULL\n    AND debit_operation_id IS NULL\n)\nOR\n(\n    status = 2\n    AND debit_operation_id IS NULL\n)\nOR\n(\n    status = 3\n    AND terminal_command_id IS NOT NULL\n    AND debit_operation_id IS NOT NULL\n)\nOR\n(\n    status = 4\n    AND terminal_command_id IS NOT NULL\n    AND debit_operation_id IS NULL\n)");
        }
    }
}
