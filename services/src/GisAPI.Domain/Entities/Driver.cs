using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

/// <summary>
/// A driver is a standalone record (name, permit, contact info). Drivers do
/// not log in, do not have credentials, and do not consume a user seat.
/// They're just employees of the fleet that the company tracks for permit
/// expiry, vehicle assignment, trip attribution, scoring, etc.
/// </summary>
public class Driver : TenantEntity
{
    // Identity — previously lived on User, now native to Driver.
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    // Contact info (optional — the company may or may not need it).
    public string? Email { get; set; }
    public string? Phone { get; set; }

    // Permit & personal info.
    public string? PermitNumber { get; set; }
    public string? PermitType { get; set; }
    public DateTime? PermitExpiry { get; set; }
    public string? CIN { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public DateTime? HireDate { get; set; }

    // Default vehicle assignment (optional). We keep the scalar FK but NOT a
    // navigation property — Vehicle already has a scalar Driver nav pointing at
    // Driver, and having scalar navs on both sides of the FK confuses EF's
    // relationship discovery into treating it as an ambiguous 1-1. Consumers that
    // need the assigned vehicle must join on AssignedVehicleId explicitly.
    public int? AssignedVehicleId { get; set; }

    public string Status { get; set; } = "active";

    // Computed (not stored).
    public string FullName => $"{FirstName} {LastName}".Trim();
}
