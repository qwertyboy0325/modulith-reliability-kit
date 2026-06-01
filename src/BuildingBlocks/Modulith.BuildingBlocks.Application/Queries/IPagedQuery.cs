namespace Modulith.BuildingBlocks.Application.Queries;

public interface IPagedQuery
{
    int PageNumber { get; }

    int PageSize { get; }
}
