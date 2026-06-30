namespace ModulithReliabilityKit.BuildingBlocks.Application.Queries;

public sealed record PageData<T>(
    IReadOnlyCollection<T> Items,
    int PageNumber,
    int PageSize,
    long TotalCount);
