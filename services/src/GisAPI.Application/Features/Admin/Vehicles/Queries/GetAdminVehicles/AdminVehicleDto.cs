using System;

namespace GisAPI.Application.Features.Admin.Vehicles.Queries.GetAdminVehicles;

public class AdminVehicleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "camion";
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Plate { get; set; }
    public int? Year { get; set; }
    public string? Color { get; set; }
    public string Status { get; set; } = "available";
    public bool HasGps { get; set; }
    public int Mileage { get; set; }
    public string? FuelType { get; set; }
    public int CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public int? GpsDeviceId { get; set; }
    public string? GpsImei { get; set; }
    public string? GpsMat { get; set; }
    public string? GpsModel { get; set; }
    public string? GpsFirmwareVersion { get; set; }
    public int? AssignedDriverId { get; set; }
    public string? AssignedDriverName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
