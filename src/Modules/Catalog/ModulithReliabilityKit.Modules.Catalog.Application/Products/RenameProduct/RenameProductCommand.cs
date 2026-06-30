using ModulithReliabilityKit.BuildingBlocks.Application.Commands;

namespace ModulithReliabilityKit.Modules.Catalog.Application.Products.RenameProduct;

public sealed record RenameProductCommand(Guid ProductId, string NewName) : ICommand;
