using FluentValidation;
using MediatR;
using ModulithReliabilityKit.BuildingBlocks.Application;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.Pipeline;

public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IReadOnlyCollection<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators.ToList();
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (_validators.Count == 0)
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var errors = validationResults
            .SelectMany(vr => vr.Errors)
            .Where(e => e is not null)
            .Select(e => e.ErrorMessage)
            .Distinct()
            .ToArray();

        if (errors.Length > 0)
        {
            throw new InvalidCommandException(errors);
        }

        return await next();
    }
}
