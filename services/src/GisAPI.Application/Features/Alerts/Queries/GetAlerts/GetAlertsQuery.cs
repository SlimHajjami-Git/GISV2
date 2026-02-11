using GisAPI.Application.Common.Interfaces;
using MediatR;

namespace GisAPI.Application.Features.Alerts.Queries.GetAlerts;

public record GetAlertsQuery(
    int CompanyId,
    bool? Resolved = null,
    string? Type = null,
    int Limit = 50
) : IRequest<List<AlertDto>>;
