using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class PaymentInitiationConfiguration :
    IEntityTypeConfiguration<PaymentInitiationEntity>
{
    internal const string PrimaryKeyConstraint =
        "pk_payments_payment_initiations";

    internal const string OrderNumberUniqueConstraint =
        "uq_payments_payment_initiations_order_number";

    internal const string CorrelationUniqueConstraint =
        "uq_payments_payment_initiations_correlation";

    public void Configure(
        EntityTypeBuilder<PaymentInitiationEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "payment_initiations",
            "payments",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_payments_initiations_payment_not_empty",
                    "payment_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_payments_initiations_provider_valid",
                    "provider IN (1, 2)");
                table.HasCheckConstraint(
                    "ck_payments_initiations_order_number_valid",
                    $"order_number BETWEEN 1 AND {PaymentInitiation.MaximumOrderNumber}");
                table.HasCheckConstraint(
                    "ck_payments_initiations_correlation_not_empty",
                    "correlation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_payments_initiations_state_valid",
                    "state IN (1, 2, 3, 4)");
                table.HasCheckConstraint(
                    "ck_payments_initiations_timestamps_ordered",
                    "updated_at >= created_at AND (started_at IS NULL OR started_at >= created_at) AND (finished_at IS NULL OR (started_at IS NOT NULL AND finished_at >= started_at))");
                table.HasCheckConstraint(
                    "ck_payments_initiations_state_consistent",
                    "(state = 1 AND started_at IS NULL AND finished_at IS NULL) OR " +
                    "(state = 2 AND started_at IS NOT NULL AND finished_at IS NULL) OR " +
                    "(state IN (3, 4) AND started_at IS NOT NULL AND finished_at IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_payments_initiations_error_consistent",
                    "(state = 4 AND last_error IS NOT NULL AND length(btrim(last_error)) > 0) OR (state <> 4 AND last_error IS NULL)");
                table.HasCheckConstraint(
                    "ck_payments_initiations_process_uri_consistent",
                    "(state = 3) OR process_uri IS NULL");
                table.HasCheckConstraint(
                    "ck_payments_initiations_observation_consistent",
                    "(state <> 4 AND observed_provider_reference IS NULL AND observed_process_uri IS NULL) OR " +
                    "(state = 4 AND (observed_process_uri IS NULL OR observed_provider_reference IS NOT NULL))");
                table.HasCheckConstraint(
                    "ck_payments_initiations_version_positive",
                    "version > 0");
            });

        builder.HasKey(item => item.PaymentId)
            .HasName(PrimaryKeyConstraint);

        builder.Property(item => item.PaymentId)
            .HasColumnName("payment_id")
            .ValueGeneratedNever();
        builder.Property(item => item.Provider)
            .HasColumnName("provider")
            .IsRequired();
        builder.Property(item => item.OrderNumber)
            .HasColumnName("order_number")
            .IsRequired();
        builder.Property(item => item.CorrelationId)
            .HasColumnName("correlation_id")
            .IsRequired();
        builder.Property(item => item.State)
            .HasColumnName("state")
            .IsRequired();
        builder.Property(item => item.LastError)
            .HasColumnName("last_error")
            .HasMaxLength(PaymentInitiation.MaximumErrorLength);
        builder.Property(item => item.ProcessUri)
            .HasColumnName("process_uri")
            .HasMaxLength(PaymentInitiation.MaximumProcessUriLength);
        builder.Property(item => item.ObservedProviderReference)
            .HasColumnName("observed_provider_reference")
            .HasMaxLength(PaymentProviderReference.MaxLength);
        builder.Property(item => item.ObservedProcessUri)
            .HasColumnName("observed_process_uri")
            .HasMaxLength(PaymentInitiation.MaximumProcessUriLength);
        builder.Property(item => item.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
        builder.Property(item => item.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
        builder.Property(item => item.StartedAt)
            .HasColumnName("started_at");
        builder.Property(item => item.FinishedAt)
            .HasColumnName("finished_at");
        builder.Property(item => item.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasOne<PaymentEntity>()
            .WithOne()
            .HasForeignKey<PaymentInitiationEntity>(item => item.PaymentId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_payments_initiations_payment");

        builder.HasIndex(item => item.OrderNumber)
            .IsUnique()
            .HasDatabaseName(OrderNumberUniqueConstraint);

        builder.HasIndex(item => item.CorrelationId)
            .IsUnique()
            .HasDatabaseName(CorrelationUniqueConstraint);

        builder.HasIndex(
                item => new
                {
                    item.State,
                    item.UpdatedAt
                })
            .HasDatabaseName("ix_payments_initiations_state_updated_at");
    }
}
