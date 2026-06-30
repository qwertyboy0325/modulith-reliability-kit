namespace ModulithReliabilityKit.BuildingBlocks.Application.Queries;

public static class PagedQueryHelper
{
    public static int Offset(IPagedQuery query)
    {
        return Math.Max(query.PageNumber - 1, 0) * query.PageSize;
    }
}
