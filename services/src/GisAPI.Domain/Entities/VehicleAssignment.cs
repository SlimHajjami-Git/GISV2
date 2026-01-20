using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class VehicleAssignment : Entity
{
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    
    public int UserId { get; set; }
    public User? User { get; set; }
    
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UnassignedAt { get; set; }
    public bool IsActive { get; set; } = true;
    
    public string? AssignedBy { get; set; }
    public string? Notes { get; set; }
}
