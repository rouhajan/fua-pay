using FuaPay.Web.Modules.FinancialDocuments.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.FinancialDocuments.Infrastructure.Persistence;

internal sealed class FinancialDocumentConfiguration :
    IEntityTypeConfiguration<FinancialDocumentEntity>
{
    internal const string PrimaryKeyConstraint =
        "pk_financial_documents_documents";

    internal const string DocumentNumberUniqueConstraint =
        "uq_financial_documents_document_number";

    internal const string SourceUniqueConstraint =
        "uq_financial_documents_source";

    public void Configure(
        EntityTypeBuilder<FinancialDocumentEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "documents",
            "financial_documents",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_financial_documents_id_not_empty",
                    "document_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_financial_documents_number_valid",
                    "document_number ~ '^FUA-[0-9]{4}-[0-9]{6}$'");
                table.HasCheckConstraint(
                    "ck_financial_documents_number_year_matches_issued_at",
                    "substring(document_number from 5 for 4)::integer = " +
                    "EXTRACT(YEAR FROM issued_at AT TIME ZONE 'Europe/Prague')::integer");
                table.HasCheckConstraint(
                    "ck_financial_documents_document_type_valid",
                    "document_type IN (1, 2, 3)");
                table.HasCheckConstraint(
                    "ck_financial_documents_source_type_valid",
                    "source_type IN (1, 2)");
                table.HasCheckConstraint(
                    "ck_financial_documents_source_id_not_empty",
                    "source_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_financial_documents_customer_id_not_empty",
                    "customer_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_financial_documents_customer_name_not_empty",
                    "length(btrim(customer_display_name)) > 0");
                table.HasCheckConstraint(
                    "ck_financial_documents_customer_email_not_empty",
                    "customer_email IS NULL OR length(btrim(customer_email)) > 0");
                table.HasCheckConstraint(
                    "ck_financial_documents_amount_positive",
                    "amount_minor_units > 0");
                table.HasCheckConstraint(
                    "ck_financial_documents_currency_valid",
                    "currency ~ '^[A-Z]{3}$'");
                table.HasCheckConstraint(
                    "ck_financial_documents_timestamps_ordered",
                    "issued_at >= financial_event_at");
                table.HasCheckConstraint(
                    "ck_financial_documents_settlement_valid",
                    "settlement_method IN (1, 2)");
                table.HasCheckConstraint(
                    "ck_financial_documents_type_source_consistent",
                    "(document_type = 1 AND source_type = 1 AND " +
                    "settlement_method = 1 AND provider IS NULL AND " +
                    "provider_reference IS NULL AND provider_order_number IS NULL AND " +
                    "job_id IS NULL AND job_number IS NULL AND job_title IS NULL AND " +
                    "job_description IS NULL AND service_unit_name IS NULL) OR " +
                    "(document_type = 2 AND source_type = 2 AND " +
                    "settlement_method = 2 AND provider IS NOT NULL AND " +
                    "length(btrim(provider)) > 0 AND job_id IS NULL AND " +
                    "job_number IS NULL AND job_title IS NULL AND " +
                    "job_description IS NULL AND service_unit_name IS NULL) OR " +
                    "(document_type = 3 AND source_type = 2 AND " +
                    "settlement_method = 2 AND provider IS NOT NULL AND " +
                    "length(btrim(provider)) > 0 AND job_id IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_financial_documents_issuer_snapshot_consistent",
                    "(issuer_legal_name IS NULL AND issuer_unit_name IS NULL AND " +
                    "issuer_address_line1 IS NULL AND issuer_address_line2 IS NULL AND " +
                    "issuer_country IS NULL AND issuer_registration_number IS NULL AND " +
                    "issuer_vat_number IS NULL AND issuer_contact_email IS NULL) OR " +
                    "(issuer_legal_name IS NOT NULL AND issuer_unit_name IS NOT NULL AND " +
                    "issuer_address_line1 IS NOT NULL AND issuer_address_line2 IS NOT NULL AND " +
                    "issuer_country IS NOT NULL AND issuer_registration_number IS NOT NULL AND " +
                    "issuer_vat_number IS NOT NULL AND issuer_contact_email IS NOT NULL AND " +
                    "length(btrim(issuer_legal_name)) > 0 AND " +
                    "length(btrim(issuer_unit_name)) > 0 AND " +
                    "length(btrim(issuer_address_line1)) > 0 AND " +
                    "length(btrim(issuer_address_line2)) > 0 AND " +
                    "length(btrim(issuer_country)) > 0 AND " +
                    "length(btrim(issuer_registration_number)) > 0 AND " +
                    "length(btrim(issuer_vat_number)) > 0 AND " +
                    "length(btrim(issuer_contact_email)) > 0)");
                table.HasCheckConstraint(
                    "ck_financial_documents_provider_reference_not_empty",
                    "provider_reference IS NULL OR length(btrim(provider_reference)) > 0");
                table.HasCheckConstraint(
                    "ck_financial_documents_provider_order_not_empty",
                    "provider_order_number IS NULL OR length(btrim(provider_order_number)) > 0");
                table.HasCheckConstraint(
                    "ck_financial_documents_job_snapshot_consistent",
                    "(job_id IS NULL AND job_number IS NULL AND " +
                    "job_title IS NULL AND job_description IS NULL AND " +
                    "service_unit_name IS NULL) OR " +
                    "(job_id IS NOT NULL AND job_id <> " +
                    "'00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "job_number IS NOT NULL AND job_title IS NOT NULL AND " +
                    "job_description IS NOT NULL AND service_unit_name IS NOT NULL AND " +
                    "length(btrim(job_number)) > 0 AND " +
                    "length(btrim(job_title)) > 0 AND " +
                    "length(btrim(job_description)) > 0 AND " +
                    "length(btrim(service_unit_name)) > 0)");
                table.HasCheckConstraint(
                    "ck_financial_documents_versions_positive",
                    "schema_version > 0 AND render_version > 0");
            });

        builder.HasKey(item => item.DocumentId)
            .HasName(PrimaryKeyConstraint);

        builder.Property(item => item.DocumentId)
            .HasColumnName("document_id")
            .ValueGeneratedNever();
        builder.Property(item => item.DocumentNumber)
            .HasColumnName("document_number")
            .HasMaxLength(15)
            .IsRequired();
        builder.Property(item => item.DocumentType)
            .HasColumnName("document_type")
            .IsRequired();
        builder.Property(item => item.SourceType)
            .HasColumnName("source_type")
            .IsRequired();
        builder.Property(item => item.SourceId)
            .HasColumnName("source_id")
            .IsRequired();
        builder.Property(item => item.CustomerUserId)
            .HasColumnName("customer_user_id")
            .IsRequired();
        builder.Property(item => item.CustomerDisplayName)
            .HasColumnName("customer_display_name")
            .HasMaxLength(FinancialDocumentText.CustomerDisplayNameMaxLength)
            .IsRequired();
        builder.Property(item => item.CustomerEmail)
            .HasColumnName("customer_email")
            .HasMaxLength(FinancialDocumentText.CustomerEmailMaxLength);
        builder.Property(item => item.AmountMinorUnits)
            .HasColumnName("amount_minor_units")
            .IsRequired();
        builder.Property(item => item.Currency)
            .HasColumnName("currency")
            .HasMaxLength(FinancialDocumentText.CurrencyMaxLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(item => item.FinancialEventAt)
            .HasColumnName("financial_event_at")
            .IsRequired();
        builder.Property(item => item.IssuedAt)
            .HasColumnName("issued_at")
            .IsRequired();
        builder.Property(item => item.SettlementMethod)
            .HasColumnName("settlement_method")
            .IsRequired();
        builder.Property(item => item.IssuerLegalName)
            .HasColumnName("issuer_legal_name")
            .HasColumnType("text");
        builder.Property(item => item.IssuerUnitName)
            .HasColumnName("issuer_unit_name")
            .HasColumnType("text");
        builder.Property(item => item.IssuerAddressLine1)
            .HasColumnName("issuer_address_line1")
            .HasColumnType("text");
        builder.Property(item => item.IssuerAddressLine2)
            .HasColumnName("issuer_address_line2")
            .HasColumnType("text");
        builder.Property(item => item.IssuerCountry)
            .HasColumnName("issuer_country")
            .HasColumnType("text");
        builder.Property(item => item.IssuerRegistrationNumber)
            .HasColumnName("issuer_registration_number")
            .HasColumnType("text");
        builder.Property(item => item.IssuerVatNumber)
            .HasColumnName("issuer_vat_number")
            .HasColumnType("text");
        builder.Property(item => item.IssuerContactEmail)
            .HasColumnName("issuer_contact_email")
            .HasColumnType("text");
        builder.Property(item => item.Provider)
            .HasColumnName("provider")
            .HasMaxLength(FinancialDocumentText.ProviderMaxLength);
        builder.Property(item => item.ProviderReference)
            .HasColumnName("provider_reference")
            .HasMaxLength(FinancialDocumentText.ProviderReferenceMaxLength);
        builder.Property(item => item.ProviderOrderNumber)
            .HasColumnName("provider_order_number")
            .HasMaxLength(FinancialDocumentText.ProviderOrderNumberMaxLength);
        builder.Property(item => item.JobId)
            .HasColumnName("job_id");
        builder.Property(item => item.JobNumber)
            .HasColumnName("job_number")
            .HasMaxLength(FinancialDocumentText.JobNumberMaxLength);
        builder.Property(item => item.JobTitle)
            .HasColumnName("job_title")
            .HasMaxLength(FinancialDocumentText.JobTitleMaxLength);
        builder.Property(item => item.JobDescription)
            .HasColumnName("job_description")
            .HasMaxLength(FinancialDocumentText.JobDescriptionMaxLength);
        builder.Property(item => item.ServiceUnitName)
            .HasColumnName("service_unit_name")
            .HasMaxLength(FinancialDocumentText.ServiceUnitNameMaxLength);
        builder.Property(item => item.SchemaVersion)
            .HasColumnName("schema_version")
            .IsRequired();
        builder.Property(item => item.RenderVersion)
            .HasColumnName("render_version")
            .IsRequired();

        builder.HasIndex(item => item.DocumentNumber)
            .IsUnique()
            .HasDatabaseName(DocumentNumberUniqueConstraint);

        builder.HasIndex(
                item => new
                {
                    item.SourceType,
                    item.SourceId
                })
            .IsUnique()
            .HasDatabaseName(SourceUniqueConstraint);
    }
}
