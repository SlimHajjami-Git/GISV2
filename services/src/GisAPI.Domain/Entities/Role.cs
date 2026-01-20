using GisAPI.Domain.Common;
using System.Text.Json;

namespace GisAPI.Domain.Entities;

public class Role : Entity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string RoleType { get; set; } = "employee"; // system_admin, company_admin, employee, custom
    public Dictionary<string, object>? Permissions { get; set; }
    public int? SocieteId { get; set; }
    public Societe? Societe { get; set; }
    public bool IsSystem { get; set; } = false;
    public bool IsDefault { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<User> Users { get; set; } = new List<User>();
}
