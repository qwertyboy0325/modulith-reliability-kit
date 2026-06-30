using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModulithReliabilityKit.BuildingBlocks.Application.Inbox;

namespace ModulithReliabilityKit.Modules.Notifications.Infrastructure.Inbox;

internal sealed class InboxMessageEntityTypeConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox_messages");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.LogicalId).HasColumnName("logical_id");
        builder.Property(x => x.OccurredOnUtc).HasColumnName("occurred_on_utc");
        builder.Property(x => x.Type).HasColumnName("type").IsRequired();
        builder.Property(x => x.Payload).HasColumnName("payload").IsRequired();
        builder.Property(x => x.ProcessedOnUtc).HasColumnName("processed_on_utc");
        builder.Property(x => x.RetryCount).HasColumnName("retry_count");
        builder.Property(x => x.LastRetryOnUtc).HasColumnName("last_retry_on_utc");
        builder.Property(x => x.LastError).HasColumnName("last_error");
        builder.Property(x => x.NextRetryOnUtc).HasColumnName("next_retry_on_utc");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();

        // Idempotent ingest boundary: the same integration event can be delivered more than once by
        // the at-least-once bus, but it can exist in the inbox at most once.
        builder.HasIndex(x => new { x.LogicalId, x.OccurredOnUtc })
            .IsUnique()
            .HasDatabaseName("ux_notifications_inbox_logical_occurred");

        builder.HasIndex(x => new { x.Status, x.NextRetryOnUtc, x.OccurredOnUtc })
            .HasDatabaseName("ix_notifications_inbox_pending");
    }
}
