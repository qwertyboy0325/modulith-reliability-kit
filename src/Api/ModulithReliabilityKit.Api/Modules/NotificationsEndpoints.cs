using ModulithReliabilityKit.Modules.Notifications.Application.Contracts;
using ModulithReliabilityKit.Modules.Notifications.Application.ProductAnnouncements.GetProductAnnouncements;

namespace ModulithReliabilityKit.Api.Modules;

internal static class NotificationsEndpoints
{
    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/notifications").WithTags("Notifications");

        // Demonstrates that a Catalog ProductCreated event was durably consumed by this module.
        group.MapGet("/product-announcements", async (INotificationsModule module, CancellationToken ct) =>
        {
            var announcements = await module.ExecuteQueryAsync(new GetProductAnnouncementsQuery(), ct);
            return Results.Ok(announcements);
        });

        return app;
    }
}
