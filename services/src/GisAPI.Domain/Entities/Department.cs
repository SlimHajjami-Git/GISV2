using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class Department : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public Societe? Societe { get; set; }
    public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
}


