using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class PaymentConfiguration :
    IEntityTypeConfiguration<PaymentEntity>
{
    internal const string PrimaryKeyConstraint =
        "pk_payments_payments";

    internal const string ProviderReferenceUniqueConstraint =
        "uq_payments_provider_reference";

    internal const string BlockingJobUniqueConstraint =
        "uq_payments_blocking_job";

    internal const string CreationRequestUniqueConstraint =
        "uq_payments_creation_request";

    public void Configure(EntityTypeBuilder<PaymentEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "payments",
            "payments",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_payments_id_not_empty",
                    "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_payments_customer_not_empty",
                    "customer_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_payments_purpose_valid",
                    "purpose_type IN (1, 2)");
                table.HasCheckConstraint(
                    "ck_payments_provider_valid",
                    "provider IN (1, 2)");
                table.HasCheckConstraint(
                    "ck_payments_status_valid",
                    "status IN (1, 2, 3, 4, 5, 6)");
                table.HasCheckConstraint(
                    "ck_payments_amount_positive",
                    "amount_minor_units > 0");
                table.HasCheckConstraint(
                    "ck_payments_purpose_consistent",
                    "(purpose_type = 1 AND job_id IS NULL) OR (purpose_type = 2 AND job_id IS NOT NULL AND job_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
                table.HasCheckConstraint(
                    "ck_payments_creation_request_consistent",
                    "(purpose_type = 1 AND creation_request_id IS NOT NULL AND creation_request_id <> '00000000-0000-0000-0000-000000000000'::uuid) OR (purpose_type = 2 AND creation_request_id IS NULL)");
                table.HasCheckConstraint(
                    "ck_payments_timestamps_ordered",
                    "updated_at >= created_at AND (completed_at IS NULL OR completed_at >= created_at)");
                table.HasCheckConstraint(
                    "ck_payments_completion_consistent",
                    "(status IN (1, 2) AND completed_at IS NULL) OR (status IN (3, 4, 5, 6) AND completed_at IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_payments_failure_consistent",
                    "(status = 4 AND failure_reason IS NOT NULL AND length(btrim(failure_reason)) > 0) OR (status <> 4 AND failure_reason IS NULL)");
                table.HasCheckConstraint(
                    "ck_payments_provider_reference_consistent",
                    "(status = 1 AND provider_reference IS NULL) OR (status <> 1 AND provider_reference IS NOT NULL AND length(btrim(provider_reference)) > 0)");
                table.HasCheckConstraint(
                    "ck_payments_version_positive",
                    "version > 0");
            });

        builder.HasKey(item => item.Id)
            .HasName(PrimaryKeyConstraint);

        builder.Property(item => item.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(item => item.CustomerUserId)
            .HasColumnName("customer_user_id")
            .IsRequired();
        builder.Property(item => item.PurposeType)
            .HasColumnName("purpose_type")
            .IsRequired();
        builder.Property(item => item.JobId)
            .HasColumnName("job_id");
        builder.Property(item => item.AmountMinorUnits)
            .HasColumnName("amount_minor_units")
            .IsRequired();
        builder.Property(item => item.Provider)
            .HasColumnName("provider")
            .IsRequired();
        builder.Property(item => item.CreationRequestId)
            .HasColumnName("creation_request_id");
        builder.Property(item => item.Status)
            .HasColumnName("status")
            .IsRequired();
        builder.Property(item => item.ProviderReference)
            .HasColumnName("provider_reference")
            .HasMaxLength(PaymentProviderReference.MaxLength);
        builder.Property(item => item.FailureReason)
            .HasColumnName("failure_reason")
            .HasMaxLength(500);
        builder.Property(item => item.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
        builder.Property(item => item.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
        builder.Property(item => item.CompletedAt)
            .HasColumnName("completed_at");
        builder.Property(item => item.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(
                item => new
                {
                    item.Provider,
                    item.ProviderReference
                })
            .IsUnique()
            .HasFilter("provider_reference IS NOT NULL")
            .HasDatabaseName(ProviderReferenceUniqueConstraint);

        builder.HasIndex(item => item.CreationRequestId)
            .IsUnique()
            .HasFilter("creation_request_id IS NOT NULL")
            .HasDatabaseName(CreationRequestUniqueConstraint);

        var blockingStatusValues = string.Join(
            ", ",
            JobPaymentBlockingPolicy.Statuses.Select(
                status => ((int)status).ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));

        builder.HasIndex(item => item.JobId)
            .IsUnique()
            .HasFilter(
                $"job_id IS NOT NULL AND status IN ({blockingStatusValues})")
            .HasDatabaseName(BlockingJobUniqueConstraint);

        builder.HasIndex(
                item => new
                {
                    item.CustomerUserId,
                    item.CreatedAt
                })
            .HasDatabaseName("ix_payments_customer_created_at");

        builder.HasIndex(
                item => new
                {
                    item.Status,
                    item.CreatedAt
                })
            .HasDatabaseName("ix_payments_status_created_at");

        builder.HasIndex(
                item => new
                {
                    item.UpdatedAt,
                    item.Id
                })
            .HasFilter(
                "provider = 2 AND status = 2 AND provider_reference IS NOT NULL")
            .HasDatabaseName("ix_payments_csob_pending_long_open");
    }
}
