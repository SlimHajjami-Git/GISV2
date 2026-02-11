using MediatR;

namespace GisAPI.Application.Features.Alerts.Queries.GetAlertUnreadCount;

public record GetAlertUnreadCountQuery(int CompanyId) : IRequest<int>;
