using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Common.Behaviours;

public class LoggingBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehaviour<TRequest, TResponse>> _logger;
    private readonly ICurrentTenantService _tenantService;

    public LoggingBehaviour(ILogger<LoggingBehaviour<TRequest, TResponse>> logger, ICurrentTenantService tenantService)
    {
        _logger = logger;
        _tenantService = tenantService;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;
        var userId = _tenantService.UserId;
        var companyId = _tenantService.CompanyId;

        _logger.LogInformation("GisAPI Request: {Name} UserId: {UserId} CompanyId: {CompanyId} {@Request}",
            requestName, userId, companyId, request);

        var response = await next();

        _logger.LogInformation("GisAPI Response: {Name} {@Response}", requestName, response);

        return response;
    }
}
