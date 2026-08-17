using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class CsobPaymentRecoveryConfiguration :
    IEntityTypeConfiguration<CsobPaymentRecoveryEntity>
{
    public void Configure(
        EntityTypeBuilder<CsobPaymentRecoveryEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "csob_payment_reconciliation",
            "payments",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_csob_reconciliation_payment_not_empty",
                    "payment_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_csob_reconciliation_reference_consistent",
                    "(provider_reference IS NULL AND state = 3) OR " +
                    "(provider_reference IS NOT NULL AND length(btrim(provider_reference)) > 0)");
                table.HasCheckConstraint(
                    "ck_csob_reconciliation_state_valid",
                    "state IN (1, 2, 3, 4)");
                table.HasCheckConstraint(
                    "ck_csob_reconciliation_attempt_count_valid",
                    "attempt_count >= 0");
                table.HasCheckConstraint(
                    "ck_csob_reconciliation_timestamps_ordered",
                    "updated_at >= created_at AND next_attempt_at >= created_at AND " +
                    "(last_attempt_at IS NULL OR last_attempt_at >= created_at) AND " +
                    "(last_browser_return_at IS NULL OR last_browser_return_at >= created_at) AND " +
                    "(completed_at IS NULL OR completed_at >= created_at)");
                table.HasCheckConstraint(
                    "ck_csob_reconciliation_lease_consistent",
                    "(state = 2 AND lease_token IS NOT NULL AND lease_expires_at IS NOT NULL) OR " +
                    "(state <> 2 AND lease_token IS NULL AND lease_expires_at IS NULL)");
                table.HasCheckConstraint(
                    "ck_csob_reconciliation_completion_consistent",
                    "(state = 4 AND completed_at IS NOT NULL) OR (state <> 4 AND completed_at IS NULL)");
                table.HasCheckConstraint(
                    "ck_csob_reconciliation_version_positive",
                    "version > 0");
            });

        builder.HasKey(item => item.PaymentId)
            .HasName("pk_csob_payment_reconciliation");

        builder.Property(item => item.PaymentId)
            .HasColumnName("payment_id")
            .ValueGeneratedNever();
        builder.Property(item => item.ProviderReference)
            .HasColumnName("provider_reference")
            .HasMaxLength(PaymentProviderReference.MaxLength);
        builder.Property(item => item.State)
            .HasColumnName("state")
            .IsRequired();
        builder.Property(item => item.AttemptCount)
            .HasColumnName("attempt_count")
            .IsRequired();
        builder.Property(item => item.NextAttemptAt)
            .HasColumnName("next_attempt_at")
            .IsRequired();
        builder.Property(item => item.LeaseToken)
            .HasColumnName("lease_token");
        builder.Property(item => item.LeaseExpiresAt)
            .HasColumnName("lease_expires_at");
        builder.Property(item => item.LastAttemptAt)
            .HasColumnName("last_attempt_at");
        builder.Property(item => item.LastBrowserReturnAt)
            .HasColumnName("last_browser_return_at");
        builder.Property(item => item.LastGatewayPaymentStatus)
            .HasColumnName("last_gateway_payment_status");
        builder.Property(item => item.LastResultCode)
            .HasColumnName("last_result_code");
        builder.Property(item => item.LastError)
            .HasColumnName("last_error")
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

        builder.HasOne<PaymentEntity>()
            .WithOne()
            .HasForeignKey<CsobPaymentRecoveryEntity>(
                item => item.PaymentId)
            .HasConstraintName("fk_csob_reconciliation_payment")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(
                item => new
                {
                    item.State,
                    item.NextAttemptAt
                })
            .HasDatabaseName("ix_csob_reconciliation_due");

        builder.HasIndex(item => item.ProviderReference)
            .IsUnique()
            .HasFilter("provider_reference IS NOT NULL")
            .HasDatabaseName("uq_csob_reconciliation_reference");
    }
}
