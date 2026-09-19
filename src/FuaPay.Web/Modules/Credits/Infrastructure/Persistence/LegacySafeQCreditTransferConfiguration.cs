using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Application;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class LegacySafeQCreditTransferConfiguration :
    IEntityTypeConfiguration<LegacySafeQCreditTransferEntity>
{
    internal const string PrimaryKeyConstraint =
        "pk_credits_legacy_safeq_credit_transfers";

    internal const string SafeQUserIdUniqueConstraint =
        "ux_credits_legacy_safeq_credit_transfers_safeq_user_id";

    public void Configure(
        EntityTypeBuilder<LegacySafeQCreditTransferEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var maximum =
            FinancialAmountPolicy.CreditAdjustmentAbsolute.MaximumMinorUnits;

        builder.ToTable(
            "legacy_safeq_credit_transfers",
            "credits",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_credits_legacy_safeq_transfers_command_not_empty",
                    "command_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_credits_legacy_safeq_transfers_administrator_not_empty",
                    "administrator_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_credits_legacy_safeq_transfers_owner_not_empty",
                    "owner_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_credits_legacy_safeq_transfers_safeq_user_not_empty",
                    "length(btrim(safeq_user_id)) > 0");
                table.HasCheckConstraint(
                    "ck_credits_legacy_safeq_transfers_snapshot_sha256",
                    "snapshot_sha256 ~ '^[0-9A-F]{64}$'");
                table.HasCheckConstraint(
                    "ck_credits_legacy_safeq_transfers_amount_allowed",
                    $"amount_minor_units BETWEEN 1 AND {maximum}");
            });

        builder.HasKey(item => item.CommandId)
            .HasName(PrimaryKeyConstraint);

        builder.HasIndex(item => item.SafeQUserId)
            .IsUnique()
            .HasDatabaseName(SafeQUserIdUniqueConstraint);

        builder.Property(item => item.CommandId)
            .HasColumnName("command_id")
            .ValueGeneratedNever();

        builder.Property(item => item.AdministratorUserId)
            .HasColumnName("administrator_user_id")
            .IsRequired();

        builder.Property(item => item.OwnerId)
            .HasColumnName("owner_id")
            .IsRequired();

        builder.Property(item => item.SafeQUserId)
            .HasColumnName("safeq_user_id")
            .HasMaxLength(
                LegacySafeQCreditTransferCommand.SafeQUserIdMaxLength)
            .IsRequired();

        builder.Property(item => item.SnapshotSha256)
            .HasColumnName("snapshot_sha256")
            .HasMaxLength(
                LegacySafeQCreditTransferCommand.SnapshotSha256Length)
            .IsRequired();

        builder.Property(item => item.AmountMinorUnits)
            .HasColumnName("amount_minor_units")
            .IsRequired();

        builder.Property(item => item.AcceptedAt)
            .HasColumnName("accepted_at")
            .IsRequired();
    }
}
