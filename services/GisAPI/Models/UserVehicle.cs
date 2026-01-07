using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

/// <summary>
/// Pivot table for User-Vehicle many-to-many relationship
/// Replaces the AssignedVehicleIds array in User
/// </summary>
public class UserVehicle
{
    public int UserId { get; set; }

    public User? User { get; set; }

    public int VehicleId { get; set; }

    public Vehicle? Vehicle { get; set; }

    [MaxLength(50)]
    public string AccessLevel { get; set; } = "view"; // view, manage, full

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    public int? AssignedByUserId { get; set; }

    [ForeignKey("AssignedByUserId")]
    public User? AssignedByUser { get; set; }
}
