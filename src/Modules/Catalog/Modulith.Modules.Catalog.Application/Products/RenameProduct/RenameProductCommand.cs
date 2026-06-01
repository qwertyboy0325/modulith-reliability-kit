using Modulith.BuildingBlocks.Application.Commands;

namespace Modulith.Modules.Catalog.Application.Products.RenameProduct;

public sealed record RenameProductCommand(Guid ProductId, string NewName) : ICommand;
