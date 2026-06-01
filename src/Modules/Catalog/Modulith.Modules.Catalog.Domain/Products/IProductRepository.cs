namespace Modulith.Modules.Catalog.Domain.Products;

public interface IProductRepository
{
    Task AddAsync(Product product, CancellationToken cancellationToken = default);

    Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default);
}
