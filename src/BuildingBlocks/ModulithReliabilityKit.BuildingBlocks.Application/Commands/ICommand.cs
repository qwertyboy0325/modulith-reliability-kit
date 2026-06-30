using MediatR;

namespace ModulithReliabilityKit.BuildingBlocks.Application.Commands;

public interface ICommand : IRequest;

public interface ICommand<out TResult> : IRequest<TResult>;
