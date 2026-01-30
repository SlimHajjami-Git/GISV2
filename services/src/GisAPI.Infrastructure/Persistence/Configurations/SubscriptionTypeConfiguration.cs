using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class SubscriptionTypeConfiguration : IEntityTypeConfiguration<SubscriptionType>
{
    public void Configure(EntityTypeBuilder<SubscriptionType> builder)
    {
        builder.ToTable("subscription_types");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).IsRequired().HasMaxLength(100);
        builder.Property(s => s.Code).IsRequired().HasMaxLength(50);
        builder.Property(s => s.Description).HasMaxLength(500);
        builder.Property(s => s.TargetCompanyType).HasMaxLength(50).HasColumnName("target_company_type");

        // Pricing
        builder.Property(s => s.MonthlyPrice).HasColumnName("monthly_price");
        builder.Property(s => s.QuarterlyPrice).HasColumnName("quarterly_price");
        builder.Property(s => s.YearlyPrice).HasColumnName("yearly_price");
        builder.Property(s => s.MonthlyDurationDays).HasColumnName("monthly_duration_days");
        builder.Property(s => s.QuarterlyDurationDays).HasColumnName("quarterly_duration_days");
        builder.Property(s => s.YearlyDurationDays).HasColumnName("yearly_duration_days");

        // Limits
        builder.Property(s => s.MaxVehicles).HasColumnName("max_vehicles");
        builder.Property(s => s.MaxUsers).HasColumnName("max_users");
        builder.Property(s => s.MaxGpsDevices).HasColumnName("max_gps_devices");
        builder.Property(s => s.MaxGeofences).HasColumnName("max_geofences");
        builder.Property(s => s.HistoryRetentionDays).HasColumnName("history_retention_days");

        // Features
        builder.Property(s => s.GpsTracking).HasColumnName("gps_tracking");
        builder.Property(s => s.GpsInstallation).HasColumnName("gps_installation");
        builder.Property(s => s.ApiAccess).HasColumnName("api_access");
        builder.Property(s => s.AdvancedReports).HasColumnName("advanced_reports");
        builder.Property(s => s.RealTimeAlerts).HasColumnName("real_time_alerts");
        builder.Property(s => s.HistoryPlayback).HasColumnName("history_playback");
        builder.Property(s => s.FuelAnalysis).HasColumnName("fuel_analysis");
        builder.Property(s => s.DrivingBehavior).HasColumnName("driving_behavior");

        builder.Property(s => s.SortOrder).HasColumnName("sort_order");
        builder.Property(s => s.IsActive).HasColumnName("is_active");
        builder.Property(s => s.AccessRights).HasColumnType("jsonb").HasColumnName("access_rights");

        // Granular Report Permissions
        builder.Property(s => s.ReportTrips).HasColumnName("report_trips");
        builder.Property(s => s.ReportFuel).HasColumnName("report_fuel");
        builder.Property(s => s.ReportSpeed).HasColumnName("report_speed");
        builder.Property(s => s.ReportStops).HasColumnName("report_stops");
        builder.Property(s => s.ReportMileage).HasColumnName("report_mileage");
        builder.Property(s => s.ReportCosts).HasColumnName("report_costs");
        builder.Property(s => s.ReportMaintenance).HasColumnName("report_maintenance");
        builder.Property(s => s.ReportDaily).HasColumnName("report_daily");
        builder.Property(s => s.ReportMonthly).HasColumnName("report_monthly");
        builder.Property(s => s.ReportMileagePeriod).HasColumnName("report_mileage_period");
        builder.Property(s => s.ReportSpeedInfraction).HasColumnName("report_speed_infraction");
        builder.Property(s => s.ReportDrivingBehavior).HasColumnName("report_driving_behavior");

        // Module Permissions
        builder.Property(s => s.ModuleDashboard).HasColumnName("module_dashboard");
        builder.Property(s => s.ModuleMonitoring).HasColumnName("module_monitoring");
        builder.Property(s => s.ModuleVehicles).HasColumnName("module_vehicles");
        builder.Property(s => s.ModuleEmployees).HasColumnName("module_employees");
        builder.Property(s => s.ModuleGeofences).HasColumnName("module_geofences");
        builder.Property(s => s.ModuleMaintenance).HasColumnName("module_maintenance");
        builder.Property(s => s.ModuleCosts).HasColumnName("module_costs");
        builder.Property(s => s.ModuleReports).HasColumnName("module_reports");
        builder.Property(s => s.ModuleSettings).HasColumnName("module_settings");
        builder.Property(s => s.ModuleUsers).HasColumnName("module_users");
        builder.Property(s => s.ModuleSuppliers).HasColumnName("module_suppliers");
        builder.Property(s => s.ModuleDocuments).HasColumnName("module_documents");
        builder.Property(s => s.ModuleAccidents).HasColumnName("module_accidents");
        builder.Property(s => s.ModuleFleetManagement).HasColumnName("module_fleet_management");

        // Timestamps
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
    }
}


