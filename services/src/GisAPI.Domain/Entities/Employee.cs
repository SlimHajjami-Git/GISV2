using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class Employee : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Role { get; set; } = "driver";
    public string Status { get; set; } = "active";
    public DateTime? HireDate { get; set; }
    public string? LicenseNumber { get; set; }
    public DateTime? LicenseExpiry { get; set; }

    public Company? Company { get; set; }
    
    public ICollection<Vehicle> AssignedVehiclesAsDriver { get; set; } = new List<Vehicle>();
    public ICollection<Vehicle> AssignedVehiclesAsSupervisor { get; set; } = new List<Vehicle>();
}
