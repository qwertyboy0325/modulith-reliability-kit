namespace ModulithReliabilityKit.Modules.Notifications.Application.ProductAnnouncements.GetProductAnnouncements;

public sealed record ProductAnnouncementDto(
    Guid ProductId,
    string Name,
    decimal Price,
    string Currency,
    DateTime AnnouncedOnUtc);
