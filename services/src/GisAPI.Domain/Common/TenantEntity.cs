namespace GisAPI.Domain.Common;

/// <summary>
/// Base class for entities that belong to a specific tenant (company)
/// </summary>
public abstract class TenantEntity : AuditableEntity, ITenantEntity
{
    public int CompanyId { get; set; }
}
