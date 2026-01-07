using MediatR;

namespace GisAPI.Application.Common.Interfaces;

public interface IQuery<out TResponse> : IRequest<TResponse> { }
