using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Admin.Subscriptions.Queries.GetSubscriptions;
using MediatR;

namespace GisAPI.Application.Features.Admin.Subscriptions.Commands.UpdateSubscription;

public class UpdateSubscriptionCommandHandler : IRequestHandler<UpdateSubscriptionCommand, AdminSubscriptionDto?>
{
    private readonly IGisDbContext _context;

    public UpdateSubscriptionCommandHandler(IGisDbContext context) => _context = context;

    public async Task<AdminSubscriptionDto?> Handle(UpdateSubscriptionCommand request, CancellationToken ct)
    {
        var subscription = await _context.SubscriptionTypes.FindAsync(new object[] { request.Id }, ct);
        if (subscription == null) return null;

        subscription.Name = request.Name;
        subscription.TargetCompanyType = request.Type ?? subscription.TargetCompanyType;
        subscription.YearlyPrice = request.Price;
        subscription.MaxVehicles = request.MaxVehicles;
        subscription.GpsTracking = request.GpsTracking;
        subscription.GpsInstallation = request.GpsInstallation;
        subscription.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return new AdminSubscriptionDto
        {
            Id = subscription.Id,
            Name = subscription.Name,
            Type = subscription.TargetCompanyType,
            Price = subscription.YearlyPrice,
            MaxVehicles = subscription.MaxVehicles,
            GpsTracking = subscription.GpsTracking,
            GpsInstallation = subscription.GpsInstallation,
            Features = GetSubscriptionsQueryHandler.GetFeatures(subscription)
        };
    }
}
