using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ModulithReliabilityKit.Modules.Notifications.Infrastructure.ProductAnnouncements;

internal sealed class ProductAnnouncementEntityTypeConfiguration : IEntityTypeConfiguration<ProductAnnouncement>
{
    public void Configure(EntityTypeBuilder<ProductAnnouncement> builder)
    {
        builder.ToTable("product_announcements");

        builder.HasKey(x => x.ProductId);
        builder.Property(x => x.ProductId).HasColumnName("product_id").ValueGeneratedNever();
        builder.Property(x => x.Name).HasColumnName("name").IsRequired();
        builder.Property(x => x.Price).HasColumnName("price");
        builder.Property(x => x.Currency).HasColumnName("currency").IsRequired();
        builder.Property(x => x.AnnouncedOnUtc).HasColumnName("announced_on_utc");
    }
}
