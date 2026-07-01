using ModulithReliabilityKit.Modules.Notifications.Application.Contracts;
using ModulithReliabilityKit.Modules.Notifications.Application.Inbox;
using ModulithReliabilityKit.Modules.Notifications.Application.Inbox.GetInboxDeadLetters;
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

        // Operational surface for the dead-letter recovery loop: inspect poisoned messages ...
        group.MapGet("/inbox/dead-letters", async (
            INotificationsModule module,
            bool? includeResolved,
            CancellationToken ct) =>
        {
            var deadLetters = await module.ExecuteQueryAsync(
                new GetInboxDeadLettersQuery(includeResolved ?? false), ct);
            return Results.Ok(deadLetters);
        });

        // ... then requeue one for another drain once the downstream cause is fixed.
        group.MapPost("/inbox/dead-letters/{id:guid}/reprocess", async (
            Guid id,
            string? requestedBy,
            INotificationsModule module,
            CancellationToken ct) =>
        {
            var result = await module.ReprocessDeadLetterAsync(id, requestedBy ?? "api", ct);

            return result.Outcome switch
            {
                ReprocessDeadLetterOutcome.NotFound => Results.NotFound(result),
                _ => Results.Ok(result),
            };
        });

        return app;
    }
}
