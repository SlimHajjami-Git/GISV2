using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class VehicleModel : Entity
{
    public int BrandId { get; set; }
    public Brand? Brand { get; set; }
    
    public string Name { get; set; } = string.Empty;
    public string? VehicleType { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}


