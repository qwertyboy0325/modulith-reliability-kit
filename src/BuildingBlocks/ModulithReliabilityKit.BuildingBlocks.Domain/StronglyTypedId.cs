namespace ModulithReliabilityKit.BuildingBlocks.Domain;

public abstract record StronglyTypedId<TValue>(TValue Value)
    where TValue : notnull;
