using ModulithReliabilityKit.BuildingBlocks.Application.Commands;
using ModulithReliabilityKit.Modules.Catalog.Application.Products.CreateProduct;
using ModulithReliabilityKit.Modules.Catalog.Domain.Products;
using ModulithReliabilityKit.Modules.Catalog.Infrastructure;
using NetArchTest.Rules;

namespace ModulithReliabilityKit.ArchitectureTests;

public class ArchitectureRulesTests
{
    private static readonly System.Reflection.Assembly CatalogDomain = typeof(Product).Assembly;
    private static readonly System.Reflection.Assembly CatalogApplication = typeof(CreateProductCommand).Assembly;
    private static readonly System.Reflection.Assembly CatalogInfrastructure = typeof(CatalogContext).Assembly;

    [Fact]
    public void Domain_Should_Not_Depend_On_MediatR()
    {
        var result = Types.InAssembly(typeof(ModulithReliabilityKit.BuildingBlocks.Domain.Entity).Assembly)
            .ShouldNot()
            .HaveDependencyOn("MediatR")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void Domain_Should_Not_Depend_On_EntityFrameworkCore()
    {
        var result = Types.InAssembly(typeof(ModulithReliabilityKit.BuildingBlocks.Domain.Entity).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void Application_Should_Not_Depend_On_Infrastructure()
    {
        var result = Types.InAssembly(typeof(ICommand).Assembly)
            .ShouldNot()
            .HaveDependencyOn("ModulithReliabilityKit.BuildingBlocks.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void Module_Domain_Should_Not_Depend_On_MediatR_Or_EfCore()
    {
        var result = Types.InAssembly(CatalogDomain)
            .ShouldNot()
            .HaveDependencyOnAny("MediatR", "Microsoft.EntityFrameworkCore", "FluentValidation")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Module_Application_Should_Not_Depend_On_Module_Infrastructure()
    {
        var result = Types.InAssembly(CatalogApplication)
            .ShouldNot()
            .HaveDependencyOnAny(
                "ModulithReliabilityKit.Modules.Catalog.Infrastructure",
                "ModulithReliabilityKit.BuildingBlocks.Infrastructure",
                "Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Module_Domain_Should_Not_Depend_On_Module_Application_Or_Infrastructure()
    {
        var result = Types.InAssembly(CatalogDomain)
            .ShouldNot()
            .HaveDependencyOnAny(
                "ModulithReliabilityKit.Modules.Catalog.Application",
                "ModulithReliabilityKit.Modules.Catalog.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void IntegrationEvents_Is_The_Only_Catalog_Type_Other_Modules_May_Reference()
    {
        // The contract assembly must not leak domain/application/infrastructure types.
        var result = Types.InAssembly(typeof(ModulithReliabilityKit.Modules.Catalog.IntegrationEvents.ProductCreatedIntegrationEvent).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "ModulithReliabilityKit.Modules.Catalog.Domain",
                "ModulithReliabilityKit.Modules.Catalog.Application",
                "ModulithReliabilityKit.Modules.Catalog.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}