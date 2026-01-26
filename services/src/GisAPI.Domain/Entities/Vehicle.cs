using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class Vehicle : TenantEntity
{
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
    public int? RentalMileage { get; set; }
    
    public int? AssignedDriverId { get; set; }
    public User? AssignedDriver { get; set; }
    
    public int? AssignedSupervisorId { get; set; }
    public User? AssignedSupervisor { get; set; }
    
    public int? GpsDeviceId { get; set; }
    public GpsDevice? GpsDevice { get; set; }

    public Societe? Societe { get; set; }
    
    public string? DriverName { get; set; }
    public string? DriverPhone { get; set; }
    
    // Document expiry dates
    public DateTime? InsuranceExpiry { get; set; }
    public DateTime? TechnicalInspectionExpiry { get; set; }
    public DateTime? TaxExpiry { get; set; }
    public DateTime? RegistrationExpiry { get; set; }
    public DateTime? TransportPermitExpiry { get; set; }

    public ICollection<VehicleDocument> Documents { get; set; } = new List<VehicleDocument>();
    public ICollection<MaintenanceRecord> MaintenanceRecords { get; set; } = new List<MaintenanceRecord>();
    public ICollection<VehicleCost> Costs { get; set; } = new List<VehicleCost>();
}

public class VehicleDocument : Entity
{
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public string? FileUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
