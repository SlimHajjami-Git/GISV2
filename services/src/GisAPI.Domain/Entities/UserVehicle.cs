namespace GisAPI.Domain.Entities;

public class UserVehicle
{
    public int UserId { get; set; }
    public User? User { get; set; }
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public string AccessLevel { get; set; } = "view";
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public int? AssignedByUserId { get; set; }
    public User? AssignedByUser { get; set; }
}
