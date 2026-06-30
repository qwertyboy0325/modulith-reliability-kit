using ModulithReliabilityKit.BuildingBlocks.Application;
using ModulithReliabilityKit.BuildingBlocks.Application.Commands;
using ModulithReliabilityKit.Modules.Catalog.Domain.Products;

namespace ModulithReliabilityKit.Modules.Catalog.Application.Products.RenameProduct;

public sealed class RenameProductCommandHandler : ICommandHandler<RenameProductCommand>
{
    private readonly IProductRepository _productRepository;

    public RenameProductCommandHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task Handle(RenameProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(request.ProductId), cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Product), request.ProductId);

        product.Rename(request.NewName);
    }
}
