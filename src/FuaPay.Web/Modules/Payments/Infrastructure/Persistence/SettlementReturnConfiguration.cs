using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class SettlementReturnConfiguration :
    IEntityTypeConfiguration<SettlementReturnEntity>
{
    internal const string PrimaryKeyConstraint =
        "pk_payments_settlement_returns";

    internal const string RequestUniqueConstraint =
        "uq_payments_settlement_returns_request";

    internal const string OriginalPaymentUniqueConstraint =
        "uq_payments_settlement_returns_original_payment";

    internal const string JobUniqueConstraint =
        "uq_payments_settlement_returns_job";

    public void Configure(
        EntityTypeBuilder<SettlementReturnEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "settlement_returns",
            "payments",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_payments_settlement_returns_id_not_empty",
                    "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_payments_settlement_returns_request_not_empty",
                    "request_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_payments_settlement_returns_customer_not_empty",
                    "customer_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_payments_settlement_returns_admin_not_empty",
                    "administrator_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_payments_settlement_returns_kind_valid",
                    "kind IN (1, 2, 3)");
                table.HasCheckConstraint(
                    "ck_payments_settlement_returns_original_not_empty",
                    "original_payment_id IS NULL OR original_payment_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_payments_settlement_returns_job_not_empty",
                    "job_id IS NULL OR job_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_payments_settlement_returns_source_consistent",
                    "(kind = 1 AND original_payment_id IS NOT NULL AND job_id IS NOT NULL) OR " +
                    "(kind = 2 AND original_payment_id IS NULL AND job_id IS NOT NULL) OR " +
                    "(kind = 3 AND original_payment_id IS NOT NULL AND job_id IS NULL)");
                table.HasCheckConstraint(
                    "ck_payments_settlement_returns_amount_positive",
                    "amount_minor_units > 0");
                table.HasCheckConstraint(
                    "ck_payments_settlement_returns_currency_supported",
                    $"currency = '{Money.CurrencyCode}'");
                table.HasCheckConstraint(
                    "ck_payments_settlement_returns_reason_not_blank",
                    "length(btrim(reason)) > 0");
                table.HasCheckConstraint(
                    "ck_payments_settlement_returns_state_valid",
                    "state IN (1, 2, 3, 4, 5)");
                table.HasCheckConstraint(
                    "ck_payments_settlement_returns_timestamps_ordered",
                    "updated_at >= requested_at AND " +
                    "(started_at IS NULL OR (started_at >= requested_at AND updated_at >= started_at)) AND " +
                    "(completed_at IS NULL OR (started_at IS NOT NULL AND completed_at >= started_at AND updated_at >= completed_at))");
                table.HasCheckConstraint(
                    "ck_payments_settlement_returns_state_consistent",
                    "(state = 1 AND started_at IS NULL AND completed_at IS NULL) OR " +
                    "(state = 2 AND started_at IS NOT NULL AND completed_at IS NULL) OR " +
                    "(state IN (3, 4) AND started_at IS NOT NULL AND completed_at IS NOT NULL) OR " +
                    "(state = 5 AND started_at IS NOT NULL AND completed_at IS NULL)");
                table.HasCheckConstraint(
                    "ck_payments_settlement_returns_version_positive",
                    "version > 0");
            });

        builder.HasKey(item => item.Id)
            .HasName(PrimaryKeyConstraint);

        builder.Property(item => item.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(item => item.RequestId)
            .HasColumnName("request_id")
            .IsRequired();
        builder.Property(item => item.Kind)
            .HasColumnName("kind")
            .IsRequired();
        builder.Property(item => item.OriginalPaymentId)
            .HasColumnName("original_payment_id");
        builder.Property(item => item.JobId)
            .HasColumnName("job_id");
        builder.Property(item => item.CustomerUserId)
            .HasColumnName("customer_user_id")
            .IsRequired();
        builder.Property(item => item.AdministratorUserId)
            .HasColumnName("administrator_user_id")
            .IsRequired();
        builder.Property(item => item.AmountMinorUnits)
            .HasColumnName("amount_minor_units")
            .IsRequired();
        builder.Property(item => item.Currency)
            .HasColumnName("currency")
            .HasMaxLength(Money.CurrencyCode.Length)
            .IsRequired();
        builder.Property(item => item.Reason)
            .HasColumnName("reason")
            .HasMaxLength(SettlementReturn.MaximumReasonLength)
            .IsRequired();
        builder.Property(item => item.State)
            .HasColumnName("state")
            .IsRequired();
        builder.Property(item => item.RequestedAt)
            .HasColumnName("requested_at")
            .IsRequired();
        builder.Property(item => item.StartedAt)
            .HasColumnName("started_at");
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
            .WithMany()
            .HasForeignKey(item => item.OriginalPaymentId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_payments_settlement_returns_original_payment");

        builder.HasIndex(item => item.RequestId)
            .IsUnique()
            .HasDatabaseName(RequestUniqueConstraint);

        builder.HasIndex(item => item.OriginalPaymentId)
            .IsUnique()
            .HasFilter("original_payment_id IS NOT NULL")
            .HasDatabaseName(OriginalPaymentUniqueConstraint);

        builder.HasIndex(item => item.JobId)
            .IsUnique()
            .HasFilter("job_id IS NOT NULL")
            .HasDatabaseName(JobUniqueConstraint);
    }
}
