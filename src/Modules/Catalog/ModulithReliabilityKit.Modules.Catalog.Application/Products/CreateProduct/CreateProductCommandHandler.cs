using ModulithReliabilityKit.BuildingBlocks.Application.Commands;
using ModulithReliabilityKit.Modules.Catalog.Domain.Products;

namespace ModulithReliabilityKit.Modules.Catalog.Application.Products.CreateProduct;

public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    private readonly IProductRepository _productRepository;

    public CreateProductCommandHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Guid> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var product = Product.Create(request.Name, Money.Of(request.Price, request.Currency));

        await _productRepository.AddAsync(product, cancellationToken);

        // No SaveChanges/commit here: the UnitOfWorkBehavior commits the transaction
        // (dispatch domain events -> SaveChanges) after the handler returns.
        return product.Id.Value;
    }
}
