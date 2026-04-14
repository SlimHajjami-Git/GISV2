using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class AlertEmail : TenantEntity
{
    public string Email { get; set; } = string.Empty;
    public string AlertType { get; set; } = string.Empty; // "assurance", "taxe_circulation", "visite_technique", "entretien"
}
