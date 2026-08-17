using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Audit.Infrastructure.Persistence;

internal sealed class AuditEventConfiguration :
    IEntityTypeConfiguration<AuditEventEntity>
{
    public void Configure(EntityTypeBuilder<AuditEventEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "events",
            "audit",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_audit_events_id_not_empty",
                    "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_audit_events_actor_consistent",
                    "(actor_user_id IS NOT NULL AND actor_process_name IS NULL) OR " +
                    "(actor_user_id IS NULL AND actor_process_name IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_audit_events_text_not_blank",
                    "length(btrim(action)) > 0 AND " +
                    "length(btrim(entity_type)) > 0 AND " +
                    "length(btrim(entity_id)) > 0 AND " +
                    "length(btrim(description)) > 0");
            });

        builder.HasKey(item => item.Id)
            .HasName("pk_audit_events");

        builder.Property(item => item.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(item => item.OccurredAt)
            .HasColumnName("occurred_at")
            .IsRequired();
        builder.Property(item => item.ActorUserId)
            .HasColumnName("actor_user_id");
        builder.Property(item => item.ActorProcessName)
            .HasColumnName("actor_process_name")
            .HasMaxLength(120);
        builder.Property(item => item.Action)
            .HasColumnName("action")
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(item => item.EntityType)
            .HasColumnName("entity_type")
            .HasMaxLength(80)
            .IsRequired();
        builder.Property(item => item.EntityId)
            .HasColumnName("entity_id")
            .HasMaxLength(160)
            .IsRequired();
        builder.Property(item => item.Description)
            .HasColumnName("description")
            .HasMaxLength(500)
            .IsRequired();

        builder.HasIndex(item => item.OccurredAt)
            .HasDatabaseName("ix_audit_events_occurred_at");
        builder.HasIndex(
                item => new
                {
                    item.EntityType,
                    item.EntityId,
                    item.OccurredAt
                })
            .HasDatabaseName("ix_audit_events_entity");
        builder.HasIndex(
                item => new
                {
                    item.ActorUserId,
                    item.OccurredAt
                })
            .HasDatabaseName("ix_audit_events_actor_user");
    }
}
