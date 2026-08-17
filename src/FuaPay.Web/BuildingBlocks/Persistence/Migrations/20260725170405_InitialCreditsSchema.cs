using System;

using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreditsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "credits");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "credits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    balance_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credits_accounts", x => x.id);
                    table.CheckConstraint("ck_credits_accounts_balance_non_negative", "balance_minor_units >= 0");
                    table.CheckConstraint("ck_credits_accounts_id_not_empty", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_accounts_owner_not_empty", "owner_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_accounts_version_positive", "version > 0");
                });

            migrationBuilder.CreateTable(
                name: "movements",
                schema: "credits",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_type = table.Column<int>(type: "integer", nullable: false),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    balance_after_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credits_movements", x => x.id);
                    table.CheckConstraint("ck_credits_movements_account_not_empty", "account_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_movements_amount_positive", "amount_minor_units > 0");
                    table.CheckConstraint("ck_credits_movements_balance_non_negative", "balance_after_minor_units >= 0");
                    table.CheckConstraint("ck_credits_movements_description_not_empty", "length(btrim(description)) > 0");
                    table.CheckConstraint("ck_credits_movements_operation_not_empty", "operation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_credits_movements_sequence_positive", "sequence > 0");
                    table.CheckConstraint("ck_credits_movements_type_valid", "movement_type IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_movements_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "credits",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_credits_accounts_owner",
                schema: "credits",
                table: "accounts",
                column: "owner_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credits_movements_account_recorded_at",
                schema: "credits",
                table: "movements",
                columns: new[] { "account_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "uq_credits_movements_account_sequence",
                schema: "credits",
                table: "movements",
                columns: new[] { "account_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_credits_movements_operation",
                schema: "credits",
                table: "movements",
                column: "operation_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "movements",
                schema: "credits");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "credits");
        }
    }
}
