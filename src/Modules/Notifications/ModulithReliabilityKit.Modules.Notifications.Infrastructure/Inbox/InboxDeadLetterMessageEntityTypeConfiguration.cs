using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModulithReliabilityKit.BuildingBlocks.Application.Inbox;

namespace ModulithReliabilityKit.Modules.Notifications.Infrastructure.Inbox;

internal sealed class InboxDeadLetterMessageEntityTypeConfiguration : IEntityTypeConfiguration<InboxDeadLetterMessage>
{
    public void Configure(EntityTypeBuilder<InboxDeadLetterMessage> builder)
    {
        builder.ToTable("inbox_dead_letters");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.OriginalMessageId).HasColumnName("original_message_id");
        builder.Property(x => x.Type).HasColumnName("type").IsRequired();
        builder.Property(x => x.Payload).HasColumnName("payload").IsRequired();
        builder.Property(x => x.OccurredOnUtc).HasColumnName("occurred_on_utc");
        builder.Property(x => x.RetryCount).HasColumnName("retry_count");
        builder.Property(x => x.LastError).HasColumnName("last_error").IsRequired();
        builder.Property(x => x.MovedToDeadLetterOnUtc).HasColumnName("moved_to_dead_letter_on_utc");
        builder.Property(x => x.ResolvedOnUtc).HasColumnName("resolved_on_utc");
        builder.Property(x => x.ResolvedBy).HasColumnName("resolved_by");
        builder.Property(x => x.ResolutionNotes).HasColumnName("resolution_notes");
        builder.Property(x => x.ResolutionStatus).HasColumnName("resolution_status").IsRequired();
    }
}
