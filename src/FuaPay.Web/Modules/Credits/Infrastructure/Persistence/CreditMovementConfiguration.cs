using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class CreditMovementConfiguration :
    IEntityTypeConfiguration<CreditMovementEntity>
{
    internal const string OperationUniqueConstraint =
        "uq_credits_movements_operation";

    internal const string AccountSequenceUniqueConstraint =
        "uq_credits_movements_account_sequence";

    public void Configure(
        EntityTypeBuilder<CreditMovementEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "movements",
            "credits",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_credits_movements_account_not_empty",
                    "account_id <> '00000000-0000-0000-0000-000000000000'::uuid");

                table.HasCheckConstraint(
                    "ck_credits_movements_operation_not_empty",
                    "operation_id <> '00000000-0000-0000-0000-000000000000'::uuid");

                table.HasCheckConstraint(
                    "ck_credits_movements_sequence_positive",
                    "sequence > 0");

                table.HasCheckConstraint(
                    "ck_credits_movements_type_valid",
                    "movement_type IN (1, 2)");

                table.HasCheckConstraint(
                    "ck_credits_movements_amount_positive",
                    "amount_minor_units > 0");

                table.HasCheckConstraint(
                    "ck_credits_movements_balance_non_negative",
                    "balance_after_minor_units >= 0");

                table.HasCheckConstraint(
                    "ck_credits_movements_description_not_empty",
                    "length(btrim(description)) > 0");
            });

        builder.HasKey(movement => movement.Id)
            .HasName("pk_credits_movements");

        builder.Property(movement => movement.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(movement => movement.AccountId)
            .HasColumnName("account_id")
            .IsRequired();

        builder.Property(movement => movement.Sequence)
            .HasColumnName("sequence")
            .IsRequired();

        builder.Property(movement => movement.OperationId)
            .HasColumnName("operation_id")
            .IsRequired();

        builder.Property(movement => movement.MovementType)
            .HasColumnName("movement_type")
            .IsRequired();

        builder.Property(movement => movement.AmountMinorUnits)
            .HasColumnName("amount_minor_units")
            .IsRequired();

        builder.Property(movement => movement.BalanceAfterMinorUnits)
            .HasColumnName("balance_after_minor_units")
            .IsRequired();

        builder.Property(movement => movement.RecordedAt)
            .HasColumnName("recorded_at")
            .IsRequired();

        builder.Property(movement => movement.Description)
            .HasColumnName("description")
            .HasColumnType("text")
            .IsRequired();

        builder.HasIndex(movement => movement.OperationId)
            .IsUnique()
            .HasDatabaseName(OperationUniqueConstraint);

        builder.HasIndex(
                movement => new
                {
                    movement.AccountId,
                    movement.Sequence
                })
            .IsUnique()
            .HasDatabaseName(
                AccountSequenceUniqueConstraint);

        builder.HasIndex(
                movement => new
                {
                    movement.AccountId,
                    movement.RecordedAt
                })
            .HasDatabaseName(
                "ix_credits_movements_account_recorded_at");
    }
}
