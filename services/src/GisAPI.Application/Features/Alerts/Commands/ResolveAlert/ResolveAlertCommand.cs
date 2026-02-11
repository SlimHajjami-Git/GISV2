using MediatR;

namespace GisAPI.Application.Features.Alerts.Commands.ResolveAlert;

public record ResolveAlertCommand(int AlertId, int UserId) : IRequest<bool>;
