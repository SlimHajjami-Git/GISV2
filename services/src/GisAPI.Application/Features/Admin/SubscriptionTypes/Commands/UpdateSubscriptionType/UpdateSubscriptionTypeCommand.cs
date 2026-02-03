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
    bool? IsActive,
    // Module permissions
    bool? ModuleDashboard,
    bool? ModuleMonitoring,
    bool? ModuleVehicles,
    bool? ModuleEmployees,
    bool? ModuleGeofences,
    bool? ModuleMaintenance,
    bool? ModuleCosts,
    bool? ModuleReports,
    bool? ModuleSettings,
    bool? ModuleUsers,
    bool? ModuleSuppliers,
    bool? ModuleDocuments,
    bool? ModuleAccidents,
    bool? ModuleFleetManagement,
    // Report permissions
    bool? ReportTrips,
    bool? ReportFuel,
    bool? ReportSpeed,
    bool? ReportStops,
    bool? ReportMileage,
    bool? ReportCosts,
    bool? ReportMaintenance,
    bool? ReportDaily,
    bool? ReportMonthly,
    bool? ReportMileagePeriod,
    bool? ReportSpeedInfraction,
    bool? ReportDrivingBehavior
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
        
        // Module permissions
        if (request.ModuleDashboard.HasValue) subscriptionType.ModuleDashboard = request.ModuleDashboard.Value;
        if (request.ModuleMonitoring.HasValue) subscriptionType.ModuleMonitoring = request.ModuleMonitoring.Value;
        if (request.ModuleVehicles.HasValue) subscriptionType.ModuleVehicles = request.ModuleVehicles.Value;
        if (request.ModuleEmployees.HasValue) subscriptionType.ModuleEmployees = request.ModuleEmployees.Value;
        if (request.ModuleGeofences.HasValue) subscriptionType.ModuleGeofences = request.ModuleGeofences.Value;
        if (request.ModuleMaintenance.HasValue) subscriptionType.ModuleMaintenance = request.ModuleMaintenance.Value;
        if (request.ModuleCosts.HasValue) subscriptionType.ModuleCosts = request.ModuleCosts.Value;
        if (request.ModuleReports.HasValue) subscriptionType.ModuleReports = request.ModuleReports.Value;
        if (request.ModuleSettings.HasValue) subscriptionType.ModuleSettings = request.ModuleSettings.Value;
        if (request.ModuleUsers.HasValue) subscriptionType.ModuleUsers = request.ModuleUsers.Value;
        if (request.ModuleSuppliers.HasValue) subscriptionType.ModuleSuppliers = request.ModuleSuppliers.Value;
        if (request.ModuleDocuments.HasValue) subscriptionType.ModuleDocuments = request.ModuleDocuments.Value;
        if (request.ModuleAccidents.HasValue) subscriptionType.ModuleAccidents = request.ModuleAccidents.Value;
        if (request.ModuleFleetManagement.HasValue) subscriptionType.ModuleFleetManagement = request.ModuleFleetManagement.Value;
        
        // Report permissions
        if (request.ReportTrips.HasValue) subscriptionType.ReportTrips = request.ReportTrips.Value;
        if (request.ReportFuel.HasValue) subscriptionType.ReportFuel = request.ReportFuel.Value;
        if (request.ReportSpeed.HasValue) subscriptionType.ReportSpeed = request.ReportSpeed.Value;
        if (request.ReportStops.HasValue) subscriptionType.ReportStops = request.ReportStops.Value;
        if (request.ReportMileage.HasValue) subscriptionType.ReportMileage = request.ReportMileage.Value;
        if (request.ReportCosts.HasValue) subscriptionType.ReportCosts = request.ReportCosts.Value;
        if (request.ReportMaintenance.HasValue) subscriptionType.ReportMaintenance = request.ReportMaintenance.Value;
        if (request.ReportDaily.HasValue) subscriptionType.ReportDaily = request.ReportDaily.Value;
        if (request.ReportMonthly.HasValue) subscriptionType.ReportMonthly = request.ReportMonthly.Value;
        if (request.ReportMileagePeriod.HasValue) subscriptionType.ReportMileagePeriod = request.ReportMileagePeriod.Value;
        if (request.ReportSpeedInfraction.HasValue) subscriptionType.ReportSpeedInfraction = request.ReportSpeedInfraction.Value;
        if (request.ReportDrivingBehavior.HasValue) subscriptionType.ReportDrivingBehavior = request.ReportDrivingBehavior.Value;

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
            // Module permissions
            ModuleDashboard = subscriptionType.ModuleDashboard,
            ModuleMonitoring = subscriptionType.ModuleMonitoring,
            ModuleVehicles = subscriptionType.ModuleVehicles,
            ModuleEmployees = subscriptionType.ModuleEmployees,
            ModuleGeofences = subscriptionType.ModuleGeofences,
            ModuleMaintenance = subscriptionType.ModuleMaintenance,
            ModuleCosts = subscriptionType.ModuleCosts,
            ModuleReports = subscriptionType.ModuleReports,
            ModuleSettings = subscriptionType.ModuleSettings,
            ModuleUsers = subscriptionType.ModuleUsers,
            ModuleSuppliers = subscriptionType.ModuleSuppliers,
            ModuleDocuments = subscriptionType.ModuleDocuments,
            ModuleAccidents = subscriptionType.ModuleAccidents,
            ModuleFleetManagement = subscriptionType.ModuleFleetManagement,
            // Report permissions
            ReportTrips = subscriptionType.ReportTrips,
            ReportFuel = subscriptionType.ReportFuel,
            ReportSpeed = subscriptionType.ReportSpeed,
            ReportStops = subscriptionType.ReportStops,
            ReportMileage = subscriptionType.ReportMileage,
            ReportCosts = subscriptionType.ReportCosts,
            ReportMaintenance = subscriptionType.ReportMaintenance,
            ReportDaily = subscriptionType.ReportDaily,
            ReportMonthly = subscriptionType.ReportMonthly,
            ReportMileagePeriod = subscriptionType.ReportMileagePeriod,
            ReportSpeedInfraction = subscriptionType.ReportSpeedInfraction,
            ReportDrivingBehavior = subscriptionType.ReportDrivingBehavior,
            CreatedAt = subscriptionType.CreatedAt,
            UpdatedAt = subscriptionType.UpdatedAt
        };
    }
}



