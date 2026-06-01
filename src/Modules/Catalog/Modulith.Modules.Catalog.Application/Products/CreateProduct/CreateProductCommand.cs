using Modulith.BuildingBlocks.Application.Commands;

namespace Modulith.Modules.Catalog.Application.Products.CreateProduct;

public sealed record CreateProductCommand(string Name, decimal Price, string Currency) : ICommand<Guid>;
