using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class Driver : TenantEntity
{
    public int UserId { get; set; }
    public string? PermitNumber { get; set; }
    public string? PermitType { get; set; }
    public DateTime? PermitExpiry { get; set; }
    public string? CIN { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public DateTime? HireDate { get; set; }
    public int? AssignedVehicleId { get; set; }
    public string Status { get; set; } = "active";

    // Navigation
    public User User { get; set; } = null!;
    public Vehicle? AssignedVehicle { get; set; }
}
