namespace Modulith.Modules.Catalog.Application.Products.GetProduct;

/// <summary>
/// Read-side port. Queries bypass the aggregate/repository and read projections directly
/// (Dapper/EF read model), so the read path stays decoupled from the write model.
/// Implemented in Infrastructure.
/// </summary>
public interface IProductReadStore
{
    Task<ProductDto?> GetByIdAsync(Guid productId, CancellationToken cancellationToken = default);
}
