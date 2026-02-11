using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Subscriptions.Queries.GetSubscriptions;

public class GetSubscriptionsQueryHandler : IRequestHandler<GetSubscriptionsQuery, List<AdminSubscriptionDto>>
{
    private readonly IGisDbContext _context;

    public GetSubscriptionsQueryHandler(IGisDbContext context) => _context = context;

    public async Task<List<AdminSubscriptionDto>> Handle(GetSubscriptionsQuery request, CancellationToken ct)
    {
        return await _context.SubscriptionTypes
            .Where(s => s.IsActive)
            .OrderBy(s => s.YearlyPrice)
            .Select(s => new AdminSubscriptionDto
            {
                Id = s.Id,
                Name = s.Name,
                Type = s.TargetCompanyType,
                Price = s.YearlyPrice,
                MaxVehicles = s.MaxVehicles,
                GpsTracking = s.GpsTracking,
                GpsInstallation = s.GpsInstallation,
                Features = GetFeatures(s)
            })
            .ToListAsync(ct);
    }

    public static List<string> GetFeatures(SubscriptionType s)
    {
        var features = new List<string> { "Gestion du parc" };
        if (s.GpsTracking) features.Add("Suivi GPS temps réel");
        if (s.GpsInstallation) features.Add("Installation GPS");
        if (s.MaxVehicles >= 50) features.Add("Rapports avancés");
        if (s.MaxVehicles >= 100) features.Add("Géofencing");
        if (s.GpsInstallation) features.Add("Support prioritaire");
        return features;
    }
}
