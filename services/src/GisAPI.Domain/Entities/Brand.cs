using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class Brand : Entity
{
    public string Name { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public ICollection<VehicleModel> Models { get; set; } = new List<VehicleModel>();
}


