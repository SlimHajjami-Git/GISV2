using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Admin.Subscriptions.Queries.GetSubscriptions;
using GisAPI.Domain.Entities;
using MediatR;

namespace GisAPI.Application.Features.Admin.Subscriptions.Commands.CreateSubscription;

public class CreateSubscriptionCommandHandler : IRequestHandler<CreateSubscriptionCommand, AdminSubscriptionDto>
{
    private readonly IGisDbContext _context;

    public CreateSubscriptionCommandHandler(IGisDbContext context) => _context = context;

    public async Task<AdminSubscriptionDto> Handle(CreateSubscriptionCommand request, CancellationToken ct)
    {
        var subscription = new SubscriptionType
        {
            Name = request.Name,
            Code = request.Name.ToLower().Replace(" ", "-"),
            TargetCompanyType = request.Type ?? "all",
            YearlyPrice = request.Price,
            MaxVehicles = request.MaxVehicles,
            GpsTracking = request.GpsTracking,
            GpsInstallation = request.GpsInstallation,
            IsActive = true
        };

        _context.SubscriptionTypes.Add(subscription);
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
