using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class CreditAccountConfiguration :
    IEntityTypeConfiguration<CreditAccountEntity>
{
    internal const string OwnerUniqueConstraint =
        "uq_credits_accounts_owner";

    public void Configure(
        EntityTypeBuilder<CreditAccountEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "accounts",
            "credits",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_credits_accounts_id_not_empty",
                    "id <> '00000000-0000-0000-0000-000000000000'::uuid");

                table.HasCheckConstraint(
                    "ck_credits_accounts_owner_not_empty",
                    "owner_id <> '00000000-0000-0000-0000-000000000000'::uuid");

                table.HasCheckConstraint(
                    "ck_credits_accounts_balance_non_negative",
                    "balance_minor_units >= 0");

                table.HasCheckConstraint(
                    "ck_credits_accounts_version_positive",
                    "version > 0");
            });

        builder.HasKey(account => account.Id)
            .HasName("pk_credits_accounts");

        builder.Property(account => account.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(account => account.OwnerId)
            .HasColumnName("owner_id")
            .IsRequired();

        builder.Property(account => account.BalanceMinorUnits)
            .HasColumnName("balance_minor_units")
            .IsRequired();

        builder.Property(account => account.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(account => account.OwnerId)
            .IsUnique()
            .HasDatabaseName(OwnerUniqueConstraint);

        builder.HasMany(account => account.Movements)
            .WithOne(movement => movement.Account)
            .HasForeignKey(movement => movement.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
