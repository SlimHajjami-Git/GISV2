using GisAPI.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Common.Services;

/// <summary>
/// Single source of truth for per-user vehicle visibility, used by every endpoint
/// that returns per-vehicle data (vehicles list, monitoring, dashboard, échéances…).
///
/// EXPLICIT assignments are AUTHORITATIVE: a user who has rows in user_vehicles is
/// restricted to exactly those vehicles — even if their role was (auto-)promoted to
/// company_admin (granting all permissions does that). A user WITHOUT assignments
/// sees the whole company fleet when admin, and nothing when a normal user.
/// </summary>
public static class VehicleVisibility
{
    /// <summary>
    /// Returns the vehicle IDs the user may see:
    ///   - null            → no restriction (caller shows all company vehicles)
    ///   - non-empty list  → restrict to exactly these IDs
    ///   - empty list      → user sees no vehicle
    /// </summary>
    public static async Task<List<int>?> GetVisibleVehicleIdsAsync(
        IGisDbContext context, int userId, bool isAdmin, CancellationToken ct = default)
    {
        if (userId <= 0) return null;

        var assigned = await context.UserVehicles
            .Where(uv => uv.UserId == userId)
            .Select(uv => uv.VehicleId)
            .ToListAsync(ct);

        if (assigned.Count > 0) return assigned;   // assignment wins, even for admins
        return isAdmin ? null : new List<int>();   // admin = all; normal user = none
    }
}
