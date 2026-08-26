using FuaPay.Web.Modules.Credits.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class PrintReservationConfiguration :
    IEntityTypeConfiguration<PrintReservationEntity>
{
    internal const string PrintJobUniqueConstraint =
        "uq_credits_print_reservations_print_job";

    internal const string ReserveCommandUniqueConstraint =
        "uq_credits_print_reservations_reserve_command";

    public void Configure(
        EntityTypeBuilder<PrintReservationEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "print_reservations",
            "credits",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_credits_print_reservations_id_not_empty",
                    "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_credits_print_reservations_account_not_empty",
                    "credit_account_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_credits_print_reservations_source_not_empty",
                    "print_source_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_credits_print_reservations_reserve_command_not_empty",
                    "reserve_command_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_credits_print_reservations_resolution_command_not_empty",
                    "resolution_command_id IS NULL OR resolution_command_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_credits_print_reservations_terminal_command_not_empty",
                    "terminal_command_id IS NULL OR terminal_command_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_credits_print_reservations_debit_operation_not_empty",
                    "debit_operation_id IS NULL OR debit_operation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_credits_print_reservations_job_uuid_valid",
                    "job_uuid ~ '^urn:uuid:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' AND job_uuid <> 'urn:uuid:00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint(
                    "ck_credits_print_reservations_amount_positive",
                    "amount_minor_units > 0");
                table.HasCheckConstraint(
                    "ck_credits_print_reservations_status_valid",
                    "status IN (1, 2, 3, 4)");
                table.HasCheckConstraint(
                    "ck_credits_print_reservations_timestamps_ordered",
                    "state_changed_at >= created_at");
                table.HasCheckConstraint(
                    "ck_credits_print_reservations_version_positive",
                    "version > 0");
                table.HasCheckConstraint(
                    "ck_credits_print_reservations_state_consistent",
                    """
                    (
                        status = 1
                        AND resolution_command_id IS NULL
                        AND terminal_command_id IS NULL
                        AND debit_operation_id IS NULL
                    )
                    OR
                    (
                        status = 2
                        AND debit_operation_id IS NULL
                    )
                    OR
                    (
                        status = 3
                        AND terminal_command_id IS NOT NULL
                        AND debit_operation_id IS NOT NULL
                    )
                    OR
                    (
                        status = 4
                        AND terminal_command_id IS NOT NULL
                        AND debit_operation_id IS NULL
                    )
                    """);
            });

        builder.HasKey(reservation => reservation.Id)
            .HasName("pk_credits_print_reservations");

        builder.Property(reservation => reservation.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(reservation => reservation.CreditAccountId)
            .HasColumnName("credit_account_id")
            .IsRequired();
        builder.Property(reservation => reservation.PrintSourceId)
            .HasColumnName("print_source_id")
            .IsRequired();
        builder.Property(reservation => reservation.JobUuid)
            .HasColumnName("job_uuid")
            .HasMaxLength(IppJobUuid.MaxLength)
            .IsRequired();
        builder.Property(reservation => reservation.AmountMinorUnits)
            .HasColumnName("amount_minor_units")
            .IsRequired();
        builder.Property(reservation => reservation.Status)
            .HasColumnName("status")
            .IsRequired();
        builder.Property(reservation => reservation.ReserveCommandId)
            .HasColumnName("reserve_command_id")
            .IsRequired();
        builder.Property(reservation => reservation.ResolutionCommandId)
            .HasColumnName("resolution_command_id");
        builder.Property(reservation => reservation.TerminalCommandId)
            .HasColumnName("terminal_command_id");
        builder.Property(reservation => reservation.DebitOperationId)
            .HasColumnName("debit_operation_id");
        builder.Property(reservation => reservation.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
        builder.Property(reservation => reservation.StateChangedAt)
            .HasColumnName("state_changed_at")
            .IsRequired();
        builder.Property(reservation => reservation.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasOne<CreditAccountEntity>()
            .WithMany()
            .HasForeignKey(reservation => reservation.CreditAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_credits_print_reservations_account");

        builder.HasIndex(
                reservation => new
                {
                    reservation.PrintSourceId,
                    reservation.JobUuid
                })
            .IsUnique()
            .HasDatabaseName(PrintJobUniqueConstraint);

        builder.HasIndex(
                reservation => new
                {
                    reservation.PrintSourceId,
                    reservation.ReserveCommandId
                })
            .IsUnique()
            .HasDatabaseName(ReserveCommandUniqueConstraint);

        builder.HasIndex(
                reservation => new
                {
                    reservation.PrintSourceId,
                    reservation.ResolutionCommandId
                })
            .IsUnique()
            .HasFilter("resolution_command_id IS NOT NULL")
            .HasDatabaseName(
                "uq_credits_print_reservations_resolution_command");

        builder.HasIndex(
                reservation => new
                {
                    reservation.PrintSourceId,
                    reservation.TerminalCommandId
                })
            .IsUnique()
            .HasFilter("terminal_command_id IS NOT NULL")
            .HasDatabaseName(
                "uq_credits_print_reservations_terminal_command");

        builder.HasIndex(reservation => reservation.DebitOperationId)
            .IsUnique()
            .HasFilter("debit_operation_id IS NOT NULL")
            .HasDatabaseName(
                "uq_credits_print_reservations_debit_operation");

        builder.HasIndex(
                reservation => new
                {
                    reservation.CreditAccountId,
                    reservation.Status
                })
            .HasDatabaseName(
                "ix_credits_print_reservations_account_status");
    }
}
