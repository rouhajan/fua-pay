using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManualTopUpFinancialDocumentCutover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "financial_document_required",
                schema: "credits",
                table: "manual_topup_commands",
                type: "boolean",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE credits.manual_topup_commands
                SET financial_document_required = FALSE
                WHERE financial_document_required IS NULL;
                """);

            migrationBuilder.AlterColumn<bool>(
                name: "financial_document_required",
                schema: "credits",
                table: "manual_topup_commands",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "financial_document_required",
                schema: "credits",
                table: "manual_topup_commands");
        }
    }
}
