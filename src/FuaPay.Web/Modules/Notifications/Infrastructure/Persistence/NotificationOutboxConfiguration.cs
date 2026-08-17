using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Notifications.Infrastructure.Persistence;

internal sealed class NotificationOutboxConfiguration :
    IEntityTypeConfiguration<NotificationOutboxEntity>
{
    public void Configure(EntityTypeBuilder<NotificationOutboxEntity> builder)
    {
        builder.ToTable(
            "outbox",
            "notifications",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_notifications_outbox_id_not_empty",
                    "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_notifications_outbox_recipient_not_empty",
                    "recipient_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_notifications_outbox_attempt_count_nonnegative",
                    "attempt_count >= 0");
            });

        builder.HasKey(item => item.Id).HasName("pk_notifications_outbox");
        builder.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(item => item.RecipientUserId).HasColumnName("recipient_user_id").IsRequired();
        builder.Property(item => item.Type).HasColumnName("type").HasMaxLength(80).IsRequired();
        builder.Property(item => item.Subject).HasColumnName("subject").HasMaxLength(160).IsRequired();
        builder.Property(item => item.Body).HasColumnName("body").HasMaxLength(2000).IsRequired();
        builder.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(item => item.SentAt).HasColumnName("sent_at");
        builder.Property(item => item.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(item => item.LastError).HasColumnName("last_error").HasMaxLength(1000);

        builder.HasIndex(item => new { item.SentAt, item.CreatedAt })
            .HasDatabaseName("ix_notifications_outbox_pending");
        builder.HasIndex(item => new { item.RecipientUserId, item.CreatedAt })
            .HasDatabaseName("ix_notifications_outbox_recipient_created");
    }
}
