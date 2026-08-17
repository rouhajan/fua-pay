using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Application;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class CreditAdjustmentCommandConfiguration :
    IEntityTypeConfiguration<CreditAdjustmentCommandEntity>
{
    internal const string PrimaryKeyConstraint =
        "pk_credits_adjustment_commands";

    public void Configure(
        EntityTypeBuilder<CreditAdjustmentCommandEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var maximum = FinancialAmountPolicy
            .CreditAdjustmentAbsolute
            .MaximumMinorUnits;

        builder.ToTable(
            "adjustment_commands",
            "credits",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_credits_adjustment_commands_id_not_empty",
                    "command_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_credits_adjustment_commands_administrator_not_empty",
                    "administrator_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_credits_adjustment_commands_owner_not_empty",
                    "owner_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_credits_adjustment_commands_amount_allowed",
                    $"signed_amount_minor_units <> 0 AND signed_amount_minor_units BETWEEN {-maximum} AND {maximum}");
                table.HasCheckConstraint(
                    "ck_credits_adjustment_commands_reason_not_empty",
                    "length(btrim(reason)) > 0");
            });

        builder.HasKey(item => item.CommandId)
            .HasName(PrimaryKeyConstraint);

        builder.Property(item => item.CommandId)
            .HasColumnName("command_id")
            .ValueGeneratedNever();
        builder.Property(item => item.AdministratorUserId)
            .HasColumnName("administrator_user_id")
            .IsRequired();
        builder.Property(item => item.OwnerId)
            .HasColumnName("owner_id")
            .IsRequired();
        builder.Property(item => item.SignedAmountMinorUnits)
            .HasColumnName("signed_amount_minor_units")
            .IsRequired();
        builder.Property(item => item.Reason)
            .HasColumnName("reason")
            .HasMaxLength(CreditAdjustmentCommand.ReasonMaxLength)
            .IsRequired();
        builder.Property(item => item.AcceptedAt)
            .HasColumnName("accepted_at")
            .IsRequired();
    }
}
