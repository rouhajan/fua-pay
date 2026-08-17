using FuaPay.Web.Modules.Access.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Access.Infrastructure.Persistence;

internal sealed class AccessUserConfiguration :
    IEntityTypeConfiguration<AccessUserEntity>
{
    public void Configure(
        EntityTypeBuilder<AccessUserEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "users",
            "access",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_access_users_id_not_empty",
                    "id <> '00000000-0000-0000-0000-000000000000'::uuid");

                table.HasCheckConstraint(
                    "ck_access_users_display_name_not_empty",
                    "length(btrim(display_name)) > 0");

                table.HasCheckConstraint(
                    "ck_access_users_email_not_empty",
                    "email IS NULL OR length(btrim(email)) > 0");

                table.HasCheckConstraint(
                    "ck_access_users_status_valid",
                    "status IN (1, 2)");

                table.HasCheckConstraint(
                    "ck_access_users_last_seen_valid",
                    "last_seen_at >= created_at");

                table.HasCheckConstraint(
                    "ck_access_users_version_positive",
                    "version > 0");
            });

        builder.HasKey(user => user.Id)
            .HasName("pk_access_users");

        builder.Property(user => user.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(user => user.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(
                AccessTextLimits.DisplayNameMaxLength)
            .IsRequired();

        builder.Property(user => user.Email)
            .HasColumnName("email")
            .HasMaxLength(
                AccessTextLimits.EmailMaxLength);

        builder.Property(user => user.Status)
            .HasColumnName("status")
            .IsRequired();

        builder.Property(user => user.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(user => user.LastSeenAt)
            .HasColumnName("last_seen_at")
            .IsRequired();

        builder.Property(user => user.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(user => user.Email)
            .HasDatabaseName("ix_access_users_email");
    }
}
