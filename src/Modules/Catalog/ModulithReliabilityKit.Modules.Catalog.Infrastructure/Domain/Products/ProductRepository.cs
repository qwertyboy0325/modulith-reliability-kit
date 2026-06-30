using Microsoft.EntityFrameworkCore;
using ModulithReliabilityKit.Modules.Catalog.Domain.Products;

namespace ModulithReliabilityKit.Modules.Catalog.Infrastructure.Domain.Products;

internal sealed class ProductRepository : IProductRepository
{
    private readonly CatalogContext _context;

    public ProductRepository(CatalogContext context)
    {
        _context = context;
    }

    public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
        => await _context.Products.AddAsync(product, cancellationToken);

    public Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default)
        => _context.Products.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
}
