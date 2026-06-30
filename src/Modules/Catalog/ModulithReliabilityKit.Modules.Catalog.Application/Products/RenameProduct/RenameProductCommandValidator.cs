using FluentValidation;

namespace ModulithReliabilityKit.Modules.Catalog.Application.Products.RenameProduct;

public sealed class RenameProductCommandValidator : AbstractValidator<RenameProductCommand>
{
    public RenameProductCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.NewName).NotEmpty().MaximumLength(200);
    }
}
