using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class SettlementReturnProviderAttemptConfiguration :
    IEntityTypeConfiguration<SettlementReturnProviderAttemptEntity>
{
    internal const string PrimaryKeyConstraint =
        "pk_payments_return_provider_attempts";

    internal const string AttemptSequenceUniqueConstraint =
        "uq_payments_return_provider_attempts_sequence";

    public void Configure(
        EntityTypeBuilder<SettlementReturnProviderAttemptEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "settlement_return_provider_attempts",
            "payments",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_payments_return_provider_attempts_id_not_empty",
                    "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_payments_return_provider_attempts_return_not_empty",
                    "settlement_return_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_payments_return_provider_attempts_provider_valid",
                    "provider IN (1, 2)");
                table.HasCheckConstraint(
                    "ck_payments_return_provider_attempts_operation_valid",
                    "operation IN (1, 2)");
                table.HasCheckConstraint(
                    "ck_payments_return_provider_attempts_reference_not_blank",
                    "length(btrim(provider_reference)) > 0");
                table.HasCheckConstraint(
                    "ck_payments_return_provider_attempts_state_valid",
                    "state IN (1, 2, 3, 4, 5)");
                table.HasCheckConstraint(
                    "ck_payments_return_provider_attempts_timestamps_ordered",
                    "updated_at >= created_at AND " +
                    "(started_at IS NULL OR (started_at >= created_at AND updated_at >= started_at)) AND " +
                    "(finished_at IS NULL OR (finished_at >= created_at AND updated_at >= finished_at AND " +
                    "(started_at IS NULL OR finished_at >= started_at)))");
                table.HasCheckConstraint(
                    "ck_payments_return_provider_attempts_state_consistent",
                    "(state = 1 AND started_at IS NULL AND finished_at IS NULL AND updated_at = created_at) OR " +
                    "(state = 2 AND started_at IS NOT NULL AND finished_at IS NULL AND updated_at = started_at) OR " +
                    "(state = 3 AND started_at IS NOT NULL AND finished_at IS NOT NULL AND updated_at = finished_at) OR " +
                    "(state = 4 AND finished_at IS NOT NULL AND updated_at = finished_at) OR " +
                    "(state = 5 AND started_at IS NOT NULL AND finished_at IS NULL AND updated_at >= started_at)");
                table.HasCheckConstraint(
                    "ck_payments_return_provider_attempts_diagnostic_consistent",
                    "(state IN (4, 5) AND diagnostic IS NOT NULL AND length(btrim(diagnostic)) > 0) OR " +
                    "(state IN (1, 2) AND diagnostic IS NULL) OR state = 3");
                table.HasCheckConstraint(
                    "ck_payments_return_provider_attempts_version_positive",
                    "version > 0");
            });

        builder.HasKey(item => item.Id)
            .HasName(PrimaryKeyConstraint);

        builder.Property(item => item.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(item => item.SettlementReturnId)
            .HasColumnName("settlement_return_id")
            .IsRequired();
        builder.Property(item => item.Provider)
            .HasColumnName("provider")
            .IsRequired();
        builder.Property(item => item.Operation)
            .HasColumnName("operation")
            .IsRequired();
        builder.Property(item => item.ProviderReference)
            .HasColumnName("provider_reference")
            .HasMaxLength(PaymentProviderReference.MaxLength)
            .IsRequired();
        builder.Property(item => item.State)
            .HasColumnName("state")
            .IsRequired();
        builder.Property(item => item.Diagnostic)
            .HasColumnName("diagnostic")
            .HasMaxLength(
                SettlementReturnProviderAttempt.MaximumDiagnosticLength);
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

        builder.HasOne<SettlementReturnEntity>()
            .WithMany()
            .HasForeignKey(item => item.SettlementReturnId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_payments_return_provider_attempts_settlement_return");

        builder.HasIndex(item => item.SettlementReturnId)
            .IsUnique()
            .HasFilter("state IN (1, 2, 3, 5)")
            .HasDatabaseName(AttemptSequenceUniqueConstraint);

        builder.HasIndex(
                item => new
                {
                    item.SettlementReturnId,
                    item.CreatedAt,
                    item.Id
                })
            .HasDatabaseName(
                "ix_payments_return_provider_attempts_history");
    }
}
