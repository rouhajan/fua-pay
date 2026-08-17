using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Jobs.Infrastructure.Persistence;

internal sealed class JobConfiguration :
    IEntityTypeConfiguration<JobEntity>
{
    internal const string PrimaryKeyConstraint =
        "pk_jobs_jobs";

    internal const string SettlementReferenceUniqueConstraint =
        "uq_jobs_jobs_settlement_reference";

    internal const string NumberUniqueConstraint =
        "uq_jobs_jobs_number";

    public void Configure(
        EntityTypeBuilder<JobEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "jobs",
            "jobs",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_jobs_jobs_id_not_empty",
                    "id <> '00000000-0000-0000-0000-000000000000'::uuid");

                table.HasCheckConstraint(
                    "ck_jobs_jobs_number_valid",
                    "job_number ~ '^[A-Z0-9]{2,8}-[0-9]{4}-[0-9]{6}$'");

                table.HasCheckConstraint(
                    "ck_jobs_jobs_service_unit_not_empty",
                    "service_unit_id <> '00000000-0000-0000-0000-000000000000'::uuid");

                table.HasCheckConstraint(
                    "ck_jobs_jobs_customer_not_empty",
                    "customer_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");

                table.HasCheckConstraint(
                    "ck_jobs_jobs_created_by_not_empty",
                    "created_by_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");

                table.HasCheckConstraint(
                    "ck_jobs_jobs_service_type_valid",
                    "service_type IN (1, 2, 3, 4)");

                table.HasCheckConstraint(
                    "ck_jobs_jobs_title_not_empty",
                    "length(btrim(title)) > 0");

                table.HasCheckConstraint(
                    "ck_jobs_jobs_description_not_empty",
                    "length(btrim(description)) > 0");

                table.HasCheckConstraint(
                    "ck_jobs_jobs_price_positive",
                    "price_minor_units > 0");

                table.HasCheckConstraint(
                    "ck_jobs_jobs_production_status_valid",
                    "production_status IN (1, 2, 3, 4, 5, 6)");

                table.HasCheckConstraint(
                    "ck_jobs_jobs_payment_status_valid",
                    "payment_status IN (1, 2)");

                table.HasCheckConstraint(
                    "ck_jobs_jobs_version_positive",
                    "version > 0");

                table.HasCheckConstraint(
                    "ck_jobs_jobs_settlement_consistent",
                    """
                    (
                        payment_status = 1
                        AND settlement_type IS NULL
                        AND settlement_reference_id IS NULL
                        AND settled_at IS NULL
                    )
                    OR
                    (
                        payment_status = 2
                        AND settlement_type IN (1, 2)
                        AND settlement_reference_id IS NOT NULL
                        AND settlement_reference_id <>
                            '00000000-0000-0000-0000-000000000000'::uuid
                        AND settled_at IS NOT NULL
                        AND published_at IS NOT NULL
                        AND settled_at >= published_at
                    )
                    """);

                table.HasCheckConstraint(
                    "ck_jobs_jobs_lifecycle_consistent",
                    """
                    (
                        production_status = 1
                        AND published_at IS NULL
                        AND production_started_at IS NULL
                        AND ready_for_pickup_at IS NULL
                        AND completed_at IS NULL
                        AND cancelled_at IS NULL
                    )
                    OR
                    (
                        production_status = 2
                        AND published_at IS NOT NULL
                        AND production_started_at IS NULL
                        AND ready_for_pickup_at IS NULL
                        AND completed_at IS NULL
                        AND cancelled_at IS NULL
                    )
                    OR
                    (
                        production_status = 3
                        AND published_at IS NOT NULL
                        AND production_started_at IS NOT NULL
                        AND ready_for_pickup_at IS NULL
                        AND completed_at IS NULL
                        AND cancelled_at IS NULL
                    )
                    OR
                    (
                        production_status = 4
                        AND published_at IS NOT NULL
                        AND production_started_at IS NOT NULL
                        AND ready_for_pickup_at IS NOT NULL
                        AND completed_at IS NULL
                        AND cancelled_at IS NULL
                    )
                    OR
                    (
                        production_status = 5
                        AND published_at IS NOT NULL
                        AND production_started_at IS NOT NULL
                        AND ready_for_pickup_at IS NOT NULL
                        AND completed_at IS NOT NULL
                        AND cancelled_at IS NULL
                    )
                    OR
                    (
                        production_status = 6
                        AND production_started_at IS NULL
                        AND ready_for_pickup_at IS NULL
                        AND completed_at IS NULL
                        AND cancelled_at IS NOT NULL
                    )
                    """);

                table.HasCheckConstraint(
                    "ck_jobs_jobs_paid_before_production",
                    """
                    (
                        production_status NOT IN (3, 4, 5)
                        OR payment_status = 2
                    )
                    AND
                    (
                        production_status <> 6
                        OR payment_status = 1
                    )
                    """);

                table.HasCheckConstraint(
                    "ck_jobs_jobs_timestamps_ordered",
                    """
                    (published_at IS NULL OR published_at >= created_at)
                    AND
                    (
                        settled_at IS NULL
                        OR
                        (
                            published_at IS NOT NULL
                            AND settled_at >= published_at
                        )
                    )
                    AND
                    (
                        production_started_at IS NULL
                        OR
                        (
                            settled_at IS NOT NULL
                            AND production_started_at >= settled_at
                        )
                    )
                    AND
                    (
                        ready_for_pickup_at IS NULL
                        OR
                        (
                            production_started_at IS NOT NULL
                            AND ready_for_pickup_at >= production_started_at
                        )
                    )
                    AND
                    (
                        completed_at IS NULL
                        OR
                        (
                            ready_for_pickup_at IS NOT NULL
                            AND completed_at >= ready_for_pickup_at
                        )
                    )
                    AND
                    (
                        cancelled_at IS NULL
                        OR cancelled_at >=
                            COALESCE(published_at, created_at)
                    )
                    """);
            });

        builder.HasKey(job => job.Id)
            .HasName(PrimaryKeyConstraint);

        builder.Property(job => job.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(job => job.Number)
            .HasColumnName("job_number")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(job => job.ServiceUnitId)
            .HasColumnName("service_unit_id")
            .IsRequired();

        builder.Property(job => job.CustomerUserId)
            .HasColumnName("customer_user_id")
            .IsRequired();

        builder.Property(job => job.CreatedByUserId)
            .HasColumnName("created_by_user_id")
            .IsRequired();

        builder.Property(job => job.ServiceType)
            .HasColumnName("service_type")
            .IsRequired();

        builder.Property(job => job.Title)
            .HasColumnName("title")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(job => job.Description)
            .HasColumnName("description")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(job => job.PriceMinorUnits)
            .HasColumnName("price_minor_units")
            .IsRequired();

        builder.Property(job => job.ProductionStatus)
            .HasColumnName("production_status")
            .IsRequired();

        builder.Property(job => job.PaymentStatus)
            .HasColumnName("payment_status")
            .IsRequired();

        builder.Property(job => job.SettlementType)
            .HasColumnName("settlement_type");

        builder.Property(job => job.SettlementReferenceId)
            .HasColumnName("settlement_reference_id");

        builder.Property(job => job.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(job => job.PublishedAt)
            .HasColumnName("published_at");

        builder.Property(job => job.SettledAt)
            .HasColumnName("settled_at");

        builder.Property(job => job.ProductionStartedAt)
            .HasColumnName("production_started_at");

        builder.Property(job => job.ReadyForPickupAt)
            .HasColumnName("ready_for_pickup_at");

        builder.Property(job => job.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(job => job.CancelledAt)
            .HasColumnName("cancelled_at");

        builder.Property(job => job.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(job => job.Number)
            .IsUnique()
            .HasDatabaseName(NumberUniqueConstraint);

        builder.HasIndex(
                job => new
                {
                    job.SettlementType,
                    job.SettlementReferenceId
                })
            .IsUnique()
            .HasFilter(
                "settlement_type IS NOT NULL " +
                "AND settlement_reference_id IS NOT NULL")
            .HasDatabaseName(
                SettlementReferenceUniqueConstraint);

        builder.HasIndex(
                job => new
                {
                    job.CustomerUserId,
                    job.CreatedAt
                })
            .HasDatabaseName(
                "ix_jobs_jobs_customer_created_at");

        builder.HasIndex(
                job => new
                {
                    job.ServiceUnitId,
                    job.CreatedAt
                })
            .HasDatabaseName(
                "ix_jobs_jobs_service_unit_created_at");

        builder.HasIndex(
                job => new
                {
                    job.CreatedByUserId,
                    job.CreatedAt
                })
            .HasDatabaseName(
                "ix_jobs_jobs_created_by_created_at");

        builder.HasIndex(
                job => new
                {
                    job.ProductionStatus,
                    job.PaymentStatus
                })
            .HasDatabaseName(
                "ix_jobs_jobs_production_payment_status");
    }
}
