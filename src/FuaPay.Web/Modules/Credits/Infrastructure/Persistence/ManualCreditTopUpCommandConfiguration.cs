using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Application;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class ManualCreditTopUpCommandConfiguration :
    IEntityTypeConfiguration<ManualCreditTopUpCommandEntity>
{
    internal const string PrimaryKeyConstraint =
        "pk_credits_manual_topup_commands";

    public void Configure(
        EntityTypeBuilder<ManualCreditTopUpCommandEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var maximum = FinancialAmountPolicy
            .ManualCreditTopUp
            .MaximumMinorUnits;

        builder.ToTable(
            "manual_topup_commands",
            "credits",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_credits_manual_topup_commands_id_not_empty",
                    "command_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_credits_manual_topup_commands_administrator_not_empty",
                    "administrator_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_credits_manual_topup_commands_owner_not_empty",
                    "owner_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_credits_manual_topup_commands_amount_allowed",
                    $"amount_minor_units BETWEEN 1 AND {maximum}");
                table.HasCheckConstraint(
                    "ck_credits_manual_topup_commands_note_not_empty",
                    "length(btrim(note)) > 0");
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
        builder.Property(item => item.AmountMinorUnits)
            .HasColumnName("amount_minor_units")
            .IsRequired();
        builder.Property(item => item.Note)
            .HasColumnName("note")
            .HasMaxLength(ManualCreditTopUpCommand.NoteMaxLength)
            .IsRequired();
        builder.Property(item => item.AcceptedAt)
            .HasColumnName("accepted_at")
            .IsRequired();
    }
}
