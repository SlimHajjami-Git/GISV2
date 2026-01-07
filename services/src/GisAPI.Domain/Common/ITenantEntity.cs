namespace GisAPI.Domain.Common;

/// <summary>
/// Interface for multi-tenant entities that belong to a specific company
/// </summary>
public interface ITenantEntity
{
    int CompanyId { get; set; }
}
