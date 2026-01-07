using MediatR;

namespace GisAPI.Application.Common.Interfaces;

public interface ICommand : IRequest { }

public interface ICommand<out TResponse> : IRequest<TResponse> { }
