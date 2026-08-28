using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class CreditReturnHoldConfiguration :
    IEntityTypeConfiguration<CreditReturnHoldEntity>
{
    internal const string PrimaryKeyConstraint =
        "pk_credits_return_holds";

    public void Configure(
        EntityTypeBuilder<CreditReturnHoldEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "return_holds",
            "credits",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_credits_return_holds_return_not_empty",
                    "settlement_return_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_credits_return_holds_account_not_empty",
                    "credit_account_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_credits_return_holds_amount_positive",
                    "amount_minor_units > 0");
                table.HasCheckConstraint(
                    "ck_credits_return_holds_state_valid",
                    "state IN (1, 2, 3)");
                table.HasCheckConstraint(
                    "ck_credits_return_holds_timestamps_ordered",
                    "state_changed_at >= created_at");
                table.HasCheckConstraint(
                    "ck_credits_return_holds_version_positive",
                    "version > 0");
            });

        builder.HasKey(item => item.SettlementReturnId)
            .HasName(PrimaryKeyConstraint);

        builder.Property(item => item.SettlementReturnId)
            .HasColumnName("settlement_return_id")
            .ValueGeneratedNever();
        builder.Property(item => item.CreditAccountId)
            .HasColumnName("credit_account_id")
            .IsRequired();
        builder.Property(item => item.AmountMinorUnits)
            .HasColumnName("amount_minor_units")
            .IsRequired();
        builder.Property(item => item.State)
            .HasColumnName("state")
            .IsRequired();
        builder.Property(item => item.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
        builder.Property(item => item.StateChangedAt)
            .HasColumnName("state_changed_at")
            .IsRequired();
        builder.Property(item => item.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasOne<CreditAccountEntity>()
            .WithMany()
            .HasForeignKey(item => item.CreditAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_credits_return_holds_account");

        builder.HasOne<SettlementReturnEntity>()
            .WithOne()
            .HasForeignKey<CreditReturnHoldEntity>(
                item => item.SettlementReturnId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_credits_return_holds_settlement_return");

        builder.HasIndex(
                item => new
                {
                    item.CreditAccountId,
                    item.State
                })
            .HasDatabaseName(
                "ix_credits_return_holds_account_state");
    }
}
