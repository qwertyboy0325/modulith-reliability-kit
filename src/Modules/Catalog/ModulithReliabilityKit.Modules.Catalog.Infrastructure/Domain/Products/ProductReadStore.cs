using Microsoft.EntityFrameworkCore;
using ModulithReliabilityKit.Modules.Catalog.Application.Products.GetProduct;
using ProductId = ModulithReliabilityKit.Modules.Catalog.Domain.Products.ProductId;

namespace ModulithReliabilityKit.Modules.Catalog.Infrastructure.Domain.Products;

internal sealed class ProductReadStore : IProductReadStore
{
    private readonly CatalogContext _context;

    public ProductReadStore(CatalogContext context)
    {
        _context = context;
    }

    public Task<ProductDto?> GetByIdAsync(Guid productId, CancellationToken cancellationToken = default)
        => _context.Products
            .AsNoTracking()
            .Where(x => x.Id == new ProductId(productId))
            .Select(x => new ProductDto(
                x.Id.Value,
                x.Name,
                x.Price.Amount,
                x.Price.Currency,
                x.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
}
