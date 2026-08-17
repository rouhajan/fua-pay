using FuaPay.Web.Modules.ServiceUnits.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.ServiceUnits.Infrastructure.Persistence;

internal sealed class ServiceUnitConfiguration :
    IEntityTypeConfiguration<ServiceUnitEntity>
{
    internal const string PrimaryKeyConstraint =
        "pk_service_units_units";

    internal const string CodeUniqueConstraint =
        "uq_service_units_units_code";

    public void Configure(EntityTypeBuilder<ServiceUnitEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "units",
            "service_units",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_service_units_units_id_not_empty",
                    "id <> '00000000-0000-0000-0000-000000000000'::uuid");

                table.HasCheckConstraint(
                    "ck_service_units_units_code_valid",
                    "code ~ '^[A-Z0-9]{2,8}$'");

                table.HasCheckConstraint(
                    "ck_service_units_units_name_not_blank",
                    "length(btrim(display_name)) > 0");

                table.HasCheckConstraint(
                    "ck_service_units_units_service_type_valid",
                    "default_service_type IN (1, 2, 3, 4)");

                table.HasCheckConstraint(
                    "ck_service_units_units_status_valid",
                    "status IN (1, 2)");

                table.HasCheckConstraint(
                    "ck_service_units_units_version_positive",
                    "version > 0");

                table.HasCheckConstraint(
                    "ck_service_units_units_created_actor_valid",
                    ActorConstraint("created_by"));

                table.HasCheckConstraint(
                    "ck_service_units_units_deactivated_actor_valid",
                    """
                    (
                        status = 1
                        AND deactivated_at IS NULL
                        AND deactivated_by_type IS NULL
                        AND deactivated_by_user_id IS NULL
                        AND deactivated_by_process_name IS NULL
                    )
                    OR
                    (
                        status = 2
                        AND deactivated_at IS NOT NULL
                        AND deactivated_at >= created_at
                        AND
                        (
                    """ +
                    ActorConstraint("deactivated_by") +
                    """
                        )
                    )
                    """);
            });

        builder.HasKey(item => item.Id)
            .HasName(PrimaryKeyConstraint);

        builder.Property(item => item.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(item => item.Code)
            .HasColumnName("code")
            .HasMaxLength(ServiceUnitTextLimits.CodeMaxLength)
            .IsRequired();

        builder.Property(item => item.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(ServiceUnitTextLimits.DisplayNameMaxLength)
            .IsRequired();

        builder.Property(item => item.DefaultServiceType)
            .HasColumnName("default_service_type")
            .IsRequired();

        builder.Property(item => item.Status)
            .HasColumnName("status")
            .IsRequired();

        builder.Property(item => item.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(item => item.CreatedByType)
            .HasColumnName("created_by_type")
            .IsRequired();

        builder.Property(item => item.CreatedByUserId)
            .HasColumnName("created_by_user_id");

        builder.Property(item => item.CreatedByProcessName)
            .HasColumnName("created_by_process_name")
            .HasMaxLength(ServiceUnitTextLimits.ProcessNameMaxLength);

        builder.Property(item => item.DeactivatedAt)
            .HasColumnName("deactivated_at");

        builder.Property(item => item.DeactivatedByType)
            .HasColumnName("deactivated_by_type");

        builder.Property(item => item.DeactivatedByUserId)
            .HasColumnName("deactivated_by_user_id");

        builder.Property(item => item.DeactivatedByProcessName)
            .HasColumnName("deactivated_by_process_name")
            .HasMaxLength(ServiceUnitTextLimits.ProcessNameMaxLength);

        builder.Property(item => item.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(item => item.Code)
            .IsUnique()
            .HasDatabaseName(CodeUniqueConstraint);

        builder.HasIndex(item => new { item.Status, item.DisplayName })
            .HasDatabaseName("ix_service_units_units_status_name");
    }

    private static string ActorConstraint(string prefix)
    {
        return $"""
            (
                {prefix}_type = 1
                AND {prefix}_user_id IS NOT NULL
                AND {prefix}_process_name IS NULL
            )
            OR
            (
                {prefix}_type = 2
                AND {prefix}_user_id IS NULL
                AND {prefix}_process_name IS NOT NULL
                AND length(btrim({prefix}_process_name)) > 0
            )
            """;
    }
}
