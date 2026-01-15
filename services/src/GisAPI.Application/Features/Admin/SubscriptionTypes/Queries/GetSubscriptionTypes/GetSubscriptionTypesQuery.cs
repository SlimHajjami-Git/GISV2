using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.SubscriptionTypes.Queries.GetSubscriptionTypes;

public record GetSubscriptionTypesQuery(string? CompanyType) : IRequest<List<SubscriptionTypeDto>>;

public class GetSubscriptionTypesQueryHandler : IRequestHandler<GetSubscriptionTypesQuery, List<SubscriptionTypeDto>>
{
    private readonly IGisDbContext _context;

    public GetSubscriptionTypesQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<List<SubscriptionTypeDto>> Handle(GetSubscriptionTypesQuery request, CancellationToken cancellationToken)
    {
        var query = _context.SubscriptionTypes
            .Where(st => st.IsActive)
            .OrderBy(st => st.SortOrder)
            .ThenBy(st => st.MonthlyPrice)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrEmpty(request.CompanyType) && request.CompanyType != "all")
        {
            query = query.Where(st => st.TargetCompanyType == "all" || st.TargetCompanyType == request.CompanyType);
        }

        var types = await query.ToListAsync(cancellationToken);

        return types.Select(st => new SubscriptionTypeDto
        {
            Id = st.Id,
            Name = st.Name,
            Code = st.Code,
            Description = st.Description,
            TargetCompanyType = st.TargetCompanyType,
            MonthlyPrice = st.MonthlyPrice,
            QuarterlyPrice = st.QuarterlyPrice,
            YearlyPrice = st.YearlyPrice,
            MonthlyDurationDays = st.MonthlyDurationDays,
            QuarterlyDurationDays = st.QuarterlyDurationDays,
            YearlyDurationDays = st.YearlyDurationDays,
            MaxVehicles = st.MaxVehicles,
            MaxUsers = st.MaxUsers,
            MaxGpsDevices = st.MaxGpsDevices,
            MaxGeofences = st.MaxGeofences,
            GpsTracking = st.GpsTracking,
            GpsInstallation = st.GpsInstallation,
            ApiAccess = st.ApiAccess,
            AdvancedReports = st.AdvancedReports,
            RealTimeAlerts = st.RealTimeAlerts,
            HistoryPlayback = st.HistoryPlayback,
            FuelAnalysis = st.FuelAnalysis,
            DrivingBehavior = st.DrivingBehavior,
            HistoryRetentionDays = st.HistoryRetentionDays,
            SortOrder = st.SortOrder,
            IsActive = st.IsActive,
            CreatedAt = st.CreatedAt,
            UpdatedAt = st.UpdatedAt
        }).ToList();
    }
}
