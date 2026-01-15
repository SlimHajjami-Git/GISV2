using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Admin.SubscriptionTypes.Queries.GetSubscriptionTypes;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.SubscriptionTypes.Commands.UpdateSubscriptionType;

public record UpdateSubscriptionTypeCommand(
    int Id,
    string? Name,
    string? Description,
    string? TargetCompanyType,
    decimal? MonthlyPrice,
    decimal? QuarterlyPrice,
    decimal? YearlyPrice,
    int? MonthlyDurationDays,
    int? QuarterlyDurationDays,
    int? YearlyDurationDays,
    int? MaxVehicles,
    int? MaxUsers,
    int? MaxGpsDevices,
    int? MaxGeofences,
    bool? GpsTracking,
    bool? GpsInstallation,
    bool? ApiAccess,
    bool? AdvancedReports,
    bool? RealTimeAlerts,
    bool? HistoryPlayback,
    bool? FuelAnalysis,
    bool? DrivingBehavior,
    int? HistoryRetentionDays,
    int? SortOrder,
    bool? IsActive
) : IRequest<SubscriptionTypeDto>;

public class UpdateSubscriptionTypeCommandHandler : IRequestHandler<UpdateSubscriptionTypeCommand, SubscriptionTypeDto>
{
    private readonly IGisDbContext _context;

    public UpdateSubscriptionTypeCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<SubscriptionTypeDto> Handle(UpdateSubscriptionTypeCommand request, CancellationToken cancellationToken)
    {
        var subscriptionType = await _context.SubscriptionTypes
            .FirstOrDefaultAsync(st => st.Id == request.Id, cancellationToken);

        if (subscriptionType == null)
        {
            throw new InvalidOperationException("Type d'abonnement non trouvé");
        }

        if (!string.IsNullOrEmpty(request.Name)) subscriptionType.Name = request.Name;
        if (!string.IsNullOrEmpty(request.Description)) subscriptionType.Description = request.Description;
        if (!string.IsNullOrEmpty(request.TargetCompanyType)) subscriptionType.TargetCompanyType = request.TargetCompanyType;
        
        if (request.MonthlyPrice.HasValue) subscriptionType.MonthlyPrice = request.MonthlyPrice.Value;
        if (request.QuarterlyPrice.HasValue) subscriptionType.QuarterlyPrice = request.QuarterlyPrice.Value;
        if (request.YearlyPrice.HasValue) subscriptionType.YearlyPrice = request.YearlyPrice.Value;
        
        if (request.MonthlyDurationDays.HasValue) subscriptionType.MonthlyDurationDays = request.MonthlyDurationDays.Value;
        if (request.QuarterlyDurationDays.HasValue) subscriptionType.QuarterlyDurationDays = request.QuarterlyDurationDays.Value;
        if (request.YearlyDurationDays.HasValue) subscriptionType.YearlyDurationDays = request.YearlyDurationDays.Value;
        
        if (request.MaxVehicles.HasValue) subscriptionType.MaxVehicles = request.MaxVehicles.Value;
        if (request.MaxUsers.HasValue) subscriptionType.MaxUsers = request.MaxUsers.Value;
        if (request.MaxGpsDevices.HasValue) subscriptionType.MaxGpsDevices = request.MaxGpsDevices.Value;
        if (request.MaxGeofences.HasValue) subscriptionType.MaxGeofences = request.MaxGeofences.Value;
        
        if (request.GpsTracking.HasValue) subscriptionType.GpsTracking = request.GpsTracking.Value;
        if (request.GpsInstallation.HasValue) subscriptionType.GpsInstallation = request.GpsInstallation.Value;
        if (request.ApiAccess.HasValue) subscriptionType.ApiAccess = request.ApiAccess.Value;
        if (request.AdvancedReports.HasValue) subscriptionType.AdvancedReports = request.AdvancedReports.Value;
        if (request.RealTimeAlerts.HasValue) subscriptionType.RealTimeAlerts = request.RealTimeAlerts.Value;
        if (request.HistoryPlayback.HasValue) subscriptionType.HistoryPlayback = request.HistoryPlayback.Value;
        if (request.FuelAnalysis.HasValue) subscriptionType.FuelAnalysis = request.FuelAnalysis.Value;
        if (request.DrivingBehavior.HasValue) subscriptionType.DrivingBehavior = request.DrivingBehavior.Value;
        
        if (request.HistoryRetentionDays.HasValue) subscriptionType.HistoryRetentionDays = request.HistoryRetentionDays.Value;
        if (request.SortOrder.HasValue) subscriptionType.SortOrder = request.SortOrder.Value;
        if (request.IsActive.HasValue) subscriptionType.IsActive = request.IsActive.Value;

        subscriptionType.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return new SubscriptionTypeDto
        {
            Id = subscriptionType.Id,
            Name = subscriptionType.Name,
            Code = subscriptionType.Code,
            Description = subscriptionType.Description,
            TargetCompanyType = subscriptionType.TargetCompanyType,
            MonthlyPrice = subscriptionType.MonthlyPrice,
            QuarterlyPrice = subscriptionType.QuarterlyPrice,
            YearlyPrice = subscriptionType.YearlyPrice,
            MonthlyDurationDays = subscriptionType.MonthlyDurationDays,
            QuarterlyDurationDays = subscriptionType.QuarterlyDurationDays,
            YearlyDurationDays = subscriptionType.YearlyDurationDays,
            MaxVehicles = subscriptionType.MaxVehicles,
            MaxUsers = subscriptionType.MaxUsers,
            MaxGpsDevices = subscriptionType.MaxGpsDevices,
            MaxGeofences = subscriptionType.MaxGeofences,
            GpsTracking = subscriptionType.GpsTracking,
            GpsInstallation = subscriptionType.GpsInstallation,
            ApiAccess = subscriptionType.ApiAccess,
            AdvancedReports = subscriptionType.AdvancedReports,
            RealTimeAlerts = subscriptionType.RealTimeAlerts,
            HistoryPlayback = subscriptionType.HistoryPlayback,
            FuelAnalysis = subscriptionType.FuelAnalysis,
            DrivingBehavior = subscriptionType.DrivingBehavior,
            HistoryRetentionDays = subscriptionType.HistoryRetentionDays,
            SortOrder = subscriptionType.SortOrder,
            IsActive = subscriptionType.IsActive,
            CreatedAt = subscriptionType.CreatedAt,
            UpdatedAt = subscriptionType.UpdatedAt
        };
    }
}
