namespace GisAPI.Domain.Entities;

/// <summary>
/// Devis commercial créé par le sys_admin (prospect OU société existante).
/// Niveau plateforme : PAS de filtre tenant — accessible uniquement via
/// /api/admin/* (PermissionMiddleware). Les montants (HT, remise, TVA, TTC)
/// sont calculés à la lecture à partir des lignes, jamais stockés.
/// </summary>
public class Estimate
{
    public int Id { get; set; }

    /// <summary>Numéro lisible, unique, généré côté serveur (DEV-2026-0001).</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Société existante si le devis vise un client déjà en base.</summary>
    public int? CompanyId { get; set; }
    public Societe? Company { get; set; }

    public string ClientName { get; set; } = string.Empty;
    public string? ClientEmail { get; set; }
    public string? ClientPhone { get; set; }
    public string? ClientAddress { get; set; }

    /// <summary>draft, sent, accepted, rejected</summary>
    public string Status { get; set; } = "draft";

    public DateTime IssueDate { get; set; }
    public DateTime? ValidUntil { get; set; }

    public decimal DiscountPercent { get; set; }
    public decimal TaxPercent { get; set; } = 19m;

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<EstimateItem> Items { get; set; } = new List<EstimateItem>();
}

public class EstimateItem
{
    public int Id { get; set; }
    public int EstimateId { get; set; }
    public Estimate? Estimate { get; set; }

    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1m;
    public decimal UnitPrice { get; set; }
    public int SortOrder { get; set; }
}
