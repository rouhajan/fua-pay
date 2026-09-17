using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.FinancialDocuments.Infrastructure.Persistence;

internal sealed class FinancialDocumentNumberCounterConfiguration :
    IEntityTypeConfiguration<FinancialDocumentNumberCounterEntity>
{
    public void Configure(
        EntityTypeBuilder<FinancialDocumentNumberCounterEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "number_counters",
            "financial_documents",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_financial_documents_counters_year_valid",
                    "business_year BETWEEN 2000 AND 9999");
                table.HasCheckConstraint(
                    "ck_financial_documents_counters_value_valid",
                    "last_value BETWEEN 1 AND 999999");
            });

        builder.HasKey(item => item.BusinessYear)
            .HasName("pk_financial_documents_number_counters");

        builder.Property(item => item.BusinessYear)
            .HasColumnName("business_year")
            .ValueGeneratedNever();
        builder.Property(item => item.LastValue)
            .HasColumnName("last_value")
            .IsRequired();
    }
}
