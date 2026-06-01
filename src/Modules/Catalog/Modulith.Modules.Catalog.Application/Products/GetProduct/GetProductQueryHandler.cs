using Modulith.BuildingBlocks.Application.Queries;

namespace Modulith.Modules.Catalog.Application.Products.GetProduct;

public sealed class GetProductQueryHandler : IQueryHandler<GetProductQuery, ProductDto?>
{
    private readonly IProductReadStore _readStore;

    public GetProductQueryHandler(IProductReadStore readStore)
    {
        _readStore = readStore;
    }

    public Task<ProductDto?> Handle(GetProductQuery request, CancellationToken cancellationToken)
        => _readStore.GetByIdAsync(request.ProductId, cancellationToken);
}
