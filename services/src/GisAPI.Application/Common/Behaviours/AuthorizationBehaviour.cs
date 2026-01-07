using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;

namespace GisAPI.Application.Common.Behaviours;

public class AuthorizationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ICurrentTenantService _tenantService;

    public AuthorizationBehaviour(ICurrentTenantService tenantService)
    {
        _tenantService = tenantService;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (request is IRequiresPermissions permissionAware)
        {
            if (!_tenantService.IsAuthenticated)
            {
                throw new ForbiddenAccessException();
            }

            foreach (var permission in permissionAware.RequiredPermissions)
            {
                if (!_tenantService.HasPermission(permission))
                {
                    throw new ForbiddenAccessException();
                }
            }
        }

        return await next();
    }
}
