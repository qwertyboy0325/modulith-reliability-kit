using MediatR;

namespace ModulithReliabilityKit.BuildingBlocks.Application.Queries;

public interface IQuery<out TResult> : IRequest<TResult>;
