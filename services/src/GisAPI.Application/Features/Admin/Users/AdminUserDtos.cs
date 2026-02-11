namespace GisAPI.Application.Features.Admin.Users;

public class AdminUserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? CIN { get; set; }
    public int CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public int? RoleId { get; set; }
    public string? RoleName { get; set; }
    public string[] Roles { get; set; } = [];
    public string[] Permissions { get; set; } = [];
    public int[] AssignedVehicleIds { get; set; } = [];
    public string Status { get; set; } = "active";
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsOnline { get; set; }
}

public class UserStatsDto
{
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int SuspendedUsers { get; set; }
    public int OnlineUsers { get; set; }
    public List<CompanyUserCount> UsersByCompany { get; set; } = new();
}

public class CompanyUserCount
{
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public int UserCount { get; set; }
}
