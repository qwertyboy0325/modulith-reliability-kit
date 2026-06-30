using Microsoft.EntityFrameworkCore;
using ModulithReliabilityKit.BuildingBlocks.Application.Inbox;
using ModulithReliabilityKit.Modules.Notifications.Infrastructure.ProductAnnouncements;

namespace ModulithReliabilityKit.Modules.Notifications.Infrastructure;

public sealed class NotificationsContext : DbContext
{
    public const string Schema = "notifications";

    public NotificationsContext(DbContextOptions<NotificationsContext> options)
        : base(options)
    {
    }

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<InboxDeadLetterMessage> InboxDeadLetters => Set<InboxDeadLetterMessage>();

    internal DbSet<ProductAnnouncement> ProductAnnouncements => Set<ProductAnnouncement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsContext).Assembly);
    }
}
