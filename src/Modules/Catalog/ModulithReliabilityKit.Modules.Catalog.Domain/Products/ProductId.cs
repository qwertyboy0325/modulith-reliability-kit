using ModulithReliabilityKit.BuildingBlocks.Domain;

namespace ModulithReliabilityKit.Modules.Catalog.Domain.Products;

public sealed record ProductId(Guid Value) : StronglyTypedId<Guid>(Value);
