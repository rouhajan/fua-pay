using FuaPay.Web.Modules.Access.Infrastructure.Persistence;
using FuaPay.Web.Modules.Credits.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class PrintCredentialConfiguration :
    IEntityTypeConfiguration<PrintCredentialEntity>
{
    internal const string NormalizedEmailUniqueConstraint =
        "ux_credits_print_credentials_normalized_email";

    public void Configure(EntityTypeBuilder<PrintCredentialEntity> builder)
    {
        builder.ToTable(
            "print_credentials",
            "credits",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_credits_print_credentials_owner_id_not_empty",
                    "owner_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_credits_print_credentials_email_normalized",
                    "normalized_email = lower(btrim(normalized_email)) AND length(normalized_email) > 0");
                table.HasCheckConstraint(
                    "ck_credits_print_credentials_changed_at_valid",
                    "changed_at >= created_at");
                table.HasCheckConstraint(
                    "ck_credits_print_credentials_revoked_at_valid",
                    "revoked_at IS NULL OR revoked_at = changed_at");
                table.HasCheckConstraint(
                    "ck_credits_print_credentials_version_positive",
                    "version > 0");
            });

        builder.HasKey(item => item.OwnerId)
            .HasName("pk_credits_print_credentials");
        builder.Property(item => item.OwnerId)
            .HasColumnName("owner_id")
            .ValueGeneratedNever();
        builder.Property(item => item.NormalizedEmail)
            .HasColumnName("normalized_email")
            .HasMaxLength(PrintCredentialEmail.MaximumLength)
            .IsRequired();
        builder.Property(item => item.CodeHash)
            .HasColumnName("code_hash")
            .HasMaxLength(512)
            .IsRequired();
        builder.Property(item => item.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
        builder.Property(item => item.ChangedAt)
            .HasColumnName("changed_at")
            .IsRequired();
        builder.Property(item => item.RevokedAt)
            .HasColumnName("revoked_at");
        builder.Property(item => item.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .HasDefaultValue(1L)
            .IsRequired();

        builder.HasIndex(item => item.NormalizedEmail)
            .IsUnique()
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName(NormalizedEmailUniqueConstraint);

        builder.HasOne<AccessUserEntity>()
            .WithOne()
            .HasForeignKey<PrintCredentialEntity>(item => item.OwnerId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_credits_print_credentials_access_users_owner_id");
    }
}
