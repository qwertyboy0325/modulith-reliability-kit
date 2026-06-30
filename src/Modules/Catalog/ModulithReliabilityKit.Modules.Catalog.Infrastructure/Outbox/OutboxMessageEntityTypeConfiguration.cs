using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModulithReliabilityKit.BuildingBlocks.Application.Outbox;

namespace ModulithReliabilityKit.Modules.Catalog.Infrastructure.Outbox;

internal sealed class OutboxMessageEntityTypeConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.LogicalId).HasColumnName("logical_id");
        builder.Property(x => x.OccurredOnUtc).HasColumnName("occurred_on_utc");
        builder.Property(x => x.Type).HasColumnName("type").IsRequired();
        builder.Property(x => x.Payload).HasColumnName("payload").IsRequired();
        builder.Property(x => x.ProcessedOnUtc).HasColumnName("processed_on_utc");

        builder.HasIndex(x => x.ProcessedOnUtc).HasDatabaseName("ix_outbox_messages_unprocessed");
    }
}
