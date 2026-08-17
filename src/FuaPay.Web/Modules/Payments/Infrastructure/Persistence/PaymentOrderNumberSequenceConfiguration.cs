using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class PaymentOrderNumberSequenceConfiguration :
    IEntityTypeConfiguration<PaymentOrderNumberSequenceEntity>
{
    public void Configure(
        EntityTypeBuilder<PaymentOrderNumberSequenceEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "order_number_sequence",
            "payments",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_payments_order_number_sequence_singleton",
                    "id = 1");
                table.HasCheckConstraint(
                    "ck_payments_order_number_sequence_value_valid",
                    $"last_value BETWEEN 1 AND {PaymentInitiation.MaximumOrderNumber}");
            });

        builder.HasKey(item => item.Id)
            .HasName("pk_payments_order_number_sequence");

        builder.Property(item => item.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(item => item.LastValue)
            .HasColumnName("last_value")
            .IsRequired();
    }
}
