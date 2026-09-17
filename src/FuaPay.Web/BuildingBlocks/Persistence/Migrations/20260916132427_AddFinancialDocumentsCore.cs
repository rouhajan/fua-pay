using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialDocumentsCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "financial_documents");

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "financial_documents",
                columns: table => new
                {
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_number = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    document_type = table.Column<int>(type: "integer", nullable: false),
                    source_type = table.Column<int>(type: "integer", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    customer_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    financial_event_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    settlement_method = table.Column<int>(type: "integer", nullable: false),
                    issuer_legal_name = table.Column<string>(type: "text", nullable: true),
                    issuer_unit_name = table.Column<string>(type: "text", nullable: true),
                    issuer_address_line1 = table.Column<string>(type: "text", nullable: true),
                    issuer_address_line2 = table.Column<string>(type: "text", nullable: true),
                    issuer_country = table.Column<string>(type: "text", nullable: true),
                    issuer_registration_number = table.Column<string>(type: "text", nullable: true),
                    issuer_vat_number = table.Column<string>(type: "text", nullable: true),
                    issuer_contact_email = table.Column<string>(type: "text", nullable: true),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    provider_reference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    provider_order_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    job_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    job_description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    service_unit_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    render_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_documents_documents", x => x.document_id);
                    table.CheckConstraint("ck_financial_documents_amount_positive", "amount_minor_units > 0");
                    table.CheckConstraint("ck_financial_documents_currency_valid", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_financial_documents_customer_email_not_empty", "customer_email IS NULL OR length(btrim(customer_email)) > 0");
                    table.CheckConstraint("ck_financial_documents_customer_id_not_empty", "customer_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_financial_documents_customer_name_not_empty", "length(btrim(customer_display_name)) > 0");
                    table.CheckConstraint("ck_financial_documents_document_type_valid", "document_type IN (1, 2, 3)");
                    table.CheckConstraint("ck_financial_documents_id_not_empty", "document_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_financial_documents_issuer_snapshot_consistent", "(issuer_legal_name IS NULL AND issuer_unit_name IS NULL AND issuer_address_line1 IS NULL AND issuer_address_line2 IS NULL AND issuer_country IS NULL AND issuer_registration_number IS NULL AND issuer_vat_number IS NULL AND issuer_contact_email IS NULL) OR (issuer_legal_name IS NOT NULL AND issuer_unit_name IS NOT NULL AND issuer_address_line1 IS NOT NULL AND issuer_address_line2 IS NOT NULL AND issuer_country IS NOT NULL AND issuer_registration_number IS NOT NULL AND issuer_vat_number IS NOT NULL AND issuer_contact_email IS NOT NULL AND length(btrim(issuer_legal_name)) > 0 AND length(btrim(issuer_unit_name)) > 0 AND length(btrim(issuer_address_line1)) > 0 AND length(btrim(issuer_address_line2)) > 0 AND length(btrim(issuer_country)) > 0 AND length(btrim(issuer_registration_number)) > 0 AND length(btrim(issuer_vat_number)) > 0 AND length(btrim(issuer_contact_email)) > 0)");
                    table.CheckConstraint("ck_financial_documents_job_snapshot_consistent", "(job_id IS NULL AND job_number IS NULL AND job_title IS NULL AND job_description IS NULL AND service_unit_name IS NULL) OR (job_id IS NOT NULL AND job_id <> '00000000-0000-0000-0000-000000000000'::uuid AND job_number IS NOT NULL AND job_title IS NOT NULL AND job_description IS NOT NULL AND service_unit_name IS NOT NULL AND length(btrim(job_number)) > 0 AND length(btrim(job_title)) > 0 AND length(btrim(job_description)) > 0 AND length(btrim(service_unit_name)) > 0)");
                    table.CheckConstraint("ck_financial_documents_number_valid", "document_number ~ '^FUA-[0-9]{4}-[0-9]{6}$'");
                    table.CheckConstraint("ck_financial_documents_number_year_matches_issued_at", "substring(document_number from 5 for 4)::integer = EXTRACT(YEAR FROM issued_at AT TIME ZONE 'Europe/Prague')::integer");
                    table.CheckConstraint("ck_financial_documents_provider_order_not_empty", "provider_order_number IS NULL OR length(btrim(provider_order_number)) > 0");
                    table.CheckConstraint("ck_financial_documents_provider_reference_not_empty", "provider_reference IS NULL OR length(btrim(provider_reference)) > 0");
                    table.CheckConstraint("ck_financial_documents_settlement_valid", "settlement_method IN (1, 2)");
                    table.CheckConstraint("ck_financial_documents_source_id_not_empty", "source_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_financial_documents_source_type_valid", "source_type IN (1, 2)");
                    table.CheckConstraint("ck_financial_documents_timestamps_ordered", "issued_at >= financial_event_at");
                    table.CheckConstraint("ck_financial_documents_type_source_consistent", "(document_type = 1 AND source_type = 1 AND settlement_method = 1 AND provider IS NULL AND provider_reference IS NULL AND provider_order_number IS NULL AND job_id IS NULL AND job_number IS NULL AND job_title IS NULL AND job_description IS NULL AND service_unit_name IS NULL) OR (document_type = 2 AND source_type = 2 AND settlement_method = 2 AND provider IS NOT NULL AND length(btrim(provider)) > 0 AND job_id IS NULL AND job_number IS NULL AND job_title IS NULL AND job_description IS NULL AND service_unit_name IS NULL) OR (document_type = 3 AND source_type = 2 AND settlement_method = 2 AND provider IS NOT NULL AND length(btrim(provider)) > 0 AND job_id IS NOT NULL)");
                    table.CheckConstraint("ck_financial_documents_versions_positive", "schema_version > 0 AND render_version > 0");
                });

            migrationBuilder.CreateTable(
                name: "number_counters",
                schema: "financial_documents",
                columns: table => new
                {
                    business_year = table.Column<int>(type: "integer", nullable: false),
                    last_value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_documents_number_counters", x => x.business_year);
                    table.CheckConstraint("ck_financial_documents_counters_value_valid", "last_value BETWEEN 1 AND 999999");
                    table.CheckConstraint("ck_financial_documents_counters_year_valid", "business_year BETWEEN 2000 AND 9999");
                });

            migrationBuilder.CreateIndex(
                name: "uq_financial_documents_document_number",
                schema: "financial_documents",
                table: "documents",
                column: "document_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_financial_documents_source",
                schema: "financial_documents",
                table: "documents",
                columns: new[] { "source_type", "source_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documents",
                schema: "financial_documents");

            migrationBuilder.DropTable(
                name: "number_counters",
                schema: "financial_documents");
        }
    }
}
