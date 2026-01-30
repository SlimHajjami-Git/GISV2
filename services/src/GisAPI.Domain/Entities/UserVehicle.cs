using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class UserVehicle : Entity
{
    public int UserId { get; set; }
    public int VehicleId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public int? AssignedById { get; set; }

    // Navigation
    public User User { get; set; } = null!;
    public Vehicle Vehicle { get; set; } = null!;
    public User? AssignedBy { get; set; }
}


