using FuaPay.Web.Modules.Access.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Access.Infrastructure.Persistence;

internal sealed class RoleAssignmentConfiguration :
    IEntityTypeConfiguration<RoleAssignmentEntity>
{
    internal const string ActiveRoleUniqueConstraint =
        "uq_access_role_assignments_active_role";

    public void Configure(
        EntityTypeBuilder<RoleAssignmentEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "role_assignments",
            "access",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_access_role_assignments_id_not_empty",
                    "id <> '00000000-0000-0000-0000-000000000000'::uuid");

                table.HasCheckConstraint(
                    "ck_access_role_assignments_user_not_empty",
                    "user_id <> '00000000-0000-0000-0000-000000000000'::uuid");

                table.HasCheckConstraint(
                    "ck_access_role_assignments_role_valid",
                    "role IN (1, 2, 3)");

                table.HasCheckConstraint(
                    "ck_access_role_assignments_granted_actor_valid",
                    """
                    (
                        granted_by_type = 1
                        AND granted_by_user_id IS NOT NULL
                        AND granted_by_process_name IS NULL
                    )
                    OR
                    (
                        granted_by_type = 2
                        AND granted_by_user_id IS NULL
                        AND granted_by_process_name IS NOT NULL
                        AND length(btrim(granted_by_process_name)) > 0
                    )
                    """);

                table.HasCheckConstraint(
                    "ck_access_role_assignments_revoked_actor_valid",
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
                            (
                                revoked_by_type = 1
                                AND revoked_by_user_id IS NOT NULL
                                AND revoked_by_process_name IS NULL
                            )
                            OR
                            (
                                revoked_by_type = 2
                                AND revoked_by_user_id IS NULL
                                AND revoked_by_process_name IS NOT NULL
                                AND length(btrim(revoked_by_process_name)) > 0
                            )
                        )
                    )
                    """);
            });

        builder.HasKey(assignment => assignment.Id)
            .HasName("pk_access_role_assignments");

        builder.Property(assignment => assignment.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(assignment => assignment.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(assignment => assignment.Role)
            .HasColumnName("role")
            .IsRequired();

        builder.Property(assignment => assignment.GrantedAt)
            .HasColumnName("granted_at")
            .IsRequired();

        builder.Property(assignment => assignment.GrantedByType)
            .HasColumnName("granted_by_type")
            .IsRequired();

        builder.Property(assignment => assignment.GrantedByUserId)
            .HasColumnName("granted_by_user_id");

        builder.Property(assignment => assignment.GrantedByProcessName)
            .HasColumnName("granted_by_process_name")
            .HasMaxLength(
                AccessTextLimits.ProcessNameMaxLength);

        builder.Property(assignment => assignment.RevokedAt)
            .HasColumnName("revoked_at");

        builder.Property(assignment => assignment.RevokedByType)
            .HasColumnName("revoked_by_type");

        builder.Property(assignment => assignment.RevokedByUserId)
            .HasColumnName("revoked_by_user_id");

        builder.Property(assignment => assignment.RevokedByProcessName)
            .HasColumnName("revoked_by_process_name")
            .HasMaxLength(
                AccessTextLimits.ProcessNameMaxLength);

        builder.HasIndex(
                assignment => new
                {
                    assignment.UserId,
                    assignment.Role
                })
            .IsUnique()
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName(
                ActiveRoleUniqueConstraint);

        builder.HasIndex(
                assignment => new
                {
                    assignment.UserId,
                    assignment.GrantedAt
                })
            .HasDatabaseName(
                "ix_access_role_assignments_user_granted_at");

        builder.HasIndex(
                assignment =>
                    assignment.GrantedByUserId)
            .HasDatabaseName(
                "ix_access_role_assignments_granted_by_user");

        builder.HasIndex(
                assignment =>
                    assignment.RevokedByUserId)
            .HasDatabaseName(
                "ix_access_role_assignments_revoked_by_user");

        builder.HasOne(assignment => assignment.User)
            .WithMany(user => user.RoleAssignments)
            .HasForeignKey(assignment => assignment.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_access_role_assignments_user");

        builder.HasOne<AccessUserEntity>()
            .WithMany()
            .HasForeignKey(
                assignment => assignment.GrantedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_access_role_assignments_granted_by_user");

        builder.HasOne<AccessUserEntity>()
            .WithMany()
            .HasForeignKey(
                assignment => assignment.RevokedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_access_role_assignments_revoked_by_user");
    }
}
