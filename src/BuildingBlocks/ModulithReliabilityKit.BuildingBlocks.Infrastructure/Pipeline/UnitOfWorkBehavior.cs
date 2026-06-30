using MediatR;
using ModulithReliabilityKit.BuildingBlocks.Application.Commands;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.ModulePersistence;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.Pipeline;

public sealed class UnitOfWorkBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IUnitOfWorkResolver _unitOfWorkResolver;

    public UnitOfWorkBehavior(IUnitOfWorkResolver unitOfWorkResolver)
    {
        _unitOfWorkResolver = unitOfWorkResolver;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next();

        if (IsCommandRequest(typeof(TRequest)))
        {
            var unitOfWork = _unitOfWorkResolver.Resolve(typeof(TRequest));
            await unitOfWork.CommitAsync(cancellationToken);
        }

        return response;
    }

    private static bool IsCommandRequest(Type requestType)
    {
        if (typeof(ICommand).IsAssignableFrom(requestType))
        {
            return true;
        }

        return requestType.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
    }
}
