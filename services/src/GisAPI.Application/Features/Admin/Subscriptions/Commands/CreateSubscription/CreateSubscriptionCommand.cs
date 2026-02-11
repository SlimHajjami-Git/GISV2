using MediatR;

namespace GisAPI.Application.Features.Admin.Subscriptions.Commands.CreateSubscription;

public record CreateSubscriptionCommand(
    string Name,
    string? Type,
    decimal Price,
    int MaxVehicles,
    bool GpsTracking,
    bool GpsInstallation
) : IRequest<AdminSubscriptionDto>;
