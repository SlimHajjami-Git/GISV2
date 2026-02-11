using MediatR;

namespace GisAPI.Application.Features.Admin.Subscriptions.Commands.UpdateSubscription;

public record UpdateSubscriptionCommand(
    int Id,
    string Name,
    string? Type,
    decimal Price,
    int MaxVehicles,
    bool GpsTracking,
    bool GpsInstallation
) : IRequest<AdminSubscriptionDto?>;
