using MediatR;

namespace Modulith.BuildingBlocks.Application.Queries;

public interface IQuery<out TResult> : IRequest<TResult>;
