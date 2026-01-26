namespace GisAPI.Domain.Constants;

public static class Permissions
{
    public const string Admin = "admin";
    public const string Dashboard = "dashboard";
    public const string Monitoring = "monitoring";
    public const string Vehicles = "vehicles";
    public const string Geofences = "geofences";
    public const string Drivers = "drivers";
    public const string Employees = "employees";
    public const string GpsDevices = "gps-devices";
    public const string Maintenance = "maintenance";
    public const string Costs = "costs";
    public const string Reports = "reports";
    public const string MileageReports = "mileage-reports";
    public const string Settings = "settings";
    public const string Users = "users";

    public static readonly IReadOnlyCollection<string> All =
    [
        Admin,
        Dashboard,
        Monitoring,
        Vehicles,
        Geofences,
        Drivers,
        Employees,
        GpsDevices,
        Maintenance,
        Costs,
        Reports,
        MileageReports,
        Settings,
        Users
    ];
}
