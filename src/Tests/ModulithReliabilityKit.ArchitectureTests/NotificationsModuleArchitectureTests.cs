using ModulithReliabilityKit.Modules.Notifications.Application;
using ModulithReliabilityKit.Modules.Notifications.Infrastructure.Configuration;
using NetArchTest.Rules;

namespace ModulithReliabilityKit.ArchitectureTests;

/// <summary>
/// Enforces the cross-module boundary for the consumer module: Notifications may depend on Catalog's
/// public <c>IntegrationEvents</c> contract, but never on Catalog's Domain/Application/Infrastructure.
/// </summary>
public class NotificationsModuleArchitectureTests
{
    private static readonly System.Reflection.Assembly NotificationsApplication =
        typeof(NotificationsApplicationAssembly).Assembly;

    private static readonly System.Reflection.Assembly NotificationsInfrastructure =
        typeof(NotificationsModule).Assembly;

    private const string CatalogDomain = "ModulithReliabilityKit.Modules.Catalog.Domain";
    private const string CatalogApplication = "ModulithReliabilityKit.Modules.Catalog.Application";
    private const string CatalogInfrastructure = "ModulithReliabilityKit.Modules.Catalog.Infrastructure";

    [Fact]
    public void Notifications_Application_May_Only_Reach_Catalog_Through_IntegrationEvents()
    {
        var result = Types.InAssembly(NotificationsApplication)
            .ShouldNot()
            .HaveDependencyOnAny(CatalogDomain, CatalogApplication, CatalogInfrastructure)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Notifications_Infrastructure_Must_Not_Reach_Internal_Catalog_Layers()
    {
        var result = Types.InAssembly(NotificationsInfrastructure)
            .ShouldNot()
            .HaveDependencyOnAny(CatalogDomain, CatalogApplication, CatalogInfrastructure)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Notifications_Application_Must_Not_Depend_On_Its_Infrastructure()
    {
        var result = Types.InAssembly(NotificationsApplication)
            .ShouldNot()
            .HaveDependencyOnAny(
                "ModulithReliabilityKit.Modules.Notifications.Infrastructure",
                "Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
