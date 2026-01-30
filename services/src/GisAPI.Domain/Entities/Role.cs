using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

/// <summary>
/// Rôle créé par une société ou rôle système.
/// - Rôles société: visibles uniquement par la société qui les a créés (SocieteId != null)
/// - Rôles système: pouvoir sur toute l'application (IsSystemRole = true, SocieteId = null)
/// </summary>
public class Role : Entity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? SocieteId { get; set; }
    public bool IsCompanyAdmin { get; set; } = false;
    public bool IsSystemRole { get; set; } = false;
    public Dictionary<string, object>? Permissions { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Societe? Societe { get; set; }
    public ICollection<User> Users { get; set; } = new List<User>();
    
    // Computed properties - any system role has absolute power
    public bool IsSystemAdmin => IsSystemRole;
}


