using MediatR;

namespace GisAPI.Application.Features.Alerts.Commands.ResolveAllAlerts;

public record ResolveAllAlertsCommand(int CompanyId, int UserId) : IRequest<int>;
