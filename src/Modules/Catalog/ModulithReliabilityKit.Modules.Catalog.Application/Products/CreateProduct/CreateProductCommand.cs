using ModulithReliabilityKit.BuildingBlocks.Application.Commands;

namespace ModulithReliabilityKit.Modules.Catalog.Application.Products.CreateProduct;

public sealed record CreateProductCommand(string Name, decimal Price, string Currency) : ICommand<Guid>;
