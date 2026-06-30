using System.Reflection;

namespace ModulithReliabilityKit.Modules.Notifications.Application;

/// <summary>
/// Anchor type so the host can register this assembly with MediatR without leaking concrete handlers.
/// </summary>
public static class NotificationsApplicationAssembly
{
    public static readonly Assembly Assembly = typeof(NotificationsApplicationAssembly).Assembly;
}
