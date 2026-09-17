using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialDocumentTaxSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_documents_versions_positive",
                schema: "financial_documents",
                table: "documents");

            migrationBuilder.AddColumn<long>(
                name: "tax_base_minor_units",
                schema: "financial_documents",
                table: "documents",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "tax_treatment",
                schema: "financial_documents",
                table: "documents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "vat_amount_minor_units",
                schema: "financial_documents",
                table: "documents",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "vat_rate_basis_points",
                schema: "financial_documents",
                table: "documents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_documents_tax_snapshot_consistent",
                schema: "financial_documents",
                table: "documents",
                sql: "(schema_version = 1 AND tax_treatment IS NULL AND vat_rate_basis_points IS NULL AND tax_base_minor_units IS NULL AND vat_amount_minor_units IS NULL) OR (schema_version = 2 AND issuer_legal_name IS NOT NULL AND issuer_unit_name IS NOT NULL AND issuer_address_line1 IS NOT NULL AND issuer_address_line2 IS NOT NULL AND issuer_country IS NOT NULL AND issuer_registration_number IS NOT NULL AND issuer_vat_number IS NOT NULL AND issuer_contact_email IS NOT NULL AND tax_treatment IS NOT NULL AND vat_rate_basis_points IS NOT NULL AND tax_base_minor_units IS NOT NULL AND vat_amount_minor_units IS NOT NULL AND tax_treatment = 1 AND vat_rate_basis_points = 2100 AND tax_base_minor_units >= 0 AND vat_amount_minor_units >= 0 AND tax_base_minor_units = round(amount_minor_units::numeric * 10000 / 12100, 0)::bigint AND vat_amount_minor_units = amount_minor_units - tax_base_minor_units AND tax_base_minor_units + vat_amount_minor_units = amount_minor_units)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_documents_versions_positive",
                schema: "financial_documents",
                table: "documents",
                sql: "(schema_version = 1 AND render_version = 1) OR (schema_version = 2 AND render_version = 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_documents_tax_snapshot_consistent",
                schema: "financial_documents",
                table: "documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_documents_versions_positive",
                schema: "financial_documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "tax_base_minor_units",
                schema: "financial_documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "tax_treatment",
                schema: "financial_documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "vat_amount_minor_units",
                schema: "financial_documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "vat_rate_basis_points",
                schema: "financial_documents",
                table: "documents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_documents_versions_positive",
                schema: "financial_documents",
                table: "documents",
                sql: "schema_version > 0 AND render_version > 0");
        }
    }
}
