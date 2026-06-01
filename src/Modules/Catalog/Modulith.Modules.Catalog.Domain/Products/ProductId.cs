using Modulith.BuildingBlocks.Domain;

namespace Modulith.Modules.Catalog.Domain.Products;

public sealed record ProductId(Guid Value) : StronglyTypedId<Guid>(Value);
