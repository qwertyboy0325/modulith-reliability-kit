using MediatR;

namespace Modulith.BuildingBlocks.Application.Commands;

public interface ICommand : IRequest;

public interface ICommand<out TResult> : IRequest<TResult>;
