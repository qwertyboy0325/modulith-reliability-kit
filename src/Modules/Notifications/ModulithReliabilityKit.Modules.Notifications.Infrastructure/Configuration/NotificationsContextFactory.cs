using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ModulithReliabilityKit.Modules.Notifications.Infrastructure.Configuration;

internal sealed class NotificationsContextFactory : IDesignTimeDbContextFactory<NotificationsContext>
{
    public NotificationsContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NOTIFICATIONS_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=modulith_reliability_kit;Username=modulith_reliability_kit;Password=modulith_reliability_kit";

        var optionsBuilder = new DbContextOptionsBuilder<NotificationsContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new NotificationsContext(optionsBuilder.Options);
    }
}
