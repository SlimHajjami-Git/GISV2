using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Admin.SubscriptionTypes.Queries.GetSubscriptionTypes;
using GisAPI.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.SubscriptionTypes.Commands.CreateSubscriptionType;

public record CreateSubscriptionTypeCommand(
    string Name,
    string Code,
    string? Description,
    string? TargetCompanyType,
    decimal MonthlyPrice,
    decimal? QuarterlyPrice,
    decimal? YearlyPrice,
    int? MonthlyDurationDays,
    int? QuarterlyDurationDays,
    int? YearlyDurationDays,
    int? MaxVehicles,
    int? MaxUsers,
    int? MaxGpsDevices,
    int? MaxGeofences,
    bool GpsTracking,
    bool GpsInstallation,
    bool ApiAccess,
    bool AdvancedReports,
    bool? RealTimeAlerts,
    bool? HistoryPlayback,
    bool FuelAnalysis,
    bool DrivingBehavior,
    int? HistoryRetentionDays,
    int? SortOrder
) : IRequest<SubscriptionTypeDto>;

public class CreateSubscriptionTypeCommandHandler : IRequestHandler<CreateSubscriptionTypeCommand, SubscriptionTypeDto>
{
    private readonly IGisDbContext _context;

    public CreateSubscriptionTypeCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<SubscriptionTypeDto> Handle(CreateSubscriptionTypeCommand request, CancellationToken cancellationToken)
    {
        // Check for duplicate code
        if (await _context.SubscriptionTypes.AnyAsync(st => st.Code == request.Code, cancellationToken))
        {
            throw new InvalidOperationException($"Un type d'abonnement avec le code '{request.Code}' existe déjà");
        }

        var subscriptionType = new SubscriptionType
        {
            Name = request.Name,
            Code = request.Code,
            Description = request.Description,
            TargetCompanyType = request.TargetCompanyType ?? "all",
            MonthlyPrice = request.MonthlyPrice,
            QuarterlyPrice = request.QuarterlyPrice ?? request.MonthlyPrice * 3 * 0.9m,
            YearlyPrice = request.YearlyPrice ?? request.MonthlyPrice * 12 * 0.8m,
            MonthlyDurationDays = request.MonthlyDurationDays ?? 30,
            QuarterlyDurationDays = request.QuarterlyDurationDays ?? 90,
            YearlyDurationDays = request.YearlyDurationDays ?? 365,
            MaxVehicles = request.MaxVehicles ?? 10,
            MaxUsers = request.MaxUsers ?? 5,
            MaxGpsDevices = request.MaxGpsDevices ?? 10,
            MaxGeofences = request.MaxGeofences ?? 20,
            GpsTracking = request.GpsTracking,
            GpsInstallation = request.GpsInstallation,
            ApiAccess = request.ApiAccess,
            AdvancedReports = request.AdvancedReports,
            RealTimeAlerts = request.RealTimeAlerts ?? true,
            HistoryPlayback = request.HistoryPlayback ?? true,
            FuelAnalysis = request.FuelAnalysis,
            DrivingBehavior = request.DrivingBehavior,
            HistoryRetentionDays = request.HistoryRetentionDays ?? 30,
            SortOrder = request.SortOrder ?? 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.SubscriptionTypes.Add(subscriptionType);
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
