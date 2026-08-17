using FuaPay.Web.Modules.Access.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Access.Infrastructure.Persistence;

internal sealed class ExternalIdentityConfiguration :
    IEntityTypeConfiguration<ExternalIdentityEntity>
{
    internal const string PrimaryKeyConstraint =
        "pk_access_external_identities";

    internal const string UserProviderTenantUniqueConstraint =
        "uq_access_external_identities_user_provider_tenant";

    public void Configure(
        EntityTypeBuilder<ExternalIdentityEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "external_identities",
            "access",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_access_external_identities_provider_not_empty",
                    "length(btrim(provider)) > 0");

                table.HasCheckConstraint(
                    "ck_access_external_identities_tenant_not_empty",
                    "length(btrim(tenant)) > 0");

                table.HasCheckConstraint(
                    "ck_access_external_identities_subject_not_empty",
                    "length(btrim(subject)) > 0");

                table.HasCheckConstraint(
                    "ck_access_external_identities_user_not_empty",
                    "user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            });

        builder.HasKey(
                identity => new
                {
                    identity.Provider,
                    identity.Tenant,
                    identity.Subject
                })
            .HasName(PrimaryKeyConstraint);

        builder.Property(identity => identity.Provider)
            .HasColumnName("provider")
            .HasMaxLength(
                AccessTextLimits.ExternalProviderMaxLength)
            .IsRequired();

        builder.Property(identity => identity.Tenant)
            .HasColumnName("tenant")
            .HasMaxLength(
                AccessTextLimits.ExternalTenantMaxLength)
            .IsRequired();

        builder.Property(identity => identity.Subject)
            .HasColumnName("subject")
            .HasMaxLength(
                AccessTextLimits.ExternalSubjectMaxLength)
            .IsRequired();

        builder.Property(identity => identity.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.HasIndex(identity => identity.UserId)
            .HasDatabaseName(
                "ix_access_external_identities_user");

        builder.HasIndex(
                identity => new
                {
                    identity.UserId,
                    identity.Provider,
                    identity.Tenant
                })
            .IsUnique()
            .HasDatabaseName(
                UserProviderTenantUniqueConstraint);

        builder.HasOne(identity => identity.User)
            .WithMany(user => user.ExternalIdentities)
            .HasForeignKey(identity => identity.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_access_external_identities_user");
    }
}
