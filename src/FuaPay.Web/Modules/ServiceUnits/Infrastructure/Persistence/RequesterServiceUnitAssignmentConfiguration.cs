using FuaPay.Web.Modules.ServiceUnits.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.ServiceUnits.Infrastructure.Persistence;

internal sealed class RequesterServiceUnitAssignmentConfiguration :
    IEntityTypeConfiguration<RequesterServiceUnitAssignmentEntity>
{
    internal const string PrimaryKeyConstraint =
        "pk_service_units_requester_assignments";

    internal const string ActiveAssignmentUniqueConstraint =
        "uq_service_units_requester_assignments_active";

    public void Configure(
        EntityTypeBuilder<RequesterServiceUnitAssignmentEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "requester_assignments",
            "service_units",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_service_units_assignments_id_not_empty",
                    "id <> '00000000-0000-0000-0000-000000000000'::uuid");

                table.HasCheckConstraint(
                    "ck_service_units_assignments_unit_not_empty",
                    "service_unit_id <> '00000000-0000-0000-0000-000000000000'::uuid");

                table.HasCheckConstraint(
                    "ck_service_units_assignments_user_not_empty",
                    "user_id <> '00000000-0000-0000-0000-000000000000'::uuid");

                table.HasCheckConstraint(
                    "ck_service_units_assignments_version_positive",
                    "version > 0");

                table.HasCheckConstraint(
                    "ck_service_units_assignments_granted_actor_valid",
                    ActorConstraint("granted_by"));

                table.HasCheckConstraint(
                    "ck_service_units_assignments_revoked_actor_valid",
                    """
                    (
                        revoked_at IS NULL
                        AND revoked_by_type IS NULL
                        AND revoked_by_user_id IS NULL
                        AND revoked_by_process_name IS NULL
                    )
                    OR
                    (
                        revoked_at IS NOT NULL
                        AND revoked_at >= granted_at
                        AND
                        (
                    """ +
                    ActorConstraint("revoked_by") +
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

        builder.Property(item => item.ServiceUnitId)
            .HasColumnName("service_unit_id")
            .IsRequired();

        builder.Property(item => item.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(item => item.GrantedAt)
            .HasColumnName("granted_at")
            .IsRequired();

        builder.Property(item => item.GrantedByType)
            .HasColumnName("granted_by_type")
            .IsRequired();

        builder.Property(item => item.GrantedByUserId)
            .HasColumnName("granted_by_user_id");

        builder.Property(item => item.GrantedByProcessName)
            .HasColumnName("granted_by_process_name")
            .HasMaxLength(ServiceUnitTextLimits.ProcessNameMaxLength);

        builder.Property(item => item.RevokedAt)
            .HasColumnName("revoked_at");

        builder.Property(item => item.RevokedByType)
            .HasColumnName("revoked_by_type");

        builder.Property(item => item.RevokedByUserId)
            .HasColumnName("revoked_by_user_id");

        builder.Property(item => item.RevokedByProcessName)
            .HasColumnName("revoked_by_process_name")
            .HasMaxLength(ServiceUnitTextLimits.ProcessNameMaxLength);

        builder.Property(item => item.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(
                item => new
                {
                    item.ServiceUnitId,
                    item.UserId
                })
            .IsUnique()
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName(ActiveAssignmentUniqueConstraint);

        builder.HasIndex(
                item => new
                {
                    item.UserId,
                    item.GrantedAt
                })
            .HasDatabaseName(
                "ix_service_units_assignments_user_granted_at");

        builder.HasOne(item => item.ServiceUnit)
            .WithMany(unit => unit.RequesterAssignments)
            .HasForeignKey(item => item.ServiceUnitId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_service_units_assignments_unit");
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
