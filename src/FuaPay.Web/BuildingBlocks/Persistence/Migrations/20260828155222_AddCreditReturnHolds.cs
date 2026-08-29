using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditReturnHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "return_holds",
                schema: "credits",
                columns: table => new
                {
                    settlement_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credits_return_holds", x => x.settlement_return_id);
                    table.CheckConstraint("ck_credits_return_holds_account_not_empty", "credit_account_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_return_holds_amount_positive", "amount_minor_units > 0");
                    table.CheckConstraint("ck_credits_return_holds_return_not_empty", "settlement_return_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_return_holds_state_valid", "state IN (1, 2, 3)");
                    table.CheckConstraint("ck_credits_return_holds_timestamps_ordered", "state_changed_at >= created_at");
                    table.CheckConstraint("ck_credits_return_holds_version_positive", "version > 0");
                    table.ForeignKey(
                        name: "fk_credits_return_holds_account",
                        column: x => x.credit_account_id,
                        principalSchema: "credits",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_credits_return_holds_settlement_return",
                        column: x => x.settlement_return_id,
                        principalSchema: "payments",
                        principalTable: "settlement_returns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_credits_return_holds_account_state",
                schema: "credits",
                table: "return_holds",
                columns: new[] { "credit_account_id", "state" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "return_holds",
                schema: "credits");
        }
    }
}
